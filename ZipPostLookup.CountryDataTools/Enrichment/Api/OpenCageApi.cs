using System.Net;
using System.Text.Json;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

/// <summary>
/// OpenCage Geocoding API (https://opencagedata.com/api).
/// Supports US, CA, MX. API key required (free tier: 2,500 requests/day).
/// Returns place name, state/province, timezone, and coordinates.
/// A 401/402/403 response (bad/expired/quota-exceeded key) removes this API from rotation.
/// Zero results returned as HTTP 200 with total_results=0 — treated as NotFound.
/// </summary>
internal sealed class OpenCageApi : EnrichmentApiBase
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "US", "CA", "MX" };

    private const string BaseUrl = "https://api.opencagedata.com/geocode/v1/json";

    private readonly string _apiKey;
    private readonly int?   _dailyLimit;

    public OpenCageApi(HttpClient http, string apiKey, int? dailyLimit = null) : base(http)
    {
        _apiKey     = apiKey;
        _dailyLimit = dailyLimit;
    }

    public override string               Name               => "OpenCage";
    public override IReadOnlySet<string> SupportedCountries => _countries;
    public override int?                 DailyLimit         => _dailyLimit;

    protected override string? BuildUrl(string country, string code, string? stateAbbr)
    {
        var cc = country.ToLowerInvariant();
        return $"{BaseUrl}?q={Uri.EscapeDataString(code)}&countrycode={cc}&limit=1&key={_apiKey}";
    }

    // Bad key / quota exceeded (402 Payment Required) — drop from rotation.
    protected override FetchResult? MapStatus(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.PaymentRequired
            ? new FetchResult(null, FetchOutcome.RateLimited)
            : null;

    protected override async Task<FetchResult> ParseAsync(
        HttpResponseMessage response, string country, string code, CancellationToken ct)
    {
        var json = await ReadJsonAsync(response, ct);

        // OpenCage returns HTTP 200 with total_results=0 when nothing matches
        if (json.TryGetProperty("total_results", out var total) && total.GetInt32() == 0)
            return (null, FetchOutcome.NotFound);

        if (!json.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return (null, FetchOutcome.NotFound);

        var r = results[0];

        if (!r.TryGetProperty("components", out var components))
            return (null, FetchOutcome.TransientError, "missing components");

        // Place name: _normalized_city covers city/town/village/municipality in priority order
        var rawCity = components.TryGetProperty("_normalized_city", out var nc) ? nc.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(rawCity))
            rawCity = components.TryGetProperty("city",  out var c) ? c.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(rawCity))
            rawCity = components.TryGetProperty("town",  out var t) ? t.GetString() ?? "" : "";

        var rawStateCode = components.TryGetProperty("state_code", out var sc) ? sc.GetString() ?? "" : "";
        var rawStateName = components.TryGetProperty("state",      out var sn) ? sn.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(rawCity) && string.IsNullOrWhiteSpace(rawStateCode))
            return (null, FetchOutcome.TransientError, "empty city and state");

        var lat = 0.0;
        var lon = 0.0;
        if (r.TryGetProperty("geometry", out var geo))
        {
            if (geo.TryGetProperty("lat", out var latEl) && latEl.TryGetDouble(out var latD)) lat = latD;
            if (geo.TryGetProperty("lng", out var lonEl) && lonEl.TryGetDouble(out var lonD)) lon = lonD;
        }

        // Timezone is provided directly in annotations
        string? iana = null;
        if (r.TryGetProperty("annotations", out var ann) &&
            ann.TryGetProperty("timezone",   out var tz)  &&
            tz.TryGetProperty("name",        out var tzName))
            iana = tzName.GetString();

        // StateResolver covers US; for CA/MX the state_code from the API is used directly
        var (admin1Code, admin1Name) = ResolveAdmin1(rawStateCode, rawStateName);

        return (new ApiLookupResult
        {
            PlaceName  = TitleCase(rawCity),
            Admin1Code = admin1Code,
            Admin1Name = admin1Name,
            Timezone   = iana,
            Lat        = lat,
            Lon        = lon,
        }, FetchOutcome.Found);
    }
}
