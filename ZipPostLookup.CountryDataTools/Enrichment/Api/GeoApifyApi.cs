using System.Net;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

/// <summary>
/// GeoApify Postcode Search API (https://apidocs.geoapify.com/docs/postcode/#search-api).
/// Supports US, CA, MX. Returns timezone directly in the response.
/// Pricing: 1 credit per 20 postcodes; free plan = 3,000 credits/day.
/// A 401/403 response (bad/missing key) removes this API from the session rotation.
/// </summary>
internal sealed class GeoApifyApi : EnrichmentApiBase
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "US", "CA", "MX" };

    private const string BaseUrl = "https://api.geoapify.com/v1/postcode/search";

    private readonly string _apiKey;
    private readonly int? _dailyLimit;

    public GeoApifyApi(HttpClient http, string apiKey, int? dailyLimit = null) : base(http)
    {
        _apiKey     = apiKey;
        _dailyLimit = dailyLimit;
    }

    public override string Name => "GeoApify";
    public override IReadOnlySet<string> SupportedCountries => _countries;
    public override int? DailyLimit => _dailyLimit;

    protected override string? BuildUrl(string country, string code, string? stateAbbr)
    {
        var cc = country.ToLowerInvariant();
        return $"{BaseUrl}?postcode={Uri.EscapeDataString(code)}&countrycode={cc}&format=json&apiKey={_apiKey}";
    }

    protected override FetchResult? MapStatus(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.NotFound
            ? new FetchResult(null, FetchOutcome.NotFound)
            : null;

    protected override async Task<FetchResult> ParseAsync(
        HttpResponseMessage response, string country, string code, CancellationToken ct)
    {
        var json = await ReadJsonAsync(response, ct);

        if (!json.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return (null, FetchOutcome.NotFound);

        var r = results[0];

        var rawCity      = r.TryGetProperty("city",       out var c)  ? c.GetString()  ?? "" : "";
        var rawStateCode = r.TryGetProperty("state_code", out var sc) ? sc.GetString() ?? "" : "";
        var rawStateName = r.TryGetProperty("state",      out var sn) ? sn.GetString() ?? "" : "";

        var lat = r.TryGetProperty("lat", out var latEl) && latEl.TryGetDouble(out var latD) ? latD : 0.0;
        var lon = r.TryGetProperty("lon", out var lonEl) && lonEl.TryGetDouble(out var lonD) ? lonD : 0.0;

        string? iana = null;
        if (r.TryGetProperty("timezone", out var tz) && tz.TryGetProperty("name", out var tzName))
            iana = tzName.GetString();

        if (string.IsNullOrWhiteSpace(rawCity) && string.IsNullOrWhiteSpace(rawStateName))
            return (null, FetchOutcome.TransientError, "missing city and state");

        var (admin1Code, admin1Name) = ResolveAdmin1(rawStateCode, rawStateName);

        return (new ApiLookupResult
        {
            PlaceName  = rawCity,
            Admin1Code = admin1Code,
            Admin1Name = admin1Name,
            Timezone   = iana,
            Lat        = lat,
            Lon        = lon,
        }, FetchOutcome.Found);
    }
}
