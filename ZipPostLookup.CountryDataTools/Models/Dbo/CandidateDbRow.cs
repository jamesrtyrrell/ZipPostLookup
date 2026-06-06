namespace ZipPostLookup.CountryDataTools.Models.Dbo;

/// <summary>
/// Dapper projection for <c>GetCandidatesByStatus</c> and <c>GetCandidatesByCode</c>.
/// Maps the joined <c>codes.Candidate</c> + <c>codes.CandidateAdmins</c> result
/// into a flat row before conversion to <c>CsvRow</c>.
/// </summary>
public sealed record CandidateDbRow(
    int     RecordNumber,
    string  ZpCode,
    string  PlaceName,
    string? Timezone,
    string? IsDefault,
    string? Admin1,
    string? Admin1Code
) : ICodesSchema;
