using ZipPostLookup.Core;

namespace ZipPostLookup.Normalizers;

/// <summary>
/// Postal code rules for Mexico.
/// <see href="https://dof.gob.mx/nota_detalle.php?codigo=4641408" />
/// </summary>
/// <remarks>
/// Mexican postal codes (SEPOMEX) are 5-digit numeric strings in the range 01000–99999.
/// The leading zero is significant — <c>"01000"</c> is Álvaro Obregón, CDMX.
/// Normalisation zero-pads any numeric input shorter than 5 digits so that, for example,
/// <c>"603"</c> → <c>"00603"</c> and <c>"1000"</c> → <c>"01000"</c>. This handles both
/// common data-entry errors and codes that lost leading zeros during integer conversion
/// (e.g. from SQL Server when the Zip column was cast or stored as an integer).
/// Validation rejects <c>"00000"</c> and any non-5-digit or non-numeric input.
/// </remarks>
public sealed class MxCountryCodeRules : ICountryCodeRules
{
    /// <inheritdoc/>
    public CountryCode CountryCode => CountryCode.MX;

    /// <inheritdoc/>
    public string Normalize(string? input)
    {
        if (input is null) return string.Empty;

        var s = input.Trim();

        // Zero-pad any short all-digit string up to 5 characters.
        // Handles 1-, 2-, 3-, and 4-digit input that is missing leading zeros —
        // e.g. a zip stored/exported as an integer loses its leading zeros.
        if (s.Length is >= 1 and <= 4 && IsAllDigits(s))
        {
            s = s.PadLeft(5, '0');
        }

        return s;
    }

    /// <inheritdoc/>
    public bool Validate(string normalizedInput)
    {
        if (normalizedInput is not { Length: 5 }) return false;
        if (!IsAllDigits(normalizedInput)) return false;

        // "00000" is not a valid Mexican postal code
        if (normalizedInput == "00000") return false;

        return true;
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