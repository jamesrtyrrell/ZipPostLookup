using Dapper.Contrib.Extensions;

namespace ZipPostLookup.CountryDataTools.Models.Dbo;

/// <summary>
/// Mirrors <c>data.AdminLevels</c> for the candidate pipeline.
/// Used by <c>GetCandidateAdminLevels</c> to resolve LevelNumber → AdminLevelId FK
/// when inserting <c>codes.CandidateAdmins</c> rows, and by the Level-1 subqueries
/// in <c>GetCandidatesByStatus</c>, <c>GetCandidatesByCode</c>, and <c>GetCandidateStateCode</c>.
/// </summary>
[Table("codes.AdminLevels")]
public class CodesAdminLevel : ICodesSchema
{
    [Key] public int AdminLevelId { get; set; }
    public string CountryId  { get; set; } = string.Empty;
    public int    LevelNumber { get; set; }
    public string LevelName  { get; set; } = string.Empty;
    public string? CodeType  { get; set; }
    public string? Aliases   { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
