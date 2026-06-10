using System.Net;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

/// <summary>
/// Abstract Timezone API (https://docs.abstractapi.com/api/timezones).
/// Supports US, CA, MX. API key required (free tier: 5,000 requests/day, 1 req/sec).
/// Returns timezone and coordinates only — no place name or admin data.
/// Useful as a fallback for codes that are missing timezone and lat/lng.
/// Query format: "{postalCode}, {countryFullName}" to avoid cross-country mis-matches.
/// A 401/403/422 response (bad/blocked key or quota exhausted) removes this API from rotation.
/// No-results are returned as HTTP 204 — treated as NotFound.
/// </summary>
internal sealed class AbstractTimezoneApi : EnrichmentApiBase
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "US", "CA", "MX" };

    private static readonly Dictionary<string, string> _countryName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["US"] = "United States",
            ["CA"] = "Canada",
            ["MX"] = "Mexico",
        };

    private const string BaseUrl = "https://timezone.abstractapi.com/v1/current_time/";

    private readonly string _apiKey;
    private readonly int?   _dailyLimit;

    public AbstractTimezoneApi(HttpClient http, string apiKey, int? dailyLimit = null) : base(http)
    {
        _apiKey     = apiKey;
        _dailyLimit = dailyLimit;
    }

    public override string                 Name               => "AbstractAPI";
    public override IReadOnlySet<string>   SupportedCountries => _countries;
    public override int?                   DailyLimit         => _dailyLimit;

    protected override string? BuildUrl(string country, string code, string? stateAbbr)
    {
        if (!_countryName.TryGetValue(country, out var countryName))
            return null;

        var location = $"{code}, {countryName}";
        return $"{BaseUrl}?api_key={_apiKey}&location={Uri.EscapeDataString(location)}";
    }

    protected override FetchResult? MapStatus(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.NoContent           => new FetchResult(null, FetchOutcome.NotFound),    // 204 = no location data
        HttpStatusCode.UnprocessableEntity => new FetchResult(null, FetchOutcome.RateLimited), // 422 = bad key / quota
        _                                  => null,
    };

    protected override async Task<FetchResult> ParseAsync(
        HttpResponseMessage response, string country, string code, CancellationToken ct)
    {
        var json = await ReadJsonAsync(response, ct);

        var iana = json.TryGetProperty("timezone_location", out var tz) ? tz.GetString() : null;
        var lat = json.TryGetProperty("latitude",  out var latEl) && latEl.TryGetDouble(out var latD) ? latD : 0.0;
        var lon = json.TryGetProperty("longitude", out var lonEl) && lonEl.TryGetDouble(out var lonD) ? lonD : 0.0;

        if (string.IsNullOrWhiteSpace(iana) || !iana.Contains('/'))
            return (null, FetchOutcome.TransientError, "missing or invalid timezone in response");

        return (new ApiLookupResult
        {
            // No place name or admin data from this API — timezone + coordinates only
            Timezone = iana,
            Lat      = lat,
            Lon      = lon,
        }, FetchOutcome.Found);
    }
}
