namespace ZipPostLookup.CountryDataTools.Models.Commands;

public sealed class EnrichCoordinateCounters
{
    public int TzUpdated   { get; set; }
    public int TzSkipped   { get; set; }
    public int CityChecked { get; set; }
    public int NotFound    { get; set; }
}
