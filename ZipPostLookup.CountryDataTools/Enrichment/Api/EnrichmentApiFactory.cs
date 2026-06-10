using ZipPostLookup.CountryDataTools.Database.WorkDb;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

internal static class EnrichmentApiFactory
{
    // Key-less APIs, in rotation-priority order.
    private static readonly Func<HttpClient, IEnrichmentApi>[] _freeApis =
    [
        http => new ZippopotamusApi(http),
        http => new GeoLocatorApi(http),
        http => new GeocoderCaApi(http),
        http => new ZiptasticApi(http),
    ];

    // Key-based APIs: (apikeys.json entry name, factory). Ordered after the free APIs.
    private static readonly (string ConfigKey, Func<HttpClient, ApiKeyEntry, IEnrichmentApi> Create)[] _keyedApis =
    [
        ("geoApify",        (http, e) => new GeoApifyApi(http, e.Key, e.DailyLimit)),
        ("geocodio",        (http, e) => new GeocodioApi(http, e.Key, e.DailyLimit)),
        ("abstractApi",     (http, e) => new AbstractTimezoneApi(http, e.Key, e.DailyLimit)),
        ("openCageDataApi", (http, e) => new OpenCageApi(http, e.Key, e.DailyLimit)),
        ("zipCodeBase",     (http, e) => new ZipCodeBaseApi(http, e.Key, e.MonthlyLimit)),
    ];

    public static IReadOnlyList<IEnrichmentApi> GetApisForCountry(
        string country, HttpClient http, ApiKeysConfig? apiKeys = null)
    {
        var result = new List<IEnrichmentApi>();

        foreach (var make in _freeApis)
        {
            var api = make(http);
            if (api.SupportedCountries.Contains(country))
                result.Add(api);
        }

        foreach (var (configKey, create) in _keyedApis)
        {
            var entry = apiKeys?.TryGetEntry(configKey);
            if (entry is null)
                continue;

            if (!entry.IsConfigured)
            {
                Console.Error.WriteLine(
                    $"  ⚠  apikeys.json: {configKey} key is not configured — replace the placeholder value.");
                continue;
            }

            var api = create(http, entry);
            if (api.SupportedCountries.Contains(country))
                result.Add(api);
        }

        return result;
    }
}
