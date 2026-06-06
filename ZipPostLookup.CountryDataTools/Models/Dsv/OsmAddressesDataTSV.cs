namespace ZipPostLookup.CountryDataTools.Models.Dsv;

/// <summary>
/// Represents a single row in an OpenStreetMap Addresses TSV file.
///
/// Format: tab-delimited, with a header row.
/// Columns (order is header-driven, not positional):
///   postal_code_  — the postal / ZIP code   (trailing underscore distinguishes from OSM Houses)
///   city_         — city / locality name     (trailing underscore distinguishes from OSM Houses)
///   street_       — street name (optional)
///   x_min         — western bounding longitude
///   x_max         — eastern bounding longitude
///   y_min         — southern bounding latitude
///   y_max         — northern bounding latitude
///   house_min     — lowest house number in range (optional, ignored)
///   house_max     — highest house number in range (optional, ignored)
///   house_odd     — odd house numbers flag (optional, ignored)
///   house_even    — even house numbers flag (optional, ignored)
///
/// Detection heuristic: header row contains both "postal_code_" and "city_".
///
/// lat/lng in the output are the midpoints of (y_min+y_max)/2 and (x_min+x_max)/2,
/// averaged across all rows sharing the same (postal_code_, city_) pair.
/// </summary>
public class OsmAddressesDataTSV : ITsvDsvModel
{
    public static string FormatName => "OpenStreetMap Addresses";

    /// <inheritdoc cref="ITsvDsvModel.Detect"/>
    public static bool Detect(string firstLine)
    {
        var tabs = firstLine.Split('\t');
        return HeaderContains(tabs, "postal_code_") && HeaderContains(tabs, "city_");
    }

    private static bool HeaderContains(string[] tokens, string name) =>
        tokens.Any(t => t.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

    public string  PostalCode { get; set; } = string.Empty;
    public string  City       { get; set; } = string.Empty;
    public string? Street     { get; set; }
    public string? XMin       { get; set; }
    public string? XMax       { get; set; }
    public string? YMin       { get; set; }
    public string? YMax       { get; set; }
    public string? HouseMin   { get; set; }
    public string? HouseMax   { get; set; }
    public string? HouseOdd   { get; set; }
    public string? HouseEven  { get; set; }
}