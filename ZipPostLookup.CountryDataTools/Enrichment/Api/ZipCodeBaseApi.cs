using System.Net.Http.Json;
using System.Text.Json;
using ZipPostLookup.CountryDataTools.Pipeline;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

/// <summary>
/// ZipCodeBase API (https://zipcodebase.com).
/// Supports US, CA, MX. API key required. Monthly call limit (not daily).
/// Returns city, state/province code, timezone, and coordinates.
/// 401/403 (bad/expired key) removes this API from rotation.
/// </summary>
internal sealed class ZipCodeBaseApi : IEnrichmentApi
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "US", "CA", "MX" };

    private const string BaseUrl = "https://app.zipcodebase.com/api/v1/search";

    private readonly HttpClient _http;
    private readonly string     _apiKey;
    private readonly int?       _monthlyLimit;

    public ZipCodeBaseApi(HttpClient http, string apiKey, int? monthlyLimit = null)
    {
        _http         = http;
        _apiKey       = apiKey;
        _monthlyLimit = monthlyLimit;
    }

    public string               Name               => "ZipCodeBase";
    public IReadOnlySet<string> SupportedCountries => _countries;
    public int?                 DailyLimit         => null;
    public int?                 MonthlyLimit       => _monthlyLimit;

    public async Task<FetchResult> LookupAsync(
        string country, string code, string? stateAbbr, CancellationToken ct = default)
    {
        var cc  = country.ToUpperInvariant();
        var url = $"{BaseUrl}?apikey={_apiKey}&codes={Uri.EscapeDataString(code)}&country={cc}";

        try
        {
            var response = await _http.GetAsync(url, ct);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                    or System.Net.HttpStatusCode.Forbidden)
                return (null, FetchOutcome.RateLimited);

            if ((int)response.StatusCode == 429)
                return (null, FetchOutcome.RateLimited);

            if (!response.IsSuccessStatusCode)
                return (null, FetchOutcome.TransientError, $"HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            // Response: { "results": { "{code}": [ { city, state, state_code, timezone, ... } ] } }
            // Not-found codes may return results as an empty array [] instead of an object {}.
            if (!json.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Object)
                return (null, FetchOutcome.NotFound);

            // The results dict is keyed by the requested code; value may be null or empty array.
            if (!results.TryGetProperty(code, out var codeArray) ||
                codeArray.ValueKind != JsonValueKind.Array ||
                codeArray.GetArrayLength() == 0)
                return (null, FetchOutcome.NotFound);

            var r = codeArray[0];

            var city      = r.TryGetProperty("city",       out var c) ? c.GetString() ?? "" : "";
            var stateCode = r.TryGetProperty("state_code", out var sc) ? sc.GetString() ?? "" : "";
            var stateName = r.TryGetProperty("state",      out var sn) ? sn.GetString() ?? "" : "";
            var iana      = r.TryGetProperty("timezone",   out var tz) ? tz.GetString()       : null;

            if (string.IsNullOrWhiteSpace(city) && string.IsNullOrWhiteSpace(stateCode))
                return (null, FetchOutcome.NotFound);

            var lat = 0.0;
            var lon = 0.0;
            if (r.TryGetProperty("latitude",  out var latEl) &&
                double.TryParse(latEl.GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsedLat))
                lat = parsedLat;
            if (r.TryGetProperty("longitude", out var lonEl) &&
                double.TryParse(lonEl.GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsedLon))
                lon = parsedLon;

            var stateMatch = StateResolver.Resolve(stateCode) ?? StateResolver.Resolve(stateName);
            var admin1Code = stateMatch?.StateCode ?? stateCode.ToUpperInvariant();
            var admin1Name = stateMatch?.StateName ?? stateName;

            return (new ApiLookupResult
            {
                PlaceName  = city,
                Admin1Code = admin1Code,
                Admin1Name = admin1Name,
                Timezone   = iana,
                Lat        = lat,
                Lon        = lon,
            }, FetchOutcome.Found);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, FetchOutcome.TransientError, $"{ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[ZipCodeBase] Unexpected error for {code}: {ex.Message}");
            return (null, FetchOutcome.TransientError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
