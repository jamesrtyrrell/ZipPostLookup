using System.Net.Http.Json;
using System.Text.Json;
using GeoTimeZone;
using ZipPostLookup.CountryDataTools.Validation;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

/// <summary>
/// Canadian Geospatial Platform GeoLocator API (https://geolocator.api.geo.ca).
/// CA-only. No API key required.
/// Queries the FSA (Forward Sortation Area) data source for province and coordinates.
/// Does not return a place name — only admin1 (province) and lat/lon → timezone.
/// </summary>
internal sealed class GeoLocatorApi : IEnrichmentApi
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "CA" };

    private const string BaseUrl = "https://geolocator.api.geo.ca/";

    private static readonly ICountryRules _caRules = CountryRulesFactory.For("CA");

    private readonly HttpClient _http;

    public GeoLocatorApi(HttpClient http) => _http = http;

    public string Name => "GeoLocator";
    public IReadOnlySet<string> SupportedCountries => _countries;
    public int? DailyLimit   => null;
    public int? MonthlyLimit => null;

    public async Task<FetchResult> LookupAsync(
        string country, string code, string? stateAbbr, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}?q={Uri.EscapeDataString(code)}&lang=en&keys=fsa";

        try
        {
            var response = await _http.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (null, FetchOutcome.NotFound);

            // 401/403 (bad/blocked) — drop from rotation for the rest of the run.
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                    or System.Net.HttpStatusCode.Forbidden)
                return (null, FetchOutcome.RateLimited);

            if ((int)response.StatusCode == 429)
                return (null, FetchOutcome.RateLimited);

            if (!response.IsSuccessStatusCode)
                return (null, FetchOutcome.TransientError, $"HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, FetchOutcome.TransientError, $"{ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[GeoLocator] Unexpected error for {code}: {ex.Message}");
            return (null, FetchOutcome.TransientError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
