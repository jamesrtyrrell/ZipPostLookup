namespace ZipPostLookup.CountryDataTools.Models.Enums;

/// <summary>
/// Severity of a CSV validation finding.
///   Error   — structural problem; cannot be auto-fixed; blocks Fix and Merge.
///   Fixable — the Fixer can resolve automatically (duplicates, casing, timezones, booleans).
///   Warning — advisory only; does not block any command.
/// </summary>
public enum Severity
{
    Error,
    Fixable,
    Warning,
}
