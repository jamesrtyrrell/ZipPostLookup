namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

internal sealed class ApiLookupResult
{
    public string PlaceName  { get; init; } = "";
    public string Admin1Code { get; init; } = "";
    public string Admin1Name { get; init; } = "";
    public string? Timezone  { get; init; }   // null when API doesn't return it (Ziptastic)
    public double  Lat       { get; init; }
    public double  Lon       { get; init; }
}
