using System.Net.Http.Json;
using System.Text.Json;

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
internal sealed class AbstractApi : IEnrichmentApi
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

    private readonly HttpClient _http;
    private readonly string     _apiKey;
    private readonly int?       _dailyLimit;

    public AbstractApi(HttpClient http, string apiKey, int? dailyLimit = null)
    {
        _http       = http;
        _apiKey     = apiKey;
        _dailyLimit = dailyLimit;
    }

    public string                 Name               => "AbstractAPI";
    public IReadOnlySet<string>   SupportedCountries => _countries;
    public int?                   DailyLimit         => _dailyLimit;
    public int?                   MonthlyLimit       => null;

    public async Task<FetchResult> LookupAsync(
        string country, string code, string? stateAbbr, CancellationToken ct = default)
    {
        if (!_countryName.TryGetValue(country, out var countryName))
            return (null, FetchOutcome.NotFound);

        var location = $"{code}, {countryName}";
        var url      = $"{BaseUrl}?api_key={_apiKey}&location={Uri.EscapeDataString(location)}";

        try
        {
            var response = await _http.GetAsync(url, ct);

            // 204 = no location data found
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return (null, FetchOutcome.NotFound);

            // Bad key, blocked, or quota exhausted — drop from rotation
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                    or System.Net.HttpStatusCode.Forbidden
                                    or System.Net.HttpStatusCode.UnprocessableEntity)
                return (null, FetchOutcome.RateLimited);

            if ((int)response.StatusCode == 429)
                return (null, FetchOutcome.RateLimited);

            if (!response.IsSuccessStatusCode)
                return (null, FetchOutcome.TransientError, $"HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, FetchOutcome.TransientError, $"{ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[AbstractAPI] Unexpected error for {code}: {ex.Message}");
            return (null, FetchOutcome.TransientError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
