using static ZipPostLookup.CountryDataTools.Export.ZPimage.ZpImageFormat;

namespace ZipPostLookup.CountryDataTools.Export.ZPimage;

/// <summary>
/// Result of building a ZP frozen image: the raw (uncompressed) image bytes plus the
/// counts that went into it, for logging.
/// </summary>
internal sealed record ZpImageBuildResult(
    byte[] Bytes,
    string Country,
    int    ExactCodes,
    int    RecordCount,
    int    TimezoneCount,
    int    AdminCount,
    int    RangeCount,
    int    NameCount,
    bool   UsedU32NameIndex,
    string OutputPath = "");

/// <summary>
/// Builds a <see cref="ZpImageFormat">ZP frozen image</see> from the same
/// <see cref="ExportRow"/> stream the CSV exporter consumes.
///
/// <para>The build mirrors how <c>ZipPostRegistry</c> ingests data:</para>
/// <list type="number">
/// <item>Range rows (code contains <c>':'</c>) are routed to the range table, exact rows to
///   the primary code index — exactly as <c>LoadSource</c> / <c>LoadRangeEntry</c> split them.</item>
/// <item>Timezone and admin1 are dictionary-encoded to single-byte indices (reusing
///   <see cref="ExportMeta"/>'s tables when present, otherwise derived the same way
///   <c>StringIndexStage</c> derives them).</item>
/// <item>Each code's rows are stored contiguously with the default entry first (matching
///   <c>AddToIndex</c>'s "default at index 0" behaviour).</item>
/// <item>A minimal perfect hash over the packed codes gives O(1) code → directory slot.</item>
/// </list>
///
/// <para>After assembly the builder <b>verifies</b> the image in memory — every exact code is
/// re-resolved through the MPHF and checked against the directory — and throws if anything is
/// inconsistent, so a corrupt image can never be written.</para>
/// </summary>
internal static class ZpImageBuilder
{
    private struct BuiltRecord
    {
        public int          NameIndex;
        public byte         TimezoneIndex;
        public byte         AdminIndex;
        public RecordFlags  Flags;
    }

    public static ZpImageBuildResult Build(
        string countryCode,
        IReadOnlyList<ExportRow> rows,
        ExportMeta? meta = null)
    {
        var country = countryCode.ToUpperInvariant();

        var (tzTable, tzToIndex)       = BuildTimezoneTable(rows, meta);
        var (adminTable, adminToIndex) = BuildAdminTable(rows, meta);

        if (tzTable.Length > 256)
        {
            throw new InvalidOperationException(
                $"Timezone table size {tzTable.Length} exceeds the single-byte index limit (256).");
        }

        if (adminTable.Length > 256)
        {
            throw new InvalidOperationException(
                $"Admin1 division table size {adminTable.Length} exceeds the single-byte index limit (256).");
        }

        var names    = new StringPool();
        var records  = new List<BuiltRecord>(rows.Count);

        byte ResolveTz(string tz)       => (byte)(tzToIndex.TryGetValue(tz, out var i) ? i : 0);
        byte ResolveAdmin(string code)  => (byte)(adminToIndex.TryGetValue(code, out var i) ? i : 0);

        // ── Split exact vs range rows ─────────────────────────────────────────
        var exactRows = new List<ExportRow>(rows.Count);
        var rangeRows = new List<ExportRow>();
        foreach (var row in rows)
        {
            (row.ZpCode.Contains(':') ? rangeRows : exactRows).Add(row);
        }

        // ── Group exact rows by code, default-first, in ordinal code order ────
        var groups = new SortedDictionary<string, List<ExportRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in exactRows)
        {
            if (!groups.TryGetValue(row.ZpCode, out var bucket))
            {
                bucket = new List<ExportRow>(1);
                groups[row.ZpCode] = bucket;
            }

            bucket.Add(row);
        }

        var codeCount   = groups.Count;
        var codesSorted = new string[codeCount];
        var packedCodes = new List<ulong>(codeCount);
        var firstRecord = new int[codeCount];
        var groupCount  = new int[codeCount];

        var ci = 0;
        foreach (var (code, bucket) in groups)
        {
            codesSorted[ci] = code;
            packedCodes.Add(CodePacker.Pack(code));
            firstRecord[ci] = records.Count;
            groupCount[ci]  = bucket.Count;

            // Default entry first, then remaining rows in their original order.
            bucket.Sort((a, b) => b.IsDefault.CompareTo(a.IsDefault));
            foreach (var row in bucket)
            {
                records.Add(new BuiltRecord
                {
                    NameIndex     = names.Intern(row.PlaceName),
                    TimezoneIndex = ResolveTz(row.Timezone),
                    AdminIndex    = ResolveAdmin(row.Admin1Code),
                    Flags         = row.IsDefault ? RecordFlags.IsDefault : RecordFlags.None,
                });
            }

            ci++;
        }

        // ── Range rows → records + range-table entries ────────────────────────
        var rangeEntries = new List<(int Code, int Fsa, int Start4, int End4, int RecordId)>(rangeRows.Count);
        foreach (var row in rangeRows)
        {
            if (!TryParseRange(row.ZpCode, out var fsa, out var start4, out var end4))
            {
                continue; // malformed range row — dropped, exactly as LoadRangeEntry does
            }

            var recordId = records.Count;
            records.Add(new BuiltRecord
            {
                NameIndex     = names.Intern(row.PlaceName),
                TimezoneIndex = ResolveTz(row.Timezone),
                AdminIndex    = ResolveAdmin(row.Admin1Code),
                Flags         = RecordFlags.IsRange | (row.IsDefault ? RecordFlags.IsDefault : RecordFlags.None),
            });

            // The original range notation (e.g. "B2G0**:B2G9**") is interned verbatim so the
            // reader can reproduce CodeEntry.ZpCode exactly — it cannot be packed/unpacked.
            rangeEntries.Add((
                names.Intern(row.ZpCode),
                names.Intern(fsa),
                names.Intern(start4),
                names.Intern(end4),
                recordId));
        }

        rangeEntries.Sort((a, b) =>
        {
            // Deterministic order; fsa/start4 indices follow interning order, so sort by the
            // resolved record id which is stable with input order within a code prefix.
            return a.RecordId.CompareTo(b.RecordId);
        });

        // ── Primary index: minimal perfect hash over the packed codes ─────────
        var mphf       = MinimalPerfectHash.Build(packedCodes);
        var directory  = new (ulong Packed, int First, int Count)[codeCount];
        var slotFilled = new bool[codeCount];
        var sortedSlots = new int[codeCount];

        for (var i = 0; i < codeCount; i++)
        {
            var slot = mphf.Evaluate(packedCodes[i]);
            if (slot < 0 || slot >= codeCount || slotFilled[slot])
            {
                throw new InvalidOperationException(
                    $"MPHF produced an invalid/duplicate slot {slot} for code '{codesSorted[i]}'.");
            }

            slotFilled[slot]  = true;
            directory[slot]   = (packedCodes[i], firstRecord[i], groupCount[i]);
            sortedSlots[i]    = slot;
        }

        // ── Auto-promote to u32 nameIndex when the pool exceeds the u16 range ──
        // The writer encodes the chosen width in the output filename (.u16.zpi.br / .u32.zpi.br)
        // so the reader can load the correct stride without a format version bump.
        var u32NameIndex = names.Count > 65_535;
        if (u32NameIndex)
        {
            Console.WriteLine($"  ⚠ NamePool has {names.Count:N0} entries — using u32 nameIndex (.u32.zpi.br).");
        }

        // ── Serialise every section, then assemble with the header ────────────
        var sections = new List<(SectionKind Kind, byte[] Data)>
        {
            (SectionKind.NamePool,         names.Serialize()),
            (SectionKind.TimezoneTable,    SerializeTimezoneTable(tzTable)),
            (SectionKind.AdminTable,       SerializeAdminTable(adminTable)),
            (SectionKind.Mphf,             SerializeMphf(mphf)),
            (SectionKind.Directory,        SerializeDirectory(directory, records, u32NameIndex)),
            (SectionKind.Records,          SerializeRecords(records, u32NameIndex)),
            (SectionKind.SortedSlots,      SerializeSortedSlots(sortedSlots)),
            (SectionKind.TimezonePostings, SerializePostings(records, tzTable.Length, r => r.TimezoneIndex)),
            (SectionKind.AdminPostings,    SerializePostings(records, adminTable.Length, r => r.AdminIndex)),
            (SectionKind.RangeTable,       SerializeRangeTable(rangeEntries)),
        };

        var bytes = Assemble(country, codeCount, records.Count, tzTable.Length,
                             adminTable.Length, rangeEntries.Count, sections);

        // ── Self-verify before handing the bytes back ─────────────────────────
        Verify(codesSorted, packedCodes, mphf, directory, records.Count);

        return new ZpImageBuildResult(
            bytes, country, codeCount, records.Count,
            tzTable.Length, adminTable.Length, rangeEntries.Count, names.Count,
            UsedU32NameIndex: u32NameIndex);
    }

    // =========================================================================
    // Verification
    // =========================================================================

    private static void Verify(
        string[] codesSorted,
        IReadOnlyList<ulong> packedCodes,
        MinimalPerfectHash mphf,
        (ulong Packed, int First, int Count)[] directory,
        int recordCount)
    {
        if (mphf.SlotCount != directory.Length)
        {
            throw new InvalidOperationException(
                $"MPHF slot count {mphf.SlotCount} != directory length {directory.Length}.");
        }

        for (var i = 0; i < codesSorted.Length; i++)
        {
            var slot = mphf.Evaluate(packedCodes[i]);
            var entry = directory[slot];

            if (entry.Packed != packedCodes[i])
            {
                throw new InvalidOperationException(
                    $"Verification failed: code '{codesSorted[i]}' resolved to slot {slot} " +
                    $"holding a different code.");
            }

            if (entry.First < 0 || entry.Count <= 0 || entry.First + entry.Count > recordCount)
            {
                throw new InvalidOperationException(
                    $"Verification failed: code '{codesSorted[i]}' has an out-of-range record span " +
                    $"[{entry.First}, {entry.First + entry.Count}) over {recordCount} records.");
            }

            // Round-trip the packed key back to its string form.
            if (!string.Equals(CodePacker.Unpack(packedCodes[i]), codesSorted[i],
                               StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Verification failed: code '{codesSorted[i]}' does not round-trip through CodePacker.");
            }
        }
    }

    // =========================================================================
    // Section serialisers
    // =========================================================================

    private static byte[] SerializeTimezoneTable(string[] table)
    {
        var w = new BlobWriter();
        w.U16((ushort)table.Length);
        foreach (var tz in table)
        {
            w.Utf8WithU16Length(tz);
        }

        return w.ToArray();
    }

    private static byte[] SerializeAdminTable((string Code, string Name)[] table)
    {
        var w = new BlobWriter();
        w.U16((ushort)table.Length);
        foreach (var (code, name) in table)
        {
            w.Utf8WithU16Length(code);
            w.Utf8WithU16Length(name);
        }

        return w.ToArray();
    }

    private static byte[] SerializeMphf(MinimalPerfectHash mphf)
    {
        var w = new BlobWriter(capacity: 4 + mphf.Displacements.Count * 4);
        w.U32((uint)mphf.SlotCount);
        foreach (var d in mphf.Displacements)
        {
            w.I32(d);
        }

        return w.ToArray();
    }

    /// <summary>
    /// Serialises the v4 directory. A single-record code in a u16 image inlines its record's
    /// name/timezone/admin into the entry (Inline flag set in firstRecord's high bit) so the reader
    /// can satisfy <c>GetByCode</c> without a second fetch into the Records section. Multi-record
    /// codes, and every entry in a u32 image, store <c>firstRecord</c> + <c>count</c> as before.
    /// The inlined record is still emitted into the Records section — this is a read shortcut, not a
    /// relocation — so postings, GetAll, and reverse lookups are unaffected.
    /// </summary>
    private static byte[] SerializeDirectory(
        (ulong Packed, int First, int Count)[] directory,
        List<BuiltRecord> records,
        bool u32NameIndex)
    {
        var w = new BlobWriter(capacity: directory.Length * DirectoryEntryBytes);
        foreach (var (packed, first, count) in directory)
        {
            w.U64(packed);

            // Inline only when the single record's nameIndex fits the u16 slot and the record id
            // fits the 30-bit field (always true in practice; guarded for safety).
            var canInline = !u32NameIndex && count == 1 && (uint)first <= DirRecordIdMask;
            if (canInline)
            {
                var rec = records[first];
                var fr  = (uint)first | DirInlineFlag;
                if ((rec.Flags & RecordFlags.IsDefault) != 0)
                {
                    fr |= DirInlineDefault;
                }

                w.U32(fr);                       // [8..12) firstRecord + inline flags
                w.U8(rec.TimezoneIndex);         // [12]    inline tzIndex
                w.U8(rec.AdminIndex);            // [13]    inline adminIndex
                w.U16((ushort)rec.NameIndex);   // [14..16) inline nameIndex
            }
            else
            {
                w.U32((uint)first);              // [8..12) firstRecord (Inline bit clear)
                w.U8(0);                         // [12]    unused
                w.U8(0);                         // [13]    unused
                w.U16((ushort)count);           // [14..16) record count
            }
        }

        return w.ToArray();
    }

    private static byte[] SerializeRecords(List<BuiltRecord> records, bool u32NameIndex)
    {
        var stride = u32NameIndex ? RecordBytesU32 : RecordBytes;
        var w = new BlobWriter(capacity: records.Count * stride);
        foreach (var r in records)
        {
            if (u32NameIndex)
                w.U32((uint)r.NameIndex); // large dataset: u32 nameIndex → .u32.zpi.br
            else
                w.U16((ushort)r.NameIndex); // normal: u16 nameIndex → .u16.zpi.br
            w.U8(r.TimezoneIndex);
            w.U8(r.AdminIndex);
            w.U8((byte)r.Flags);
            w.U8(0); // reserved / pad
        }

        return w.ToArray();
    }

    private static byte[] SerializeSortedSlots(int[] sortedSlots)
    {
        var w = new BlobWriter(capacity: sortedSlots.Length * 4);
        foreach (var slot in sortedSlots)
        {
            w.U32((uint)slot);
        }

        return w.ToArray();
    }

    private static byte[] SerializePostings(
        List<BuiltRecord> records, int bucketCount, Func<BuiltRecord, int> selector)
    {
        // CSR: count, starts[count+1], ids[recordCount] grouped by bucket, ascending record id.
        var counts = new int[bucketCount];
        foreach (var r in records)
        {
            counts[selector(r)]++;
        }

        var starts = new int[bucketCount + 1];
        for (var i = 0; i < bucketCount; i++)
        {
            starts[i + 1] = starts[i] + counts[i];
        }

        var ids    = new uint[records.Count];
        var cursor = (int[])starts.Clone();
        for (var r = 0; r < records.Count; r++)
        {
            var bucket = selector(records[r]);
            ids[cursor[bucket]++] = (uint)r;
        }

        var w = new BlobWriter(capacity: 2 + (bucketCount + 1) * 4 + records.Count * 4);
        w.U16((ushort)bucketCount);
        foreach (var s in starts)
        {
            w.U32((uint)s);
        }

        foreach (var id in ids)
        {
            w.U32(id);
        }

        return w.ToArray();
    }

    private static byte[] SerializeRangeTable(
        List<(int Code, int Fsa, int Start4, int End4, int RecordId)> entries)
    {
        var w = new BlobWriter(capacity: 4 + entries.Count * RangeEntryBytes);
        w.U32((uint)entries.Count);
        foreach (var (code, fsa, start4, end4, recordId) in entries)
        {
            w.U32((uint)code);
            w.U32((uint)fsa);
            w.U32((uint)start4);
            w.U32((uint)end4);
            w.U32((uint)recordId);
        }

        return w.ToArray();
    }

    // =========================================================================
    // Header + section-table assembly
    // =========================================================================

    private static byte[] Assemble(
        string country,
        int exactCount,
        int recordCount,
        int tzCount,
        int adminCount,
        int rangeCount,
        List<(SectionKind Kind, byte[] Data)> sections)
    {
        var headerSize = HeaderBytes + sections.Count * SectionEntryBytes;

        // Lay out each section payload 8-byte aligned after the header/table.
        var offsets = new int[sections.Count];
        var offset  = Align(headerSize, SectionAlignment);
        for (var i = 0; i < sections.Count; i++)
        {
            offsets[i] = offset;
            offset = Align(offset + sections[i].Data.Length, SectionAlignment);
        }

        var image = new byte[offset];
        var w     = new BlobWriter(capacity: headerSize);

        // Header
        w.Raw(Magic);
        w.U16(CurrentVersion);
        w.U16(0);                              // flags (reserved)
        w.Raw(CountryBytes(country));
        w.U32((uint)exactCount);
        w.U32((uint)recordCount);
        w.U16((ushort)tzCount);
        w.U16((ushort)adminCount);
        w.U32((uint)rangeCount);
        w.U16((ushort)sections.Count);
        w.U16(0);                              // reserved

        // Section table
        for (var i = 0; i < sections.Count; i++)
        {
            w.U16((ushort)sections[i].Kind);
            w.U16(0);
            w.U32((uint)offsets[i]);
            w.U32((uint)sections[i].Data.Length);
        }

        var header = w.ToArray();
        Buffer.BlockCopy(header, 0, image, 0, header.Length);

        // Payloads
        for (var i = 0; i < sections.Count; i++)
        {
            Buffer.BlockCopy(sections[i].Data, 0, image, offsets[i], sections[i].Data.Length);
        }

        return image;
    }

    private static byte[] CountryBytes(string country)
    {
        var buf = new byte[4];
        for (var i = 0; i < country.Length && i < 4; i++)
        {
            buf[i] = (byte)country[i];
        }

        return buf;
    }

    // =========================================================================
    // Enumeration tables (reuse ExportMeta when present, else derive)
    // =========================================================================

    private static (string[] Table, Dictionary<string, int> ToIndex) BuildTimezoneTable(
        IReadOnlyList<ExportRow> rows, ExportMeta? meta)
    {
        string[] table;
        if (meta?.TimezoneIndex is { } provided)
        {
            table = provided;
        }
        else
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                set.Add(r.Timezone);
            }

            table = set.ToArray();
        }

        var map = new Dictionary<string, int>(table.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < table.Length; i++)
        {
            map[table[i]] = i;
        }

        return (table, map);
    }

    private static ((string Code, string Name)[] Table, Dictionary<string, int> ToIndex) BuildAdminTable(
        IReadOnlyList<ExportRow> rows, ExportMeta? meta)
    {
        (string Code, string Name)[] table;
        if (meta?.AdminIndex is { } provided)
        {
            table = provided;
        }
        else
        {
            var seen = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                if (!seen.ContainsKey(r.Admin1Code))
                {
                    seen[r.Admin1Code] = r.Admin1;
                }
            }

            table = seen.Select(kv => (Code: kv.Key, Name: kv.Value)).ToArray();
        }

        var map = new Dictionary<string, int>(table.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < table.Length; i++)
        {
            map[table[i].Code] = i;
        }

        return (table, map);
    }

    // =========================================================================
    // Range parsing — mirrors ZipPostRegistry.LoadRangeEntry
    // =========================================================================

    private static bool TryParseRange(string code, out string fsa, out string start4, out string end4)
    {
        fsa = start4 = end4 = string.Empty;

        var colon = code.IndexOf(':');
        if (colon < 0)
        {
            return false;
        }

        start4 = code[..colon].Replace("*", "");
        end4   = code[(colon + 1)..].Replace("*", "");

        if (start4.Length < 3 || end4.Length < 3)
        {
            return false;
        }

        fsa = start4[..3];
        return true;
    }
}
