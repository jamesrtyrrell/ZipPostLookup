using ZipPostLookup.CountryDataTools.Database.WorkDb;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

internal static class EnrichmentApiFactory
{
    public static IReadOnlyList<IEnrichmentApi> GetApisForCountry(
        string country, HttpClient http, ApiKeysConfig? apiKeys = null)
    {
        var result = new List<IEnrichmentApi>();

        var zippopotam = new ZippopotamusApi(http);
        if (zippopotam.SupportedCountries.Contains(country))
            result.Add(zippopotam);

        var geoLocator = new GeoLocatorApi(http);
        if (geoLocator.SupportedCountries.Contains(country))
            result.Add(geoLocator);

        var geocoderCa = new GeocoderCaApi(http);
        if (geocoderCa.SupportedCountries.Contains(country))
            result.Add(geocoderCa);

        var ziptastic = new ZiptasticApi(http);
        if (ziptastic.SupportedCountries.Contains(country))
            result.Add(ziptastic);

        var geoEntry = apiKeys?.TryGetEntry("geoApify");
        if (geoEntry != null)
        {
            if (!geoEntry.IsConfigured)
                Console.Error.WriteLine(
                    "  ⚠  apikeys.json: geoApify key is not configured — replace the placeholder value.");
            else
            {
                var geoApify = new GeoApifyApi(http, geoEntry.Key, geoEntry.DailyLimit);
                if (geoApify.SupportedCountries.Contains(country))
                    result.Add(geoApify);
            }
        }

        var geocodioEntry = apiKeys?.TryGetEntry("geocodio");
        if (geocodioEntry != null)
        {
            if (!geocodioEntry.IsConfigured)
                Console.Error.WriteLine(
                    "  ⚠  apikeys.json: geocodio key is not configured — replace the placeholder value.");
            else
            {
                var geocodio = new GeocodioApi(http, geocodioEntry.Key, geocodioEntry.DailyLimit);
                if (geocodio.SupportedCountries.Contains(country))
                    result.Add(geocodio);
            }
        }

        var abstractApiEntry = apiKeys?.TryGetEntry("abstractApi");
        if (abstractApiEntry != null)
        {
            if (!abstractApiEntry.IsConfigured)
                Console.Error.WriteLine(
                    "  ⚠  apikeys.json: abstractApi key is not configured — replace the placeholder value.");
            else
            {
                var abstractApi = new AbstractApi(http, abstractApiEntry.Key, abstractApiEntry.DailyLimit);
                if (abstractApi.SupportedCountries.Contains(country))
                    result.Add(abstractApi);
            }
        }

        var openCageEntry = apiKeys?.TryGetEntry("openCageDataApi");
        if (openCageEntry != null)
        {
            if (!openCageEntry.IsConfigured)
                Console.Error.WriteLine(
                    "  ⚠  apikeys.json: openCageDataApi key is not configured — replace the placeholder value.");
            else
            {
                var openCage = new OpenCageApi(http, openCageEntry.Key, openCageEntry.DailyLimit);
                if (openCage.SupportedCountries.Contains(country))
                    result.Add(openCage);
            }
        }

        var zipCodeBaseEntry = apiKeys?.TryGetEntry("zipCodeBase");
        if (zipCodeBaseEntry != null)
        {
            if (!zipCodeBaseEntry.IsConfigured)
                Console.Error.WriteLine(
                    "  ⚠  apikeys.json: zipCodeBase key is not configured — replace the placeholder value.");
            else
            {
                var zipCodeBase = new ZipCodeBaseApi(http, zipCodeBaseEntry.Key, zipCodeBaseEntry.MonthlyLimit);
                if (zipCodeBase.SupportedCountries.Contains(country))
                    result.Add(zipCodeBase);
            }
        }

        return result;
    }
}
