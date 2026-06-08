namespace ZipPostLookup.CountryDataTools.Export.ZPimage;

/// <summary>
/// On-disk layout constants and section identifiers for the <b>ZP frozen image</b>
/// (<c>.zpi</c>) — a build-once, read-only binary representation of a single
/// country's postal data, designed to be loaded once into a single <c>byte[]</c>
/// (or memory-mapped) and queried zero-copy, with no CSV parsing and no per-row
/// object graph at load time.
///
/// <para><b>Why this format.</b> The runtime registry is dominated by one query —
/// exact code → entry — which must stay O(1) / zero-allocation. The expensive part
/// today is <i>construction</i> (parsing the embedded CSV and building the
/// <c>FrozenDictionary</c> indexes). This image removes the parse step entirely:
/// the primary index is a serialized minimal perfect hash over the codes, the tiny
/// timezone/admin1 enumerations are dictionary-encoded to single-byte indices, and
/// the names live in one deduplicated UTF-8 pool. A reader maps the bytes and reads
/// fields in place.</para>
///
/// <para><b>File layout</b> (all integers little-endian):</para>
/// <code>
///   ┌─ Header (32 bytes) ────────────────────────────────────────────────┐
///   │  0   magic        u8[4]  "ZPIM"                                     │
///   │  4   version      u16    = CurrentVersion                          │
///   │  6   flags        u16    (reserved; coords are always stripped)    │
///   │  8   country      u8[4]  ASCII, NUL-padded (e.g. "US\0\0")         │
///   │ 12   exactCount   u32    distinct exact codes = MPHF slots         │
///   │ 16   recordCount  u32    total records (exact rows + range rows)   │
///   │ 20   tzCount       u16   distinct timezones                        │
///   │ 22   adminCount    u16   distinct admin1 divisions                 │
///   │ 24   rangeCount    u32   range rows (e.g. CA FSA compression)      │
///   │ 28   sectionCount  u16                                             │
///   │ 30   reserved      u16                                             │
///   ├─ Section table (sectionCount × 12 bytes) ──────────────────────────┤
///   │  kind u16 │ reserved u16 │ offset u32 │ length u32                  │
///   ├─ Section payloads (each 8-byte aligned) ───────────────────────────┤
///   │  NamePool, TimezoneTable, AdminTable, Mphf, Directory, Records,     │
///   │  SortedSlots, TimezonePostings, AdminPostings, RangeTable           │
///   └────────────────────────────────────────────────────────────────────┘
/// </code>
///
/// <para><b>Section payloads.</b></para>
/// <list type="bullet">
/// <item><c>NamePool</c> — <c>count u32</c>, <c>offsets u32[count+1]</c>, then UTF-8 bytes.
///   String <c>i</c> is <c>bytes[offsets[i]..offsets[i+1]]</c>.</item>
/// <item><c>TimezoneTable</c> — <c>count u16</c>, then per entry <c>len u16</c> + UTF-8.</item>
/// <item><c>AdminTable</c> — <c>count u16</c>, then per entry
///   <c>codeLen u16</c>+code, <c>nameLen u16</c>+name.</item>
/// <item><c>Mphf</c> — <c>n u32</c>, then the displacement array <c>g i32[n]</c>
///   (see <see cref="MinimalPerfectHash"/>).</item>
/// <item><c>Directory</c> — <c>exactCount</c> × <see cref="DirectoryEntryBytes"/> (16 B), indexed by
///   MPHF slot: <c>packedCode u64</c>, then <c>firstRecord u32</c> (bit31 = Inline, bit30 =
///   InlineIsDefault, low 30 bits = record id). For a single-record code in a u16 image (Inline set)
///   the remaining bytes hold the inlined record: <c>tzIndex u8</c> @12, <c>adminIndex u8</c> @13,
///   <c>nameIndex u16</c> @14. For a multi-record code (Inline clear) bytes @12–13 are unused and
///   <c>count u16</c> sits @14. The inlined record is also present in <c>Records</c> (postings,
///   GetAll, and reverse lookups index it there); the directory copy only shortcuts <c>GetByCode</c>.</item>
/// <item><c>Records</c> — <c>recordCount</c> × <see cref="RecordBytes"/>:
///   <c>nameIndex u32</c>, <c>tzIndex u8</c>, <c>adminIndex u8</c>, <c>flags u8</c>, <c>reserved u8</c>.
///   A code's records are stored contiguously; the default entry is first.</item>
/// <item><c>SortedSlots</c> — <c>u32[exactCount]</c>: directory slots in ascending code order,
///   for ordered enumeration / binary search / <c>GetClosest</c>.</item>
/// <item><c>TimezonePostings</c> / <c>AdminPostings</c> — CSR reverse indexes:
///   <c>count u16</c>, <c>starts u32[count+1]</c>, <c>ids u32[recordCount]</c>.</item>
/// <item><c>RangeTable</c> — <c>count u32</c>, then per entry
///   <c>codeIndex u32</c>, <c>fsaIndex u32</c>, <c>start4Index u32</c>, <c>end4Index u32</c>,
///   <c>recordId u32</c> (string indices into <c>NamePool</c>; <c>codeIndex</c> is the original
///   range notation, e.g. "B2G0**:B2G9**").</item>
/// </list>
///
/// <para>The whole image is intended to be Brotli-compressed at rest and decompressed
/// once into a single <c>byte[]</c> at load — see <see cref="ZpImageWriter"/>.</para>
/// </summary>
internal static class ZpImageFormat
{
    /// <summary>Four-byte file signature: ASCII "ZPIM".</summary>
    public static readonly byte[] Magic = "ZPIM"u8.ToArray();

    /// <summary>
    /// Current image format version.
    /// <para>v3: the MPHF evaluation changed — a single seed-independent FNV pass reused for both
    /// reductions, and modulo replaced by fastrange (see <see cref="MinimalPerfectHash"/>).</para>
    /// <para>v4: <b>fused directory</b>. The Directory entry is unchanged in size (16 bytes) but the
    /// <c>firstRecord</c>/<c>count</c>/<c>reserved</c> fields are repacked so a single-record code
    /// (<c>count == 1</c>) in a u16 image inlines its record's name/timezone/admin into the directory
    /// entry. <c>GetByCode</c> then materialises straight from the directory without a second fetch
    /// into the Records section. The record still lives in Records (postings, GetAll, reverse lookups
    /// are unchanged). u32 images never inline. v3 readers would misread the directory, so the
    /// version is bumped and the reader rejects mismatches.</para>
    /// </summary>
    public const ushort CurrentVersion = 4;

    /// <summary>Fixed header size in bytes, before the section table.</summary>
    public const int HeaderBytes = 32;

    /// <summary>Size of one section-table entry: kind u16, reserved u16, offset u32, length u32.</summary>
    public const int SectionEntryBytes = 12;

    /// <summary>Fixed size of a <c>Records</c> entry when nameIndex is u16 (normal datasets, .u16.zpi.br).</summary>
    public const int RecordBytes = 6;

    /// <summary>Fixed size of a <c>Records</c> entry when nameIndex is u32 (large datasets > 65k names, .u32.zpi.br).</summary>
    public const int RecordBytesU32 = 8;

    /// <summary>Fixed size of a <c>Directory</c> entry.</summary>
    public const int DirectoryEntryBytes = 16;

    /// <summary>Fixed size of a <c>RangeTable</c> entry.</summary>
    public const int RangeEntryBytes = 20;

    /// <summary>Alignment applied to the start of every section payload.</summary>
    public const int SectionAlignment = 8;

    // Record field offsets within a RecordBytes-sized entry:
    /// <summary>Offset of nameIndex (u16) within a record.</summary>
    public const int RecordNameOffset  = 0;
    /// <summary>Offset of tzIndex (u8) within a record.</summary>
    public const int RecordTzOffset    = 2;
    /// <summary>Offset of adminIndex (u8) within a record.</summary>
    public const int RecordAdminOffset = 3;
    /// <summary>Offset of flags (u8) within a record.</summary>
    public const int RecordFlagsOffset = 4;

    // ── v4 Directory entry field offsets (entry stays DirectoryEntryBytes = 16) ──
    // [0..8) packedCode u64 · [8..12) firstRecord u32 (bit31 Inline, bit30 InlineIsDefault,
    // low 30 bits record id) · [12] inline tzIndex · [13] inline adminIndex ·
    // [14..16) inline nameIndex (u16) OR record count.
    /// <summary>Offset of the firstRecord u32 (carries the inline flags in its high bits).</summary>
    public const int DirFirstOffset       = 8;
    /// <summary>Offset of the inline timezone index (inline entries only).</summary>
    public const int DirInlineTzOffset    = 12;
    /// <summary>Offset of the inline admin index (inline entries only).</summary>
    public const int DirInlineAdminOffset = 13;
    /// <summary>Offset of the u16 that is the inline nameIndex (inline) or the record count (multi).</summary>
    public const int DirCountOrNameOffset = 14;

    /// <summary>firstRecord bit31 — this directory entry inlines its single record.</summary>
    public const uint DirInlineFlag    = 0x8000_0000u;
    /// <summary>firstRecord bit30 — the inline record is the default entry.</summary>
    public const uint DirInlineDefault = 0x4000_0000u;
    /// <summary>Mask for the low 30 bits of firstRecord that hold the record id.</summary>
    public const uint DirRecordIdMask  = 0x3FFF_FFFFu;

    /// <summary>Record flag bits.</summary>
    [Flags]
    public enum RecordFlags : byte
    {
        None      = 0,
        IsDefault = 1 << 0,
        IsRange   = 1 << 1,
    }

    /// <summary>Identifies a section payload in the section table.</summary>
    public enum SectionKind : ushort
    {
        NamePool         = 1,
        TimezoneTable    = 2,
        AdminTable       = 3,
        Mphf             = 4,
        Directory        = 5,
        Records          = 6,
        SortedSlots      = 7,
        TimezonePostings = 8,
        AdminPostings    = 9,
        RangeTable       = 10,
    }

    /// <summary>Rounds <paramref name="value"/> up to the next multiple of <paramref name="alignment"/>.</summary>
    public static int Align(int value, int alignment) =>
        (value + alignment - 1) / alignment * alignment;
}
