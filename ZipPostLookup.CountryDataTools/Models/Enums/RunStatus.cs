namespace ZipPostLookup.CountryDataTools.Models.Enums;

/// <summary>
/// Lifecycle status for a <c>pipeline.runs</c> row.
/// Stored as the PascalCase member name (e.g. "InProgress", "Complete").
/// Use <c>nameof(RunStatus.X)</c> or <c>.ToString()</c> to get the DB value.
/// </summary>
public enum RunStatus
{
    InProgress,
    Complete,
    Failed,
}
