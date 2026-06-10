using System.Text.Json;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

/// <summary>
/// ZipCodeBase API (https://zipcodebase.com).
/// Supports US, CA, MX. API key required. Monthly call limit (not daily).
/// Returns city, state/province code, timezone, and coordinates.
/// 401/403 (bad/expired key) removes this API from rotation.
/// </summary>
internal sealed class ZipCodeBaseApi : EnrichmentApiBase
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "US", "CA", "MX" };

    private const string BaseUrl = "https://app.zipcodebase.com/api/v1/search";

    private readonly string _apiKey;
    private readonly int?   _monthlyLimit;

    public ZipCodeBaseApi(HttpClient http, string apiKey, int? monthlyLimit = null) : base(http)
    {
        _apiKey       = apiKey;
        _monthlyLimit = monthlyLimit;
    }

    public override string               Name               => "ZipCodeBase";
    public override IReadOnlySet<string> SupportedCountries => _countries;
    public override int?                 MonthlyLimit       => _monthlyLimit;

    protected override string? BuildUrl(string country, string code, string? stateAbbr)
    {
        var cc = country.ToUpperInvariant();
        return $"{BaseUrl}?apikey={_apiKey}&codes={Uri.EscapeDataString(code)}&country={cc}";
    }

    protected override async Task<FetchResult> ParseAsync(
        HttpResponseMessage response, string country, string code, CancellationToken ct)
    {
        var json = await ReadJsonAsync(response, ct);

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

        var (admin1Code, admin1Name) = ResolveAdmin1(stateCode, stateName);

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
}
