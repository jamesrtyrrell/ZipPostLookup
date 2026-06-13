namespace ZipPostLookup.CountryDataTools.Models.Counters;

public sealed class EnrichCoordinateCounters
{
    public int TzUpdated    { get; set; }
    public int TzSkipped    { get; set; }
    public int CityChecked  { get; set; }
    public int CoordsFilled { get; set; }
    public int NotFound     { get; set; }
}
