namespace ZipPostLookup.CountryDataTools.Models.Enums;

/// <summary>
/// Canonical error type for a <see cref="ZipPostLookup.CountryDataTools.Reporting.ValidationError"/>.
/// Stored as the PascalCase member name in reports and log output via <c>.ToString()</c>.
/// Maps to <c>Severity</c> in <c>ValidationError.Severity</c>.
/// </summary>
public enum ErrorType
{
    // Structural — cannot be auto-fixed; block Fix and Merge
    MissingColumn,
    TooFewColumns,
    MissingValue,
    InvalidZipFormat,
    UnresolvableTimezone,

    // Fixable — Fixer handles these automatically
    DuplicatePair,
    MultipleDefaults,
    NonIanaTimezone,
    DeprecatedTimezone,
    InvalidBoolean,

    // Advisory
    NoDefaultForZip,
}
