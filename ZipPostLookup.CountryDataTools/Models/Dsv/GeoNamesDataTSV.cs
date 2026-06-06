using System.Globalization;

namespace ZipPostLookup.CountryDataTools.Models.Dsv;

/// <summary>
/// Represents a single row in a GeoNames postal-code TSV file.
///
/// Format: tab-delimited, no header row, 12 columns.
///   0  country_code  — 2-letter ISO country code (e.g. "US")
///   1  postal_code   — the postal / ZIP code
///   2  place_name    — locality name
///   3  admin_name1   — state / province name
///   4  admin_code1   — state / province code
///   5  admin_name2   — county / district name
///   6  admin_code2   — county / district code
///   7  admin_name3   — community name
///   8  admin_code3   — community code
///   9  latitude      — WGS-84 latitude  (abs ≤ 90)
///  10  longitude     — WGS-84 longitude
///  11  accuracy      — GeoNames accuracy rating (ignored)
///
/// Detection heuristic: column 0 is a 2-letter all-alpha string AND
/// column 9 parses as a double with abs ≤ 90.
/// </summary>
public class GeoNamesDataTSV : ITsvDsvModel
{
    public static string FormatName => "GeoNames";

    /// <inheritdoc cref="ITsvDsvModel.Detect"/>
    public static bool Detect(string firstLine)
    {
        var tabs = firstLine.Split('\t');
        if (tabs.Length < 11) return false;
        var col0 = tabs[0].Trim();
        var col9 = tabs[9].Trim();
        return col0.Length == 2
            && col0.All(char.IsLetter)
            && double.TryParse(col9, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            && Math.Abs(lat) <= 90.0;
    }

    // ITsvDsvModel.City bridges to PlaceName so OSM and GeoNames share the interface
    // without renaming this well-established GeoNames field name.
    string ITsvDsvModel.City => PlaceName;

    public string CountryCode { get; set; } = string.Empty;
    public string PostalCode  { get; set; } = string.Empty;
    public string PlaceName   { get; set; } = string.Empty;
    public string AdminName1  { get; set; } = "---";
    public string AdminCode1  { get; set; } = "---";
    public string AdminName2  { get; set; } = "---";
    public string AdminCode2  { get; set; } = "---";
    public string AdminName3  { get; set; } = "---";
    public string AdminCode3  { get; set; } = "---";
    public string Latitude    { get; set; } = "---";
    public string Longitude   { get; set; } = "---";
    public string Accuracy    { get; set; } = string.Empty;
}