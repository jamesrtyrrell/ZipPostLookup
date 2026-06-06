#if NET8_0_OR_GREATER
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using ZipPostLookup.Core;
using static ZipPostLookup.ZPImage.ZpImageFormat;

namespace ZipPostLookup.ZPImage;

/// <summary>
/// Zero-parse reader over a <see cref="ZpImageFormat">ZP frozen image</see>. Construction maps
/// the bytes and decodes only the two tiny enumeration tables (timezones, admin1); everything
/// else is read in place on demand. Code lookups resolve through the embedded minimal perfect
/// hash in O(1) with no allocation.
///
/// <para><b>Entry materialisation.</b> The public API hands back <see cref="CodeEntry"/> records,
/// which are heap objects. To keep repeated lookups allocation-free without paying the registry's
/// up-front cost, each record's <see cref="CodeEntry"/> is built on first access and cached by
/// record id. So the <i>first</i> lookup of a given code allocates; subsequent lookups return the
/// cached reference. The cache is one <c>CodeEntry?[recordCount]</c> array sized at load.</para>
///
/// <para>This type is available on .NET 8.0+ only (it relies on spans, BinaryPrimitives, and
/// BrotliStream).</para>
/// </summary>
internal sealed class ZpImageReader
{
    private readonly byte[] _image;
    private readonly string _levelName;

    private readonly int[] _sectionOffset = new int[16];
    private readonly int[] _sectionLength = new int[16];

    private readonly int _exactCount;
    private readonly int _recordCount;

    private readonly string[] _timezones;
    private readonly (string Code, string Name)[] _admins;

    // Constant-time reverse-attribute lookups, built once at construction. Replace the former O(n)
    // linear scans over _timezones / _admins on every GetByTimeZone / GetByState / GetByStateName /
    // GetByAdmin call (the cause of the disproportionately slow MX reverse-lookup benchmarks).
    private readonly Dictionary<string, int> _timezoneIndex;
    private readonly Dictionary<string, int> _adminCodeIndex;
    private readonly Dictionary<string, int> _adminNameIndex;

    // Section bases cached for the hot path.
    private readonly int _mphfBase;       // points at the n u32; G follows at +4
    private readonly int _directoryBase;
    private readonly int _recordsBase;
    private readonly int _namePoolOffsets; // offsets[count+1] base
    private readonly int _namePoolBytes;   // utf-8 bytes base

    // Piece 1: NamePool decoded once at load — Name(idx) is a zero-alloc array lookup.
    private readonly string[] _namePool;

    // Piece 2: AdminLevel arrays pre-built per admin index — Materialize references these
    //          directly instead of allocating new[] { new AdminLevel(...) } per call.
    private readonly AdminLevel[][] _adminLevels;

    private readonly CodeEntry?[] _entryCache;

    // Filename-encoded record format: false = nameIndex u16 (RecordBytes = 6, .u16.zpi.br),
    //                                 true  = nameIndex u32 (RecordBytesU32 = 8, .u32.zpi.br).
    private readonly bool _u32NameIndex;
    private readonly int  _recordBytes;

    // Lazily-built auxiliary indexes (only the primary code index is needed up front).
    private Dictionary<string, List<int>>? _nameIndex;
    private Dictionary<string, List<int>>? _rangeByFsa;
    private int[]? _sortedUsZipInts;
    private int[]? _recordToSlot; // exact record id → owning directory slot (range records map to -1)
    private Dictionary<int, string>? _rangeCodeByRecord; // range record id → original range notation
    private IReadOnlyList<CodeEntry>? _all;

    // Reverse-lookup result caches. A reverse query returns the full set of CodeEntry for an
    // attribute value; without memoisation that set is re-materialised into a fresh array on every
    // call (an O(results) array build that lands on the large-object heap for big CA buckets — the
    // CSV registry, by contrast, hands back a precomputed list reference). These cache the built
    // result per posting bucket / name so repeat queries are O(1), matching the registry's profile.
    private IReadOnlyList<CodeEntry>?[]? _tzPostingCache;     // indexed by timezone table index
    private IReadOnlyList<CodeEntry>?[]? _adminPostingCache;  // indexed by admin1 table index
    private ConcurrentDictionary<string, IReadOnlyList<CodeEntry>>? _nameResultCache;

    public string Country { get; }
    public int ExactCodeCount => _exactCount;
    public int RecordCount => _recordCount;

    public ZpImageReader(byte[] image, string levelName, bool u32NameIndex = false, bool validate = false)
    {
        _image         = image ?? throw new ArgumentNullException(nameof(image));
        _levelName     = string.IsNullOrEmpty(levelName) ? "Admin1" : levelName;
        _u32NameIndex  = u32NameIndex;
        _recordBytes   = u32NameIndex ? RecordBytesU32 : RecordBytes;

        if (image.Length < HeaderBytes || !image.AsSpan(0, 4).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a ZP image (bad magic).");
        }

        var version = ReadU16(4);
        if (version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported ZP image version {version} (reader supports {CurrentVersion}).");
        }

        Country      = ReadCountry(8);
        _exactCount  = (int)ReadU32(12);
        _recordCount = (int)ReadU32(16);
        var sectionCount = ReadU16(28);

        var tablePos = HeaderBytes;
        for (var i = 0; i < sectionCount; i++)
        {
            var kind   = ReadU16(tablePos);
            var offset = (int)ReadU32(tablePos + 4);
            var length = (int)ReadU32(tablePos + 8);
            if (kind < _sectionOffset.Length)
            {
                _sectionOffset[kind] = offset;
                _sectionLength[kind] = length;
            }

            tablePos += SectionEntryBytes;
        }

        // Guard against a stale/incompatible image. The on-disk version may match while the
        // RangeTable entry layout has changed (e.g. a field added → RangeEntryBytes grew). The
        // header carries the authoritative range-row count at offset 24; if the section size does
        // not match this reader's stride, every range read would walk off into garbage and crash
        // deep in a string decode. Fail fast with an actionable message instead.
        var headerRangeCount = (int)ReadU32(24);
        var rangeTableLength = _sectionLength[(int)SectionKind.RangeTable];
        if (headerRangeCount > 0 && rangeTableLength != 4 + headerRangeCount * RangeEntryBytes)
        {
            throw new InvalidDataException(
                $"ZP image range table is {rangeTableLength} bytes for {headerRangeCount} range rows, " +
                $"but this reader expects {4 + headerRangeCount * RangeEntryBytes} " +
                $"({RangeEntryBytes} bytes/entry). The image was built with an incompatible writer — " +
                $"regenerate it with 'countrydatatools export --country {Country} --target zpi'.");
        }

        _mphfBase      = Off(SectionKind.Mphf);
        _directoryBase = Off(SectionKind.Directory);
        _recordsBase   = Off(SectionKind.Records);

        var namePoolBase = Off(SectionKind.NamePool);
        var poolCount    = (int)ReadU32(namePoolBase);
        _namePoolOffsets = namePoolBase + 4;
        _namePoolBytes   = _namePoolOffsets + (poolCount + 1) * 4;

        // Untrusted-image safety: bounds-check the structure before any decoder or the unchecked
        // Unsafe hot path touches it. Runs on raw offsets only (no decoded tables needed yet).
        if (validate)
        {
            Validate();
        }

        _timezones   = DecodeTimezones();
        _admins      = DecodeAdmins();

        // Constant-time reverse-attribute lookups (replace per-call linear scans). First occurrence
        // wins, matching the prior IndexOf semantics for any duplicate code/name/timezone values.
        _timezoneIndex  = BuildValueIndex(_timezones);
        _adminCodeIndex = BuildAdminIndex(selectCode: true);
        _adminNameIndex = BuildAdminIndex(selectCode: false);

        // Piece 1 — decode entire NamePool up-front; Name(idx) becomes a zero-alloc array lookup.
        _namePool    = DecodeNamePool();

        // Piece 2 — pre-build one AdminLevel[] per admin division; shared across all Materialize calls.
        _adminLevels = BuildAdminLevels();

        // Piece 3 — skip zero-initialisation of the cache array (saves ~3.4 MB for CA).
        _entryCache  = GC.AllocateUninitializedArray<CodeEntry?>(_recordCount);
    }

    // =========================================================================
    // Loading
    // =========================================================================

    /// <summary>Loads a reader from raw (uncompressed) image bytes.</summary>
    public static ZpImageReader Load(byte[] image, string levelName, bool u32NameIndex = false,
        bool validate = false) =>
        new(image, levelName, u32NameIndex, validate);

    /// <summary>
    /// Loads a reader from a Brotli-compressed image stream.
    /// The stream must begin with a 4-byte little-endian uncompressed size prefix
    /// (written by <c>ZpImageWriter</c>) so the reader can pre-allocate an exact buffer
    /// instead of using a growing <c>MemoryStream</c>.
    /// </summary>
    public static ZpImageReader LoadCompressed(Stream compressed, string levelName,
        bool u32NameIndex = false, bool validate = false)
    {
        // Piece 4 — read the 4-byte uncompressed-size prefix, then decompress directly
        // into an exactly-sized array.  No MemoryStream, no ToArray() copy.
        Span<byte> sizeBuf = stackalloc byte[4];
        compressed.ReadExactly(sizeBuf);
        var uncompressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(sizeBuf);

        var image = GC.AllocateUninitializedArray<byte>(uncompressedSize);
        using var brotli = new BrotliStream(compressed, CompressionMode.Decompress);
        brotli.ReadExactly(image);

        return new ZpImageReader(image, levelName, u32NameIndex, validate);
    }

    // =========================================================================
    // Structural validation (opt-in, for untrusted images)
    // =========================================================================

    /// <summary>
    /// Bounds-checks every section and the on-disk indices that feed the unchecked
    /// <see cref="Unsafe"/> hot path (MPHF, directory, records, sorted slots, postings, range table,
    /// name pool), so a corrupt or hostile image throws <see cref="InvalidDataException"/> at load
    /// rather than reading out of bounds at query time. Runs only on raw offsets, before any decoder.
    /// Opt-in (<c>validate: true</c>); the trusted built-in path skips it for speed.
    /// </summary>
    private void Validate()
    {
        long len = _image.Length;

        // 1. Every present section lies fully within the image.
        for (var k = 0; k < _sectionOffset.Length; k++)
        {
            int off = _sectionOffset[k], slen = _sectionLength[k];
            if (off == 0 && slen == 0) { continue; } // absent
            if (off < HeaderBytes || slen < 0 || off + (long)slen > len)
            {
                throw Corrupt($"section {(SectionKind)k} out of bounds (offset {off}, length {slen}, image {len} B)");
            }
        }

        // 2. Required sections must be present.
        RequireSection(SectionKind.NamePool);
        RequireSection(SectionKind.Mphf);
        RequireSection(SectionKind.Directory);
        RequireSection(SectionKind.Records);
        RequireSection(SectionKind.SortedSlots);

        // 3. MPHF: slot count must equal exactCount, and the displacement array must fit.
        if (ReadU32(_mphfBase) != (uint)_exactCount)
        {
            throw Corrupt($"MPHF slot count {ReadU32(_mphfBase)} != exactCount {_exactCount}");
        }

        if (_sectionLength[(int)SectionKind.Mphf] < 4 + (long)_exactCount * 4)
        {
            throw Corrupt($"MPHF section too small for {_exactCount} slots");
        }

        // 4. Directory holds exactCount 16-byte entries; every record span is in range.
        if (_sectionLength[(int)SectionKind.Directory] < (long)_exactCount * DirectoryEntryBytes)
        {
            throw Corrupt("directory section too small for exactCount entries");
        }

        for (var slot = 0; slot < _exactCount; slot++)
        {
            var e = _directoryBase + slot * DirectoryEntryBytes;
            int first = (int)ReadU32(e + 8), count = ReadU16(e + 12);
            if (first < 0 || count <= 0 || first + (long)count > _recordCount)
            {
                throw Corrupt($"directory slot {slot} record span [{first},{first + count}) outside {_recordCount} records");
            }
        }

        // 5. Records section holds recordCount entries at the active stride.
        if (_sectionLength[(int)SectionKind.Records] < (long)_recordCount * _recordBytes)
        {
            throw Corrupt("records section too small for recordCount entries");
        }

        // 6. SortedSlots holds exactCount u32 slot indices, each in range.
        if (_sectionLength[(int)SectionKind.SortedSlots] < (long)_exactCount * 4)
        {
            throw Corrupt("sorted-slots section too small for exactCount entries");
        }

        var sortedBase = Off(SectionKind.SortedSlots);
        for (var i = 0; i < _exactCount; i++)
        {
            if (ReadU32(sortedBase + i * 4) >= (uint)_exactCount)
            {
                throw Corrupt($"sorted-slots entry {i} references a slot >= exactCount");
            }
        }

        // 7. NamePool offsets are monotonic and stay within the pool's byte region.
        var poolCount = (int)ReadU32(Off(SectionKind.NamePool));
        var poolEnd   = _sectionOffset[(int)SectionKind.NamePool] + (long)_sectionLength[(int)SectionKind.NamePool];
        uint prev     = ReadU32(_namePoolOffsets);
        for (var i = 1; i <= poolCount; i++)
        {
            var cur = ReadU32(_namePoolOffsets + i * 4);
            if (cur < prev || _namePoolBytes + (long)cur > poolEnd)
            {
                throw Corrupt($"name-pool offset {i} is non-monotonic or out of bounds");
            }

            prev = cur;
        }

        // 8. Reverse-index postings reference only in-range record ids (bucket counts from header).
        ValidatePostings(SectionKind.TimezonePostings, ReadU16(20));
        ValidatePostings(SectionKind.AdminPostings,    ReadU16(22));

        // 9. Range table: stride checked in the ctor; here, every name-pool/record index.
        var rangeOff = Off(SectionKind.RangeTable);
        if (rangeOff != 0)
        {
            var rangeCount = (int)ReadU32(rangeOff);
            var entryBase  = rangeOff + 4;
            for (var i = 0; i < rangeCount; i++)
            {
                var p = entryBase + i * RangeEntryBytes;
                for (var f = 0; f < 16; f += 4) // codeIndex, fsaIndex, start4Index, end4Index
                {
                    if (ReadU32(p + f) >= (uint)poolCount)
                    {
                        throw Corrupt($"range entry {i} references a name-pool index >= {poolCount}");
                    }
                }

                if (ReadU32(p + 16) >= (uint)_recordCount)
                {
                    throw Corrupt($"range entry {i} references a record id >= {_recordCount}");
                }
            }
        }
    }

    private void ValidatePostings(SectionKind kind, int bucketCount)
    {
        var off = Off(kind);
        if (off == 0) { return; }

        var count = ReadU16(off);
        if (count != bucketCount)
        {
            throw Corrupt($"{kind} bucket count {count} != header table length {bucketCount}");
        }

        var startsBase = off + 2;
        var idsBase    = startsBase + (count + 1) * 4;
        var idCount    = (int)ReadU32(startsBase + count * 4); // starts[count] = total ids
        var sectionEnd = off + (long)_sectionLength[(int)kind];
        if (idCount < 0 || idsBase + (long)idCount * 4 > sectionEnd)
        {
            throw Corrupt($"{kind} id array overruns its section");
        }

        if (ReadU32(startsBase) != 0)
        {
            throw Corrupt($"{kind} starts[0] != 0");
        }

        uint prevStart = 0;
        for (var b = 1; b <= count; b++)
        {
            var cur = ReadU32(startsBase + b * 4);
            if (cur < prevStart)
            {
                throw Corrupt($"{kind} starts are not monotonic at bucket {b}");
            }

            prevStart = cur;
        }

        for (var i = 0; i < idCount; i++)
        {
            if (ReadU32(idsBase + i * 4) >= (uint)_recordCount)
            {
                throw Corrupt($"{kind} references a record id >= {_recordCount}");
            }
        }
    }

    private void RequireSection(SectionKind kind)
    {
        if (_sectionOffset[(int)kind] == 0)
        {
            throw Corrupt($"required section {kind} is missing");
        }
    }

    private static InvalidDataException Corrupt(string detail) =>
        new($"ZP image failed structural validation: {detail}. The image is corrupt or was built " +
            "with an incompatible writer — regenerate it with 'countrydatatools export --target zpi'.");

    // =========================================================================
    // Exact code lookup (raw — no normalisation)
    // =========================================================================

    /// <summary>Returns the default entry for an exact code, or <c>null</c> if not present.</summary>
    public CodeEntry? GetByCodeExact(string code)
    {
        if (!TryResolve(code, out var first, out _, out var packed))
        {
            return null;
        }

        // Cache hit is fully allocation-free: skip the Unpack (which builds a string) entirely.
        return _entryCache[first] ?? Materialize(first, CodePacker.Unpack(packed));
    }

    /// <summary>Returns all entries for an exact code (default first), or <c>null</c> if absent.</summary>
    public IReadOnlyList<CodeEntry>? GetAllByCodeExact(string code)
    {
        if (!TryResolve(code, out var first, out var count, out var packed))
        {
            return null;
        }

        var list = new CodeEntry[count];
        string? codeStr = null; // unpacked at most once, and only if some entry is uncached
        for (var i = 0; i < count; i++)
        {
            var recordId = first + i;
            list[i] = _entryCache[recordId]
                ?? Materialize(recordId, codeStr ??= CodePacker.Unpack(packed));
        }

        return list;
    }

    private bool TryResolve(string code, out int first, out int count, out ulong packed)
    {
        first = count = 0;
        packed = 0;

        if (_exactCount == 0 || !CodePacker.TryPack(code.AsSpan(), out packed))
        {
            return false;
        }

        var slot = Evaluate(packed);
        if (slot < 0 || slot >= _exactCount)
        {
            return false;
        }

        // Piece 5 — fast reads on the hot directory lookup.
        var entry = _directoryBase + slot * DirectoryEntryBytes;
        if (ReadU64Fast(entry) != packed)
        {
            return false; // MPHF maps unknown keys to arbitrary slots — verify.
        }

        first = (int)ReadU32Fast(entry + 8);
        count = ReadU16Fast(entry + 12);
        return true;
    }

    private int Evaluate(ulong key)
    {
        // v3 — one seed-independent FNV pass reused for both reductions; fastrange instead of modulo.
        var hb = ZpHash.HashBase(key);
        // Piece 5 — fast read on the MPHF displacement array.
        var g = Unsafe.ReadUnaligned<int>(ref _image[_mphfBase + 4 + ZpHash.Reduce(hb, 0, _exactCount) * 4]);
        return g < 0
            ? -g - 1
            : ZpHash.Reduce(hb, (uint)g, _exactCount);
    }

    // =========================================================================
    // Range fallback (mirrors ZipPostRegistry.LookupByRange)
    // =========================================================================

    public CodeEntry? GetByRange(string code)
    {
        var id = RangeRecordId(code, out var codeStr);
        return id < 0 ? null : Materialize(id, codeStr);
    }

    public IReadOnlyList<CodeEntry>? GetAllByRange(string code)
    {
        var id = RangeRecordId(code, out var codeStr);
        return id < 0 ? null : new[] { Materialize(id, codeStr) };
    }

    private int RangeRecordId(string code, out string codeStr)
    {
        codeStr = string.Empty;

        if (code.Length < 4)
        {
            return -1;
        }

        var fsa    = code.Substring(0, 3);
        var prefix = code.Substring(0, 4);

        var byFsa = _rangeByFsa ?? Publish(ref _rangeByFsa, BuildRangeIndex());
        if (!byFsa.TryGetValue(fsa, out var entries))
        {
            return -1;
        }

        var rangeBase = Off(SectionKind.RangeTable) + 4;
        foreach (var entryIndex in entries)
        {
            var p      = rangeBase + entryIndex * RangeEntryBytes;
            var start4 = _namePool[(int)ReadU32Fast(p + 8)];
            var end4   = _namePool[(int)ReadU32Fast(p + 12)];

            if (string.Compare(prefix, start4, StringComparison.OrdinalIgnoreCase) >= 0 &&
                string.Compare(prefix, end4, StringComparison.OrdinalIgnoreCase) <= 0)
            {
                codeStr = _namePool[(int)ReadU32Fast(p)]; // original range notation
                return (int)ReadU32Fast(p + 16);           // record id
            }
        }

        return -1;
    }

    private Dictionary<string, List<int>> BuildRangeIndex()
    {
        var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var off = Off(SectionKind.RangeTable);
        if (off == 0)
        {
            return map;
        }

        var count     = (int)ReadU32Fast(off);
        var entryBase = off + 4;
        for (var i = 0; i < count; i++)
        {
            var fsa = _namePool[(int)ReadU32Fast(entryBase + i * RangeEntryBytes + 4)];
            if (!map.TryGetValue(fsa, out var list))
            {
                list = new List<int>(1);
                map[fsa] = list;
            }

            list.Add(i);
        }

        return map;
    }

    // =========================================================================
    // Reverse lookups
    // =========================================================================

    public IReadOnlyList<CodeEntry> GetByTimeZone(string timezone)
    {
        return _timezoneIndex.TryGetValue(timezone, out var idx)
            ? MaterializePostings(SectionKind.TimezonePostings, idx)
            : Array.Empty<CodeEntry>();
    }

    public IReadOnlyList<CodeEntry> GetByState(string admin1Code)
    {
        var idx = IndexOfAdminCode(admin1Code);
        return idx < 0 ? Array.Empty<CodeEntry>() : MaterializePostings(SectionKind.AdminPostings, idx);
    }

    public IReadOnlyList<CodeEntry> GetByStateName(string admin1Name)
    {
        var idx = IndexOfAdminName(admin1Name);
        return idx < 0 ? Array.Empty<CodeEntry>() : MaterializePostings(SectionKind.AdminPostings, idx);
    }

    /// <summary>
    /// Matches against the single admin1 level the image carries. <paramref name="levelName"/> is
    /// checked against this country's admin level name; <paramref name="value"/> matches either the
    /// admin code or the admin name (case-insensitive).
    /// </summary>
    public IReadOnlyList<CodeEntry> GetByAdmin(string value, string levelName)
    {
        if (!string.Equals(levelName, _levelName, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<CodeEntry>();
        }

        var idx = IndexOfAdminCode(value);
        if (idx < 0)
        {
            idx = IndexOfAdminName(value);
        }

        return idx < 0 ? Array.Empty<CodeEntry>() : MaterializePostings(SectionKind.AdminPostings, idx);
    }

    public IReadOnlyList<CodeEntry> GetByName(string name)
    {
        var index = _nameIndex ?? Publish(ref _nameIndex, BuildNameIndex());
        if (!index.TryGetValue(name, out var recordIds))
        {
            return Array.Empty<CodeEntry>();
        }

        var cache = _nameResultCache ?? Publish(ref _nameResultCache,
            new ConcurrentDictionary<string, IReadOnlyList<CodeEntry>>(StringComparer.OrdinalIgnoreCase));
        if (cache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var list = new CodeEntry[recordIds.Count];
        for (var i = 0; i < recordIds.Count; i++)
        {
            list[i] = MaterializeAny(recordIds[i]);
        }

        // First writer wins; a racing thread may build a duplicate (immutable) list — harmless.
        return cache.GetOrAdd(name, list);
    }

    private Dictionary<string, List<int>> BuildNameIndex()
    {
        // Names are too numerous for a byte-indexed posting list, so the name → records map is
        // built once on first use by scanning records (still no CSV parse).
        var index = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var r = 0; r < _recordCount; r++)
        {
            // nameIndex width from filename suffix (.u16 = ReadU16Fast, .u32 = ReadU32Fast).
            var nameIdx = _u32NameIndex
                ? (int)ReadU32Fast(_recordsBase + r * _recordBytes + RecordNameOffset)
                : (int)ReadU16Fast(_recordsBase + r * _recordBytes + RecordNameOffset);
            var name = _namePool[nameIdx];
            if (!index.TryGetValue(name, out var list))
            {
                list = new List<int>(1);
                index[name] = list;
            }

            list.Add(r);
        }

        return index;
    }

    private IReadOnlyList<CodeEntry> MaterializePostings(SectionKind kind, int bucket)
    {
        // Memoise per (posting kind, bucket). There are only two posting sections, each backed by
        // a small array of buckets (timezones / admin1 divisions), so one slot per bucket is cheap.
        var cache = kind == SectionKind.TimezonePostings
            ? _tzPostingCache    ?? Publish(ref _tzPostingCache,    new IReadOnlyList<CodeEntry>?[_timezones.Length])
            : _adminPostingCache ?? Publish(ref _adminPostingCache, new IReadOnlyList<CodeEntry>?[_admins.Length]);

        if (cache[bucket] is { } cached)
        {
            return cached;
        }

        var off       = Off(kind);
        var startsBase = off + 2;
        var idsBase    = startsBase + (ReadU16(off) + 1) * 4;

        var start = (int)ReadU32Fast(startsBase + bucket * 4);
        var end   = (int)ReadU32Fast(startsBase + (bucket + 1) * 4);

        var list = new CodeEntry[end - start];
        for (var i = start; i < end; i++)
        {
            list[i - start] = MaterializeAny((int)ReadU32Fast(idsBase + i * 4));
        }

        cache[bucket] = list;
        return list;
    }

    // =========================================================================
    // Closest (US numeric), enumeration, random
    // =========================================================================

    public CodeEntry? GetClosest(string zip, out bool exactHandled)
    {
        exactHandled = false;

        var ints = _sortedUsZipInts ?? Publish(ref _sortedUsZipInts, BuildSortedUsZipInts());
        if (ints.Length == 0 || !int.TryParse(zip, out var key))
        {
            return null;
        }

        var index = Array.BinarySearch(ints, key);
        if (index < 0)
        {
            index = ~index;
        }

        var best = -1;
        if (index < ints.Length)
        {
            best = ints[index];
        }

        if (index > 0)
        {
            var below = ints[index - 1];
            if (best < 0 || key - below < best - key)
            {
                best = below;
            }
        }

        if (best < 0)
        {
            return null;
        }

        exactHandled = true;
        return GetByCodeExact(best.ToString("D5"));
    }

    private int[] BuildSortedUsZipInts()
    {
        var list = new List<int>(_exactCount);
        var sortedBase = Off(SectionKind.SortedSlots);
        for (var i = 0; i < _exactCount; i++)
        {
            var slot   = (int)ReadU32Fast(sortedBase + i * 4);
            var packed = ReadU64Fast(_directoryBase + slot * DirectoryEntryBytes);
            var code   = CodePacker.Unpack(packed);
            if (code.Length == 5 && IsAllDigits(code))
            {
                list.Add(int.Parse(code));
            }
        }

        list.Sort();
        return list.ToArray();
    }

    public IReadOnlyList<CodeEntry> All => _all ?? Publish(ref _all, BuildAll());

    private IReadOnlyList<CodeEntry> BuildAll()
    {
        var list = new List<CodeEntry>(_recordCount);
        var sortedBase = Off(SectionKind.SortedSlots);
        for (var i = 0; i < _exactCount; i++)
        {
            var slot    = (int)ReadU32Fast(sortedBase + i * 4);
            var entry   = _directoryBase + slot * DirectoryEntryBytes;
            var packed  = ReadU64Fast(entry);
            var first   = (int)ReadU32Fast(entry + 8);
            var count   = ReadU16Fast(entry + 12);
            var codeStr = CodePacker.Unpack(packed);
            for (var k = 0; k < count; k++)
            {
                list.Add(Materialize(first + k, codeStr));
            }
        }

        return list.AsReadOnly();
    }

    // =========================================================================
    // Materialisation + cache
    // =========================================================================

    /// <summary>Materialises a record whose code string is known (exact or range).</summary>
    private CodeEntry Materialize(int recordId, string codeStr)
    {
        if (_entryCache[recordId] is { } cached)
        {
            return cached;
        }

        var rec = _recordsBase + recordId * _recordBytes;

        // Piece 5 — Unsafe.ReadUnaligned avoids AsSpan overhead on the hot path.
        // nameIndex width is determined by the filename suffix: u16 (.u16.zpi.br) or u32 (.u32.zpi.br).
        var nameIdx   = _u32NameIndex
            ? (int)ReadU32Fast(rec + RecordNameOffset)
            : (int)ReadU16Fast(rec + RecordNameOffset);
        var name      = _namePool[nameIdx];
        var timezone  = _timezones[_image[rec + RecordTzOffset]];
        var isDefault = (_image[rec + RecordFlagsOffset] & (byte)RecordFlags.IsDefault) != 0;

        // Piece 2 — reference the pre-built AdminLevel[] instead of allocating a new one.
        var admins = _adminLevels[_image[rec + RecordAdminOffset]];

        var entry = new CodeEntry(codeStr, name, timezone, isDefault, admins);
        _entryCache[recordId] = entry;
        return entry;
    }

    /// <summary>
    /// Materialises a record reached via a reverse index, where the owning code string is not in
    /// hand. Exact records recover their code from the directory; range records carry their own
    /// notation in the range table.
    /// </summary>
    private CodeEntry MaterializeAny(int recordId)
    {
        if (_entryCache[recordId] is { } cached)
        {
            return cached;
        }

        var flags = (RecordFlags)_image[_recordsBase + recordId * _recordBytes + RecordFlagsOffset];
        var codeStr = (flags & RecordFlags.IsRange) != 0
            ? RangeCodeForRecord(recordId)
            : CodeForExactRecord(recordId);

        return Materialize(recordId, codeStr);
    }

    private string CodeForExactRecord(int recordId)
    {
        // Map the record back to its owning directory slot, then unpack that slot's code. The
        // record → slot map is built once (O(exactCount)) on first use; without it this lookup
        // was an O(exactCount) scan per record, making large reverse-lookup result sets O(n²).
        var map  = _recordToSlot ?? Publish(ref _recordToSlot, BuildRecordToSlot());
        var slot = (uint)recordId < (uint)map.Length ? map[recordId] : -1;
        return slot < 0
            ? string.Empty
            : CodePacker.Unpack(ReadU64Fast(_directoryBase + slot * DirectoryEntryBytes));
    }

    private int[] BuildRecordToSlot()
    {
        // Each directory entry owns the contiguous record span [first, first+count). Walk every
        // exact code once and stamp its slot across that span. Range records (appended after the
        // exact records) are never reached via CodeForExactRecord, so they stay -1.
        var map = new int[_recordCount];
        for (var r = 0; r < map.Length; r++)
        {
            map[r] = -1;
        }

        var sortedBase = Off(SectionKind.SortedSlots);
        for (var i = 0; i < _exactCount; i++)
        {
            var slot  = (int)ReadU32Fast(sortedBase + i * 4);
            var entry = _directoryBase + slot * DirectoryEntryBytes;
            var first = (int)ReadU32Fast(entry + 8);
            var count = ReadU16Fast(entry + 12);
            for (var k = 0; k < count; k++)
            {
                map[first + k] = slot;
            }
        }

        return map;
    }

    private string RangeCodeForRecord(int recordId)
    {
        // Was an O(rangeCount) scan per uncached range record reached via a reverse lookup, making a
        // range-heavy reverse result O(records × ranges). Build the record → notation map once.
        var map = _rangeCodeByRecord ?? Publish(ref _rangeCodeByRecord, BuildRangeCodeByRecord());
        return map.TryGetValue(recordId, out var code) ? code : string.Empty;
    }

    private Dictionary<int, string> BuildRangeCodeByRecord()
    {
        var map = new Dictionary<int, string>();
        var off = Off(SectionKind.RangeTable);
        if (off == 0)
        {
            return map;
        }

        var count     = (int)ReadU32(off);
        var entryBase = off + 4;
        for (var i = 0; i < count; i++)
        {
            var p = entryBase + i * RangeEntryBytes;
            map[(int)ReadU32Fast(p + 16)] = _namePool[(int)ReadU32Fast(p)];
        }

        return map;
    }

    // =========================================================================
    // Section decoders + primitive reads
    // =========================================================================

    // Piece 1 — decode entire NamePool into string[] at load.
    private string[] DecodeNamePool()
    {
        var namePoolBase = Off(SectionKind.NamePool);
        var count        = (int)ReadU32(namePoolBase);
        var pool         = new string[count];
        for (var i = 0; i < count; i++)
        {
            var start = (int)ReadU32(_namePoolOffsets + i * 4);
            var end   = (int)ReadU32(_namePoolOffsets + (i + 1) * 4);
            pool[i]   = Encoding.UTF8.GetString(_image, _namePoolBytes + start, end - start);
        }

        return pool;
    }

    // Piece 2 — pre-build one AdminLevel[] per admin division.
    private AdminLevel[][] BuildAdminLevels()
    {
        var levels = new AdminLevel[_admins.Length][];
        for (var i = 0; i < _admins.Length; i++)
        {
            var (code, name) = _admins[i];
            levels[i] = code != "---"
                ? new[] { new AdminLevel(name, code, _levelName) }
                : Array.Empty<AdminLevel>();
        }

        return levels;
    }

    private string[] DecodeTimezones()
    {
        var off   = Off(SectionKind.TimezoneTable);
        var count = ReadU16(off);
        var arr   = new string[count];
        var pos   = off + 2;
        for (var i = 0; i < count; i++)
        {
            var len = ReadU16(pos);
            pos += 2;
            arr[i] = Encoding.UTF8.GetString(_image, pos, len);
            pos += len;
        }

        return arr;
    }

    private (string Code, string Name)[] DecodeAdmins()
    {
        var off   = Off(SectionKind.AdminTable);
        var count = ReadU16(off);
        var arr   = new (string, string)[count];
        var pos   = off + 2;
        for (var i = 0; i < count; i++)
        {
            var cl = ReadU16(pos); pos += 2;
            var code = Encoding.UTF8.GetString(_image, pos, cl); pos += cl;
            var nl = ReadU16(pos); pos += 2;
            var name = Encoding.UTF8.GetString(_image, pos, nl); pos += nl;
            arr[i] = (code, name);
        }

        return arr;
    }

    private int Off(SectionKind kind) => _sectionOffset[(int)kind];

    // Safe reads — used during construction where correctness matters more than throughput.
    private ushort ReadU16(int pos) => BinaryPrimitives.ReadUInt16LittleEndian(_image.AsSpan(pos));
    private uint   ReadU32(int pos) => BinaryPrimitives.ReadUInt32LittleEndian(_image.AsSpan(pos));
    private int    ReadI32(int pos) => BinaryPrimitives.ReadInt32LittleEndian(_image.AsSpan(pos));
    private ulong  ReadU64(int pos) => BinaryPrimitives.ReadUInt64LittleEndian(_image.AsSpan(pos));

    // Piece 5 — Unsafe reads for the hot lookup path.
    // Unsafe.ReadUnaligned avoids the AsSpan() overhead.  Safe on .NET 8+ (x86/ARM = LE, no swap).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort ReadU16Fast(int pos) =>
        Unsafe.ReadUnaligned<ushort>(ref _image[pos]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint ReadU32Fast(int pos) =>
        Unsafe.ReadUnaligned<uint>(ref _image[pos]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong ReadU64Fast(int pos) =>
        Unsafe.ReadUnaligned<ulong>(ref _image[pos]);

    private string ReadCountry(int pos)
    {
        var end = pos;
        while (end < pos + 4 && _image[end] != 0)
        {
            end++;
        }

        return Encoding.ASCII.GetString(_image, pos, end - pos);
    }

    private int IndexOfAdminCode(string code) =>
        _adminCodeIndex.TryGetValue(code, out var i) ? i : -1;

    private int IndexOfAdminName(string name) =>
        _adminNameIndex.TryGetValue(name, out var i) ? i : -1;

    private static Dictionary<string, int> BuildValueIndex(string[] values)
    {
        var map = new Dictionary<string, int>(values.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Length; i++)
        {
            map.TryAdd(values[i], i); // first occurrence wins (matches prior IndexOf behaviour)
        }

        return map;
    }

    private Dictionary<string, int> BuildAdminIndex(bool selectCode)
    {
        var map = new Dictionary<string, int>(_admins.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _admins.Length; i++)
        {
            map.TryAdd(selectCode ? _admins[i].Code : _admins[i].Name, i);
        }

        return map;
    }

    /// <summary>
    /// Publishes a lazily-built reference with release semantics and returns the single winning
    /// instance, so concurrent first-use never sees a torn object and never disagrees on identity.
    /// The <c>field ?? Publish(ref field, Build())</c> idiom short-circuits once initialised, keeping
    /// the memoised hot paths allocation-free.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T Publish<T>(ref T? field, T built) where T : class =>
        Interlocked.CompareExchange(ref field, built, null) ?? built;

    private static bool IsAllDigits(string value)
    {
        foreach (var c in value)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
#endif
