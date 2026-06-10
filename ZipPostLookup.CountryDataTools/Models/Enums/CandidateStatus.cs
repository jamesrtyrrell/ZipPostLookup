namespace ZipPostLookup.CountryDataTools.Models.Enums;

/// <summary>
/// Processing status for a <c>[codes].[candidate]</c> row.
/// Stored as the PascalCase member name (e.g. "Clean", "Discrepancy").
/// Use <c>nameof(CandidateStatus.X)</c> or <c>.ToString()</c> to get the DB value.
/// </summary>
public enum CandidateStatus
{
    Pending,
    Discrepancy,
    Clean,
    Rejected,
    Unfound,
    Error,
}
