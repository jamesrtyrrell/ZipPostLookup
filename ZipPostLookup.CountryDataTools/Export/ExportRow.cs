using ZipPostLookup.Core;

namespace ZipPostLookup.CountryDataTools.Export;

/// <summary>
/// Mutable row model passed through the export pipeline stages.
/// Initialised from a <see cref="CodeEntry"/>; stages may transform
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

    public ExportRow() { }

    /// <summary>Initialises a row from a <see cref="CodeEntry"/>.</summary>
    public ExportRow(CodeEntry entry)
    {
        ZpCode     = entry.ZpCode;
        PlaceName  = entry.PlaceName;
        Timezone   = entry.Timezone;
        IsDefault  = entry.IsDefault;
        Lat        = "---";
        Lng        = "---";
        Admin1     = entry.Admin1     ?? "---";
        Admin1Code = entry.Admin1Code ?? "---";
    }
}
