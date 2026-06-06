using System.Net.Http.Json;
using System.Text.Json;
using ZipPostLookup.CountryDataTools.Pipeline;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

/// <summary>
/// Geocodio geocoding API used as a postal-code lookup
/// (https://www.geocod.io/docs/#geocoding).
/// Supports US, CA, MX. Returns timezone via the fields=timezone append.
/// A 401/403 response removes this API from the session rotation.
/// </summary>
internal sealed class GeocodioApi : IEnrichmentApi
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "US", "CA", "MX" };

    // Geocodio expects full country name, not ISO code.
    private static readonly Dictionary<string, string> _countryParam =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "US", "USA"    },
            { "CA", "Canada" },
            { "MX", "Mexico" },
        };

    private const string BaseUrl = "https://api.geocod.io/v2/geocode";

    private readonly HttpClient _http;
    private readonly string     _apiKey;
    private readonly int?       _dailyLimit;

    public GeocodioApi(HttpClient http, string apiKey, int? dailyLimit = null)
    {
        _http       = http;
        _apiKey     = apiKey;
        _dailyLimit = dailyLimit;
    }

    public string                 Name               => "Geocodio";
    public IReadOnlySet<string>   SupportedCountries => _countries;
    public int?                   DailyLimit         => _dailyLimit;
    public int?                   MonthlyLimit       => null;

    public async Task<(ApiLookupResult? Result, FetchOutcome Outcome)> LookupAsync(
        string country, string code, string? stateAbbr, CancellationToken ct = default)
    {
        if (!_countryParam.TryGetValue(country, out var countryParam))
            return (null, FetchOutcome.NotFound);

        var url = $"{BaseUrl}?q={Uri.EscapeDataString(code)}" +
                  $"&country={countryParam}&fields=timezone&api_key={_apiKey}";

        try
        {
            var response = await _http.GetAsync(url, ct);

            // Bad/missing key — drop from rotation.
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                    or System.Net.HttpStatusCode.Forbidden)
                return (null, FetchOutcome.RateLimited);

            if ((int)response.StatusCode == 429)
                return (null, FetchOutcome.RateLimited);

            // 422 = query could not be parsed — treat as not found.
            if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
                return (null, FetchOutcome.NotFound);

            if (!response.IsSuccessStatusCode)
                return (null, FetchOutcome.TransientError);

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            if (!json.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                return (null, FetchOutcome.NotFound);

            var r = results[0];

            string rawCity = "", rawStateCode = "";
            if (r.TryGetProperty("address_components", out var ac))
            {
                rawCity      = ac.TryGetProperty("city",           out var c)  ? c.GetString() ?? "" : "";
                rawStateCode = ac.TryGetProperty("state_province", out var sp) ? sp.GetString() ?? "" : "";
            }

            var lat = 0.0;
            var lng = 0.0;
            if (r.TryGetProperty("location", out var loc))
            {
                if (loc.TryGetProperty("lat", out var latEl) && latEl.TryGetDouble(out var latD)) lat = latD;
                if (loc.TryGetProperty("lng", out var lngEl) && lngEl.TryGetDouble(out var lngD)) lng = lngD;
            }

            string? iana = null;
            if (r.TryGetProperty("timezone", out var tz) && tz.TryGetProperty("name", out var tzName))
                iana = tzName.GetString();

            if (string.IsNullOrWhiteSpace(rawCity) && string.IsNullOrWhiteSpace(rawStateCode))
                return (null, FetchOutcome.TransientError);

            var stateMatch = StateResolver.Resolve(rawStateCode);
            var admin1Code = stateMatch?.StateCode ?? rawStateCode.ToUpperInvariant();
            var admin1Name = stateMatch?.StateName ?? rawStateCode;

            return (new ApiLookupResult
            {
                PlaceName  = rawCity,
                Admin1Code = admin1Code,
                Admin1Name = admin1Name,
                Timezone   = iana,
                Lat        = lat,
                Lon        = lng,
            }, FetchOutcome.Found);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, FetchOutcome.TransientError);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[Geocodio] Unexpected error for {code}: {ex.Message}");
            return (null, FetchOutcome.TransientError);
        }
    }
}
