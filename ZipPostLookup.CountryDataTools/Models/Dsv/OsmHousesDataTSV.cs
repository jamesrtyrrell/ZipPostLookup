namespace ZipPostLookup.CountryDataTools.Models.Dsv;

/// <summary>
/// Represents a single row in an OpenStreetMap Houses TSV file.
///
/// Format: tab-delimited, with a header row.
/// Columns (order is header-driven, not positional):
///   postal_code   — the postal / ZIP code
///   city          — city / locality name
///   street        — street name (optional)
///   house_number  — individual house number (one row per house; deduplicated to one output row per code+city)
///   x             — WGS-84 longitude of the house
///   y             — WGS-84 latitude  of the house
///   country       — country code or name (optional, ignored)
///
/// Detection heuristic: header row contains both "postal_code" and "house_number"
/// (but NOT "postal_code_" — that belongs to OsmAddresses).
///
/// lat/lng in the output are the averages of y and x across all houses
/// sharing the same (postal_code, city) pair.
/// </summary>
public class OsmHousesDataTSV : ITsvDsvModel
{
    public static string FormatName => "OpenStreetMap Houses";

    /// <inheritdoc cref="ITsvDsvModel.Detect"/>
    public static bool Detect(string firstLine)
    {
        var tabs = firstLine.Split('\t');
        return HeaderContains(tabs, "postal_code") && HeaderContains(tabs, "house_number");
    }

    private static bool HeaderContains(string[] tokens, string name) =>
        tokens.Any(t => t.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));

    public string  PostalCode   { get; set; } = string.Empty;
    public string  City         { get; set; } = string.Empty;
    public string? Street       { get; set; }
    public string  HouseNumber  { get; set; } = string.Empty;
    public string? X            { get; set; }
    public string? Y            { get; set; }
    public string? Country      { get; set; }
}