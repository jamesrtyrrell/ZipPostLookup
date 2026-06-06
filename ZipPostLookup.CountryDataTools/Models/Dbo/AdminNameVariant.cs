namespace ZipPostLookup.CountryDataTools.Models.Dbo;

/// <summary>
/// Dapper result row from <c>CommonQueries.DetectAdminNameVariants</c>.
/// Represents one minority admin level 1 name that will be normalised to the dominant spelling.
/// </summary>
public sealed class AdminNameVariant
{
    public string Code          { get; set; } = "";
    public string DominantValue { get; set; } = "";
    public string MinorityValue { get; set; } = "";
    public int    MinorityCnt   { get; set; }
}
