using ZipPostLookup.Core;

namespace ZipPostLookup.Normalizers;

/// <summary>
/// Defines per-country normalisation and validation rules for postal code input.
/// </summary>
/// <remarks>
/// Implement this interface to support a new country's postal code format, or to override
/// the built-in rules for US, CA, or MX. Pass custom implementations to the
/// <see cref="ZipPostRegistry"/> constructor via the <c>rules</c> parameter.
///
/// The two-step contract is: <see cref="Normalize"/> first (clean up raw user input),
/// then <see cref="Validate"/> (check the normalised form is structurally correct).
/// <see cref="ZipPostRegistry"/> calls both in that order before hitting any index.
/// </remarks>
public interface ICountryCodeRules
{
    /// <summary>
    /// The ISO 3166-1 alpha-2 country code these rules apply to, e.g. <c>"US"</c>.
    /// </summary>
    CountryCode CountryCode { get; }

    /// <summary>
    /// Normalises raw user input into the canonical form used as the index key.
    /// </summary>
    /// <param name="input">Raw input, e.g. <c>"12345-6789"</c> or <c>"m5v 3l9"</c>.</param>
    /// <returns>
    /// The canonical form, e.g. <c>"12345"</c> or <c>"M5V3L9"</c>.
    /// Must not return <c>null</c>.
    /// </returns>
    string Normalize(string input);

    /// <summary>
    /// Returns <c>true</c> if the normalised value is structurally valid for this country.
    /// Called after <see cref="Normalize"/> — the input here is already normalised.
    /// </summary>
    /// <param name="normalizedInput">The output of <see cref="Normalize"/>.</param>
    /// <returns><c>true</c> if the format is valid; <c>false</c> otherwise.</returns>
    bool Validate(string normalizedInput);
}