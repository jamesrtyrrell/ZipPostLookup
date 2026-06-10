namespace ZipPostLookup.CountryDataTools.Export.ZpImage;

/// <summary>
/// Packs a postal code string into a single <see cref="ulong"/> key and back.
///
/// <para>Each character is mapped to a value in <c>1..36</c> (<c>0</c> is reserved as an
/// implicit terminator) and the code is treated as a big-endian base-37 number. Letters
/// are upper-cased before mapping, so <c>"m5v3l9"</c> and <c>"M5V3L9"</c> pack to the same
/// key — matching the registry's <see cref="StringComparer.OrdinalIgnoreCase"/> lookups.</para>
///
/// <para>The mapping preserves ASCII ordinal order (<c>'0'..'9'</c> → <c>1..10</c>,
/// <c>'A'..'Z'</c> → <c>11..36</c>), so for codes of equal length the packed value sorts the
/// same way the original strings do. Per-country codes are fixed length (US/MX 5 digits,
/// CA 6 alphanumerics), so a packed-value sort is equivalent to an ordinal sort within a
/// country's exact codes.</para>
///
/// <para>Distinct codes always pack to distinct keys (leading characters are significant),
/// so <c>"00501"</c> and <c>"501"</c> never collide. The scheme supports codes up to
/// <see cref="MaxCodeLength"/> characters (37¹² &lt; ulong.MaxValue).</para>
/// </summary>
internal static class CodePacker
{
    private const ulong Radix = 37UL;

    /// <summary>Maximum supported code length (37¹² still fits in a ulong).</summary>
    public const int MaxCodeLength = 12;

    /// <summary>Packs <paramref name="code"/> into a unique, reversible <see cref="ulong"/> key.</summary>
    /// <exception cref="ArgumentException">
    /// The code is empty, longer than <see cref="MaxCodeLength"/>, or contains a character
    /// that is not <c>[0-9A-Za-z]</c>.
    /// </exception>
    public static ulong Pack(ReadOnlySpan<char> code)
    {
        if (code.Length is 0 or > MaxCodeLength)
        {
            throw new ArgumentException(
                $"Code length {code.Length} is out of range 1..{MaxCodeLength}: '{code}'.");
        }

        ulong value = 0;
        foreach (var ch in code)
        {
            value = value * Radix + (ulong)CharValue(ch);
        }

        return value;
    }

    /// <summary>Reverses <see cref="Pack"/>, returning the upper-cased code string.</summary>
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

    private static int CharValue(char ch)
    {
        if (ch is >= '0' and <= '9')
        {
            return ch - '0' + 1;             // 1..10
        }

        var upper = char.ToUpperInvariant(ch);
        if (upper is >= 'A' and <= 'Z')
        {
            return upper - 'A' + 11;         // 11..36
        }

        throw new ArgumentException($"Unpackable character '{ch}' in postal code.");
    }

    private static char ValueChar(int digit) =>
        digit <= 10
            ? (char)('0' + digit - 1)
            : (char)('A' + digit - 11);
}
