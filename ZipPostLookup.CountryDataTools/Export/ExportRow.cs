namespace ZipPostLookup.CountryDataTools.Export;

/// <summary>
/// Mutable row model passed through the export pipeline stages.
/// Populated via object initialiser; stages may transform
/// fields in-place (e.g. range-compress the code, strip lat/lng).
/// </summary>
internal sealed class ExportRow
{
    public string ZpCode     { get; set; } = "";
    public string PlaceName  { get; set; } = "";
    public string Timezone  { get; set; } = "";
    public bool   IsDefault { get; set; }
    public string Lat       { get; set; } = "---";
    public string Lng       { get; set; } = "---";
    public string Admin1    { get; set; } = "---";
    public string Admin1Code{ get; set; } = "---";
}
