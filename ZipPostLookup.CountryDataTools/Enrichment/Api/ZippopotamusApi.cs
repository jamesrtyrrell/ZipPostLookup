using System.Net;
using System.Text.Json;
using GeoTimeZone;
using ZipPostLookup.CountryDataTools.Validation;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

internal sealed class ZippopotamusApi : EnrichmentApiBase
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "US", "CA", "MX" };

    public ZippopotamusApi(HttpClient http) : base(http) { }

    public override string Name => "Zippopotam.us";
    public override IReadOnlySet<string> SupportedCountries => _countries;

    protected override string? BuildUrl(string country, string code, string? stateAbbr)
    {
        var cc = CountryRulesFactory.For(country).GetZippopotamusCountryCode(country, stateAbbr);
        return $"https://api.zippopotam.us/{cc}/{Uri.EscapeDataString(code)}";
    }

    protected override FetchResult? MapStatus(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.NotFound
            ? new FetchResult(null, FetchOutcome.NotFound)
            : null;

    protected override async Task<FetchResult> ParseAsync(
        HttpResponseMessage response, string country, string code, CancellationToken ct)
    {
        var json = await ReadJsonAsync(response, ct);

        if (!json.TryGetProperty("places", out var places) || places.GetArrayLength() == 0)
            return (null, FetchOutcome.TransientError, "empty or invalid response body");

        var place = places[0];

        var rawCity      = place.TryGetProperty("place name",        out var cn) ? cn.GetString() ?? "" : "";
        var rawState     = place.TryGetProperty("state abbreviation", out var sa) ? sa.GetString() ?? "" : "";
        var rawStateName = place.TryGetProperty("state",              out var sn) ? sn.GetString() ?? "" : "";

        double lat = 0, lon = 0;
        if (place.TryGetProperty("latitude", out var latEl))
            double.TryParse(latEl.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out lat);
        if (place.TryGetProperty("longitude", out var lonEl))
            double.TryParse(lonEl.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out lon);

        var iana = (lat != 0 || lon != 0) ? TimeZoneLookup.GetTimeZone(lat, lon).Result : "";

        if (string.IsNullOrWhiteSpace(iana) || !iana.Contains('/'))
            return (null, FetchOutcome.TransientError, "missing or invalid timezone in response");

        var (admin1Code, admin1Name) = ResolveAdmin1(rawState, rawStateName);

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
