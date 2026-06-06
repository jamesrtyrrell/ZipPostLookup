using ZipPostLookup.Core;

namespace ZipPostLookup.Normalizers;

/// <summary>
/// Postal code rules for Canada.
/// https://https://www.canadapost-postescanada.ca/cpc/en/support/articles/addressing-guidelines/postal-codes.page
/// </summary>
/// <remarks>
/// Normalisation uppercases the input and removes all spaces and hyphens, so
/// <c>"m5v 3l9"</c>, <c>"M5V-3L9"</c>, and <c>"M5V3L9"</c> all resolve to <c>"M5V3L9"</c>.
/// Validation requires the 6-character <c>A1A1A1</c> pattern (letter–digit alternating).
/// </remarks>
public sealed class CaCountryCodeRules : ICountryCodeRules
{
    /// <inheritdoc/>
    public CountryCode CountryCode => CountryCode.CA;

    /// <inheritdoc/>
    public string Normalize(string? input)
    {
        if (input is null) return string.Empty;

        var s = input.Trim().ToUpperInvariant();

        // Remove optional space or hyphen between the two FSA/LDU halves
        // "M5V 3L9" → "M5V3L9", "M5V-3L9" → "M5V3L9"
        if (s.Length == 7 && (s[3] == ' ' || s[3] == '-'))
            s = s.Substring(0, 3) + s.Substring(4);

        return s;
    }

    /// <inheritdoc/>
    public bool Validate(string normalizedInput)
    {
        // Must be exactly 6 characters matching A1A1A1
        if (normalizedInput is not { Length: 6 }) return false;

        return char.IsLetter(normalizedInput[0]) &&
               char.IsDigit(normalizedInput[1]) &&
               char.IsLetter(normalizedInput[2]) &&
               char.IsDigit(normalizedInput[3]) &&
               char.IsLetter(normalizedInput[4]) &&
               char.IsDigit(normalizedInput[5]);
    }
}