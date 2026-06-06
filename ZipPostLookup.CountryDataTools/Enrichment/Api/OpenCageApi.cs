using System.Net.Http.Json;
using System.Text.Json;
using ZipPostLookup.CountryDataTools.Pipeline;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

/// <summary>
/// OpenCage Geocoding API (https://opencagedata.com/api).
/// Supports US, CA, MX. API key required (free tier: 2,500 requests/day).
/// Returns place name, state/province, timezone, and coordinates.
/// A 401/402/403 response (bad/expired/quota-exceeded key) removes this API from rotation.
/// Zero results returned as HTTP 200 with total_results=0 — treated as NotFound.
/// </summary>
internal sealed class OpenCageApi : IEnrichmentApi
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "US", "CA", "MX" };

    private const string BaseUrl = "https://api.opencagedata.com/geocode/v1/json";

    private readonly HttpClient _http;
    private readonly string     _apiKey;
    private readonly int?       _dailyLimit;

    public OpenCageApi(HttpClient http, string apiKey, int? dailyLimit = null)
    {
        _http       = http;
        _apiKey     = apiKey;
        _dailyLimit = dailyLimit;
    }

    public string                 Name               => "OpenCage";
    public IReadOnlySet<string>   SupportedCountries => _countries;
    public int?                   DailyLimit         => _dailyLimit;
    public int?                   MonthlyLimit       => null;

    public async Task<(ApiLookupResult? Result, FetchOutcome Outcome)> LookupAsync(
        string country, string code, string? stateAbbr, CancellationToken ct = default)
    {
        var cc  = country.ToLowerInvariant();
        var url = $"{BaseUrl}?q={Uri.EscapeDataString(code)}&countrycode={cc}&limit=1&key={_apiKey}";

        try
        {
            var response = await _http.GetAsync(url, ct);

            // Bad key / quota exceeded — drop from rotation
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                    or System.Net.HttpStatusCode.PaymentRequired
                                    or System.Net.HttpStatusCode.Forbidden)
                return (null, FetchOutcome.RateLimited);

            if ((int)response.StatusCode == 429)
                return (null, FetchOutcome.RateLimited);

            if (!response.IsSuccessStatusCode)
                return (null, FetchOutcome.TransientError);

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            // OpenCage returns HTTP 200 with total_results=0 when nothing matches
            if (json.TryGetProperty("total_results", out var total) && total.GetInt32() == 0)
                return (null, FetchOutcome.NotFound);

            if (!json.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                return (null, FetchOutcome.NotFound);

            var r = results[0];

            if (!r.TryGetProperty("components", out var components))
                return (null, FetchOutcome.TransientError);

            // Place name: _normalized_city covers city/town/village/municipality in priority order
            var rawCity = components.TryGetProperty("_normalized_city", out var nc) ? nc.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(rawCity))
                rawCity = components.TryGetProperty("city",  out var c) ? c.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(rawCity))
                rawCity = components.TryGetProperty("town",  out var t) ? t.GetString() ?? "" : "";

            var rawStateCode = components.TryGetProperty("state_code", out var sc) ? sc.GetString() ?? "" : "";
            var rawStateName = components.TryGetProperty("state",      out var sn) ? sn.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(rawCity) && string.IsNullOrWhiteSpace(rawStateCode))
                return (null, FetchOutcome.TransientError);

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
            var stateMatch = StateResolver.Resolve(rawStateCode) ?? StateResolver.Resolve(rawStateName);
            var admin1Code = stateMatch?.StateCode ?? rawStateCode.ToUpperInvariant();
            var admin1Name = stateMatch?.StateName ?? rawStateName;

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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, FetchOutcome.TransientError);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[OpenCage] Unexpected error for {code}: {ex.Message}");
            return (null, FetchOutcome.TransientError);
        }
    }

    private static string TitleCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var words = input.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length == 0) continue;
            words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..].ToLowerInvariant();
        }
        return string.Join(' ', words);
    }
}
