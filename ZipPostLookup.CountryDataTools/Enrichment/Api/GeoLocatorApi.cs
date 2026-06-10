using System.Net;
using System.Text.Json;
using GeoTimeZone;
using ZipPostLookup.CountryDataTools.CountryRules;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

/// <summary>
/// Canadian Geospatial Platform GeoLocator API (https://geolocator.api.geo.ca).
/// CA-only. No API key required.
/// Queries the FSA (Forward Sortation Area) data source for province and coordinates.
/// Does not return a place name — only admin1 (province) and lat/lon → timezone.
/// </summary>
internal sealed class GeoLocatorApi : EnrichmentApiBase
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "CA" };

    private const string BaseUrl = "https://geolocator.api.geo.ca/";

    private static readonly ICountryRules _caRules = CountryRulesFactory.For("CA");

    public GeoLocatorApi(HttpClient http) : base(http) { }

    public override string Name => "GeoLocator";
    public override IReadOnlySet<string> SupportedCountries => _countries;

    protected override string? BuildUrl(string country, string code, string? stateAbbr) =>
        $"{BaseUrl}?q={Uri.EscapeDataString(code)}&lang=en&keys=fsa";

    protected override FetchResult? MapStatus(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.NotFound
            ? new FetchResult(null, FetchOutcome.NotFound)
            : null;

    protected override async Task<FetchResult> ParseAsync(
        HttpResponseMessage response, string country, string code, CancellationToken ct)
    {
        var json = await ReadJsonAsync(response, ct);

        if (json.ValueKind != JsonValueKind.Array)
            return (null, FetchOutcome.NotFound);

        // Find the first result with key == "fsa"
        JsonElement? fsa = null;
        foreach (var item in json.EnumerateArray())
        {
            if (item.TryGetProperty("key", out var k) && k.GetString() == "fsa")
            {
                fsa = item;
                break;
            }
        }

        if (fsa is null)
            return (null, FetchOutcome.NotFound);

        var rawProvince = fsa.Value.TryGetProperty("province", out var p) ? p.GetString() ?? "" : "";
        var lat = fsa.Value.TryGetProperty("lat", out var latEl) ? latEl.GetDouble() : 0.0;
        var lon = fsa.Value.TryGetProperty("lng", out var lonEl) ? lonEl.GetDouble() : 0.0;

        if (string.IsNullOrWhiteSpace(rawProvince))
            return (null, FetchOutcome.TransientError, "missing province");

        var admin1Code = _caRules.ResolveAdmin1CodeFromName(rawProvince) ?? rawProvince.ToUpperInvariant();
        var admin1Name = rawProvince;

        string? iana = null;
        if (lat != 0 || lon != 0)
        {
            var tzResult = TimeZoneLookup.GetTimeZone(lat, lon).Result;
            if (!string.IsNullOrWhiteSpace(tzResult) && tzResult.Contains('/'))
                iana = tzResult;
        }

        return (new ApiLookupResult
        {
            // GeoLocator returns the FSA code as name, not a city — PlaceName left empty.
            // UpdateReferenceAsync handles empty PlaceName: sets NameChecked=false and
            // falls back to GetReferenceByCode so only admin + timezone are updated.
            Admin1Code = admin1Code,
            Admin1Name = admin1Name,
            Timezone   = iana,
            Lat        = lat,
            Lon        = lon,
        }, FetchOutcome.Found);
    }
}
