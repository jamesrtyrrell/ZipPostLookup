namespace ZipPostLookup.CountryDataTools.Models.Dbo;

/// <summary>
/// Dapper projection for <c>ExportReferenceDataWithCuration</c> and
/// <c>ExportReferenceDataCuratedOnlyWithCuration</c>.
/// Extends the standard export columns with <c>TimezoneChecked</c> and
/// <c>NameChecked</c> for the <c>--target ref</c> full-backup export.
/// </summary>
public class ReferenceRowFull : IDataSchema
{
    public string  ZpCode          { get; set; } = "";
    public string? PlaceName       { get; set; }
    public string? Timezone        { get; set; }
    public bool    IsDefault       { get; set; }
    public string? Lat             { get; set; }
    public string? Lng             { get; set; }
    public string? Admin1          { get; set; }
    public string? Admin1Code      { get; set; }
    public bool    TimezoneChecked { get; set; }
    public bool    NameChecked     { get; set; }
    public string? AltNameOf       { get; set; }
}
