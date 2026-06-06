namespace ZipPostLookup.CountryDataTools.Models.Dbo;

/// <summary>
/// Minimal Dapper read projection for <c>data.Reference</c> used by the coordinate
/// enrichment command to check which codes already have timezone / name verified.
/// Maps to <c>GetReferenceStateByCodes</c>; column names match the query aliases exactly.
/// </summary>
public sealed class ReferenceStateRow
{
    public string ZpCode          { get; set; } = "";
    public string PlaceName       { get; set; } = "";
    public bool   TimezoneChecked { get; set; }
    public bool   NameChecked     { get; set; }
}
