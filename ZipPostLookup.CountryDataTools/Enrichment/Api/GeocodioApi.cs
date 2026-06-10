using System.Net;
using System.Text.Json;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

/// <summary>
/// Geocodio geocoding API used as a postal-code lookup
/// (https://www.geocod.io/docs/#geocoding).
/// Supports US, CA, MX. Returns timezone via the fields=timezone append.
/// A 401/403 response removes this API from the session rotation.
/// </summary>
internal sealed class GeocodioApi : EnrichmentApiBase
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

    private readonly string _apiKey;
    private readonly int?   _dailyLimit;

    public GeocodioApi(HttpClient http, string apiKey, int? dailyLimit = null) : base(http)
    {
        _apiKey     = apiKey;
        _dailyLimit = dailyLimit;
    }

    public override string               Name               => "Geocodio";
    public override IReadOnlySet<string> SupportedCountries => _countries;
    public override int?                 DailyLimit         => _dailyLimit;

    protected override string? BuildUrl(string country, string code, string? stateAbbr)
    {
        if (!_countryParam.TryGetValue(country, out var countryParam))
            return null;

        return $"{BaseUrl}?q={Uri.EscapeDataString(code)}" +
               $"&country={countryParam}&fields=timezone&api_key={_apiKey}";
    }

    // 422 = query could not be parsed — treat as not found.
    protected override FetchResult? MapStatus(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.UnprocessableEntity
            ? new FetchResult(null, FetchOutcome.NotFound)
            : null;

    protected override async Task<FetchResult> ParseAsync(
        HttpResponseMessage response, string country, string code, CancellationToken ct)
    {
        var json = await ReadJsonAsync(response, ct);

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
            return (null, FetchOutcome.TransientError, "empty city and state");

        var (admin1Code, admin1Name) = ResolveAdmin1(rawStateCode, rawStateCode);

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
}
