namespace ZipPostLookup.CountryDataTools.Models.Dsv;

/// <summary>
/// Represents a single row in an OpenStreetMap Streets TSV/CSV file.
///
/// Format: tab- or comma-delimited, with a header row.
/// Common columns (order is header-driven, not positional):
///   suburb       — suburb name (optional)
///   country      — country name (optional)
///   state        — state / province name  (used as Admin1; some files use "province" instead)
///   province     — province name          (CA variant of state)
///   city         — city / locality name
///   district     — district name (optional)
///   postal_code  — the postal / ZIP code
///   street_name  — street name (one row per street; deduplicated to one output row per code+city)
///
/// Detection heuristic: header row contains both "postal_code" and "street_name".
///
/// No lat/lng is available in this format; coordinates are left as "---" in the output.
/// </summary>
public class OsmStreetsDataTSV : ITsvDsvModel
{
    public static string FormatName => "OpenStreetMap Streets";

    /// <inheritdoc cref="ITsvDsvModel.Detect"/>
    public static bool Detect(string firstLine)
    {
        var tabs   = firstLine.Split('\t');
        var commas = firstLine.Split(',');
        return (HeaderContains(tabs,   "postal_code") && HeaderContains(tabs,   "street_name"))
            || (HeaderContains(commas, "postal_code") && HeaderContains(commas, "street_name"));
    }

    private static bool HeaderContains(string[] tokens, string name) =>
        tokens.Any(t => t.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

    public string? Suburb      { get; set; }
    public string? Country     { get; set; }
    public string? State       { get; set; }
    public string? Province    { get; set; }
    public string  City        { get; set; } = string.Empty;
    public string? District    { get; set; }
    public string  PostalCode  { get; set; } = string.Empty;
    public string  StreetName  { get; set; } = string.Empty;
}