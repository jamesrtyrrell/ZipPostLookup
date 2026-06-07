using ZipPostLookup.Core;

namespace ZipPostLookup.Normalizers;

/// <summary>
/// Postal code rules for the United States.
/// https://faq.usps.com/s/article/ZIP-Code-The-Basics
/// </summary>
/// <remarks>
/// Normalisation strips ZIP+4 suffixes so that <c>"12345-6789"</c>, <c>"123456789"</c>,
/// and <c>"12345 6789"</c> all resolve to <c>"12345"</c>.
/// Short all-digit strings (1–4 digits) are zero-padded to 5 so that codes like
/// <c>"501"</c> → <c>"00501"</c> are recovered when leading zeros were lost during
/// integer conversion in the DB or during export.
/// Validation requires exactly 5 ASCII digits.
/// </remarks>
public sealed class UsCountryCodeRules : ICountryCodeRules
{
    /// <inheritdoc/>
    public CountryCode CountryCode => CountryCode.US;

    /// <inheritdoc/>
    public string Normalize(string? input)
    {
        if (input is null) return string.Empty;

        var s = input.Trim();

        // "12345-6789" → "12345"
        var dash = s.IndexOf('-');
        if (dash == 5) { s = s.Substring(0, 5); }

        // "12345 6789" → "12345"
        var space = s.IndexOf(' ');
        if (space == 5) { s = s.Substring(0, 5); }

        // "123456789" (9 digits, no separator) → "12345"
        if (s.Length == 9 && IsAllDigits(s)) { s = s.Substring(0, 5); }

        // Zero-pad short all-digit strings — recovers leading zeros lost during
        // integer conversion (e.g. DB stored zip as INT, "00501" → 501 → "501").
        if (s.Length is >= 1 and <= 4 && IsAllDigits(s))
        {
            s = s.PadLeft(5, '0');
        }

        return s;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// In addition to structural format (exactly 5 ASCII digits), valid US ZIP codes
    /// must fall within the USPS-defined range 00501 (IRS, Holtsville NY) to 99950
    /// (Ketchikan AK). Values outside this range are not assigned by USPS and are
    /// rejected even though they are structurally well-formed.
    /// </remarks>
    public bool Validate(string normalizedInput)
    {
        if (normalizedInput is not { Length: 5 }) return false;
        if (!IsAllDigits(normalizedInput)) return false;
        var n = int.Parse(normalizedInput);
        return n is >= 501 and <= 99950;
    }

    private static bool IsAllDigits(string value)
    {
        foreach (var c in value)
        {
            if (c < '0' || c > '9') return false;
        }
        return true;
    }
}