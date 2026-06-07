using System.Net.Http.Json;
using System.Text.Json;
using GeoTimeZone;
using ZipPostLookup.CountryDataTools.Pipeline;
using ZipPostLookup.CountryDataTools.Validation;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

internal sealed class ZippopotamusApi : IEnrichmentApi
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "US", "CA", "MX" };

    private readonly HttpClient _http;

    public ZippopotamusApi(HttpClient http) => _http = http;

    public string Name => "Zippopotam.us";
    public IReadOnlySet<string> SupportedCountries => _countries;
    public int? DailyLimit   => null;
    public int? MonthlyLimit => null;

    public async Task<(ApiLookupResult? Result, FetchOutcome Outcome)> LookupAsync(
        string country, string code, string? stateAbbr, CancellationToken ct = default)
    {
        var cc = CountryRulesFactory.For(country).GetZippopotamusCountryCode(country, stateAbbr);
        var url = $"https://api.zippopotam.us/{cc}/{Uri.EscapeDataString(code)}";

        try
        {
            var response = await _http.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (null, FetchOutcome.NotFound);

            if ((int)response.StatusCode == 429)
                return (null, FetchOutcome.RateLimited);

            if (!response.IsSuccessStatusCode)
                return (null, FetchOutcome.TransientError);

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            if (!json.TryGetProperty("places", out var places) || places.GetArrayLength() == 0)
                return (null, FetchOutcome.TransientError);

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
                return (null, FetchOutcome.TransientError);

            var stateMatch  = StateResolver.Resolve(rawState) ?? StateResolver.Resolve(rawStateName);
            var admin1Code  = stateMatch?.StateCode ?? rawState.ToUpperInvariant();
            var admin1Name  = stateMatch?.StateName ?? rawStateName;

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
            await Console.Error.WriteLineAsync($"[Zippopotam.us] Unexpected error for {code}: {ex.Message}");
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
