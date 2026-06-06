using System.Net.Http.Json;
using System.Text.Json;
using ZipPostLookup.CountryDataTools.Pipeline;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

/// <summary>
/// GeoApify Postcode Search API (https://apidocs.geoapify.com/docs/postcode/#search-api).
/// Supports US, CA, MX. Returns timezone directly in the response.
/// Pricing: 1 credit per 20 postcodes; free plan = 3,000 credits/day.
/// A 401/403 response (bad/missing key) removes this API from the session rotation.
/// </summary>
internal sealed class GeoApifyApi : IEnrichmentApi
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "US", "CA", "MX" };

    private const string BaseUrl = "https://api.geoapify.com/v1/postcode/search";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly int? _dailyLimit;

    public GeoApifyApi(HttpClient http, string apiKey, int? dailyLimit = null)
    {
        _http       = http;
        _apiKey     = apiKey;
        _dailyLimit = dailyLimit;
    }

    public string Name => "GeoApify";
    public IReadOnlySet<string> SupportedCountries => _countries;
    public int? DailyLimit   => _dailyLimit;
    public int? MonthlyLimit => null;

    public async Task<(ApiLookupResult? Result, FetchOutcome Outcome)> LookupAsync(
        string country, string code, string? stateAbbr, CancellationToken ct = default)
    {
        var cc  = country.ToLowerInvariant();
        var url = $"{BaseUrl}?postcode={Uri.EscapeDataString(code)}&countrycode={cc}&format=json&apiKey={_apiKey}";

        try
        {
            var response = await _http.GetAsync(url, ct);

            // Bad/missing key — drop from rotation so we stop burning time on auth failures.
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                    or System.Net.HttpStatusCode.Forbidden)
                return (null, FetchOutcome.RateLimited);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (null, FetchOutcome.NotFound);

            if ((int)response.StatusCode == 429)
                return (null, FetchOutcome.RateLimited);

            if (!response.IsSuccessStatusCode)
                return (null, FetchOutcome.TransientError);

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

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
                return (null, FetchOutcome.TransientError);

            var stateMatch = StateResolver.Resolve(rawStateCode) ?? StateResolver.Resolve(rawStateName);
            var admin1Code = stateMatch?.StateCode ?? rawStateCode.ToUpperInvariant();
            var admin1Name = stateMatch?.StateName ?? rawStateName;

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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, FetchOutcome.TransientError);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[GeoApify] Unexpected error for {code}: {ex.Message}");
            return (null, FetchOutcome.TransientError);
        }
    }
}
