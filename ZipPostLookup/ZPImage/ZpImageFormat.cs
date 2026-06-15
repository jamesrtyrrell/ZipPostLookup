#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;

namespace ZipPostLookup.ZPImage;

// ─────────────────────────────────────────────────────────────────────────────
// KEEP IN SYNC with ZipPostLookup.CountryDataTools/Export/ZPimage:
//   ZpImageFormat.cs  (constants, section kinds, record/entry sizes)
//   CodePacker.cs     (Pack / Unpack)
//   MinimalPerfectHash.cs  (the Hash + Evaluate formula)
// These three pieces are the read/write contract. Any change on the writer side
// must be mirrored here, or images will decode incorrectly. The on-disk format is
// versioned (see CurrentVersion) and ZpImageReader rejects mismatched versions.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Reader-side mirror of the ZP frozen-image on-disk layout. See the writer's
/// <c>ZpImageFormat</c> for the authoritative format documentation.
/// </summary>
internal static class ZpImageFormat
{
    public static readonly byte[] Magic = "ZPIM"u8.ToArray();

    public const ushort CurrentVersion      = 5;   // v5: per-record reason byte (offset 5, previously reserved=0)
    public const int    HeaderBytes         = 32;
    public const int    SectionEntryBytes   = 12;
    public const int    RecordBytes         = 6;   // nameIndex u16 — normal datasets (.u16.zpi.br)
    public const int    RecordBytesU32      = 8;   // nameIndex u32 — large datasets > 65k names (.u32.zpi.br)
    public const int    DirectoryEntryBytes = 16;
    public const int    RangeEntryBytes     = 20;
    public const int    SectionAlignment    = 8;

    // Record field offsets within a RecordBytes-sized entry:
    public const int RecordNameOffset  = 0;  // u16 — index into NamePool
    public const int RecordTzOffset    = 2;  // u8  — index into TimezoneTable
    public const int RecordAdminOffset = 3;  // u8  — index into AdminTable
    public const int RecordFlagsOffset  = 4;  // u8  — RecordFlags
    public const int RecordReasonOffset = 5;  // u8  — CodeReason (was reserved=0 in v4; safe to read from v4 images)

    // ── v4 Directory entry layout (still DirectoryEntryBytes = 16) ────────────
    // [0..8)  packedCode u64
    // [8..12) firstRecord u32 — bits[0..30) record id; bit31 Inline; bit30 InlineIsDefault
    // [12]    inline tzIndex    u8  (inline entries only; else unused)
    // [13]    inline adminIndex u8  (inline entries only; else unused)
    // [14..16) u16 — inline: nameIndex (u16 images only); multi-record: record count
    // For a single-record code (count == 1) in a u16 image, the record's name/tz/admin
    // are copied into the directory entry so GetByCode materialises without touching the
    // Records section. The record still exists in Records (postings, cache, GetAll use it).
    public const int  DirFirstOffset       = 8;
    public const int  DirInlineTzOffset    = 12;
    public const int  DirInlineAdminOffset = 13;
    public const int  DirCountOrNameOffset = 14;

    public const uint DirInlineFlag    = 0x8000_0000u; // bit31 of firstRecord: entry is inline
    public const uint DirInlineDefault = 0x4000_0000u; // bit30 of firstRecord: inline record IsDefault
    public const uint DirRecordIdMask  = 0x3FFF_FFFFu; // low 30 bits: record id

    [System.Flags]
    public enum RecordFlags : byte
    {
        None      = 0,
        IsDefault = 1 << 0,
        IsRange   = 1 << 1,
    }

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
}

/// <summary>
/// FNV-1a-based hashing for the v3 minimal perfect hash. <see cref="HashBase"/> is a
/// seed-independent pass over the eight little-endian bytes of a packed code key; <see cref="Reduce"/>
/// folds in a seed and maps into <c>[0, n)</c> by fastrange. Both must be byte-identical to the
/// writer's <c>MinimalPerfectHash.HashBase</c> / <c>Reduce</c> so the MPHF evaluates the same slots
/// at read time.
/// </summary>
internal static class ZpHash
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime       = 1099511628211UL;

    // Seed-independent FNV-1a over all 8 little-endian key bytes, computed once per lookup and
    // reused for both the bucket and the displaced-slot reductions (unrolled; no loop overhead).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong HashBase(ulong key)
    {
        var h = FnvOffsetBasis;
        h = (h ^ (key & 0xFF)) * FnvPrime;
        h = (h ^ ((key >> 8) & 0xFF)) * FnvPrime;
        h = (h ^ ((key >> 16) & 0xFF)) * FnvPrime;
        h = (h ^ ((key >> 24) & 0xFF)) * FnvPrime;
        h = (h ^ ((key >> 32) & 0xFF)) * FnvPrime;
        h = (h ^ ((key >> 40) & 0xFF)) * FnvPrime;
        h = (h ^ ((key >> 48) & 0xFF)) * FnvPrime;
        h = (h ^ ((key >> 56) & 0xFF)) * FnvPrime;
        return h;
    }

    // Folds the seed into the base hash and maps to [0, n) via Lemire fastrange (multiply-shift),
    // replacing the former 64-bit modulo. Two cheap multiplies + a shift; no division.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Reduce(ulong hashBase, uint seed, int n)
    {
        var h = (hashBase ^ seed) * FnvPrime;
        return (int)(((h >> 32) * (ulong)n) >> 32);
    }
}

/// <summary>
/// Reader-side mirror of the writer's <c>CodePacker</c>: a reversible, case-insensitive
/// base-37 packing of a postal code into a <see cref="ulong"/>. <see cref="TryPack"/> is used to
/// build the lookup key; <see cref="Unpack"/> reproduces the (upper-cased) code string for a
/// matched directory entry, since exact codes are not stored as strings in the image.
/// </summary>
internal static class CodePacker
{
    private const ulong Radix = 37UL;
    public const int MaxCodeLength = 12;

    public static bool TryPack(ReadOnlySpan<char> code, out ulong value)
    {
        value = 0;
        if (code.Length is 0 or > MaxCodeLength)
        {
            return false;
        }

        ulong v = 0;
        foreach (var ch in code)
        {
            if (!TryCharValue(ch, out var d))
            {
                return false;
            }

            v = v * Radix + (ulong)d;
        }

        value = v;
        return true;
    }

    public static string Unpack(ulong value)
    {
        Span<char> buffer = stackalloc char[MaxCodeLength];
        var i = buffer.Length;

        while (value > 0)
        {
            var digit = (int)(value % Radix);
            value /= Radix;
            buffer[--i] = ValueChar(digit);
        }

        return new string(buffer[i..]);
    }

    private static bool TryCharValue(char ch, out int value)
    {
        if (ch is >= '0' and <= '9')
        {
            value = ch - '0' + 1;
            return true;
        }

        var upper = char.ToUpperInvariant(ch);
        if (upper is >= 'A' and <= 'Z')
        {
            value = upper - 'A' + 11;
            return true;
        }

        value = 0;
        return false;
    }

    private static char ValueChar(int digit) =>
        digit <= 10
            ? (char)('0' + digit - 1)
            : (char)('A' + digit - 11);
}
#endif
