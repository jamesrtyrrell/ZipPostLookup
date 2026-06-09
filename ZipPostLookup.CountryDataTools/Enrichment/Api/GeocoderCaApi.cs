using System.Xml.Linq;
using GeoTimeZone;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

/// <summary>
/// Geocoder.ca free XML API (https://geocoder.ca).
/// CA-only. No API key. Free tier: 500–2,000 lookups/day (dynamically throttled by server load).
/// Sends postal={ldu}&amp;geoit=XML; receives lat/lon only — no city or province in response.
/// Timezone is resolved offline from the returned coordinates via GeoTimeZone.
/// PlaceName and Admin1 are left empty; UpdateReferenceAsync handles this by skipping
/// the name update (NameChecked stays false) and only writing timezone + coordinates.
/// Error 006 = server-side throttle → API dropped from rotation for this session.
/// </summary>
internal sealed class GeocoderCaApi : IEnrichmentApi
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "CA" };

    private const string BaseUrl = "https://geocoder.ca/";

    private readonly HttpClient _http;

    public GeocoderCaApi(HttpClient http) => _http = http;

    public string               Name               => "Geocoder.ca";
    public IReadOnlySet<string> SupportedCountries => _countries;
    // Conservative self-imposed cap. Free tier can throttle at ≥500/day; the server
    // signals the actual limit via error 006 → RateLimited drops us from rotation.
    public int? DailyLimit   => 500;
    public int? MonthlyLimit => null;

    public async Task<FetchResult> LookupAsync(
        string country, string code, string? stateAbbr, CancellationToken ct = default)
    {
        // Strip embedded space: "H1L 6P2" → "H1L6P2"
        var postal = code.Replace(" ", "");
        var url    = $"{BaseUrl}?postal={Uri.EscapeDataString(postal)}&geoit=XML";

        try
        {
            var response = await _http.GetAsync(url, ct);

            // 401/403 (bad/blocked) — drop from rotation for the rest of the run.
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                                    or System.Net.HttpStatusCode.Forbidden)
                return (null, FetchOutcome.RateLimited);

            if ((int)response.StatusCode == 429)
                return (null, FetchOutcome.RateLimited);

            if (!response.IsSuccessStatusCode)
                return (null, FetchOutcome.TransientError, $"HTTP {(int)response.StatusCode}");

            var xml  = await response.Content.ReadAsStringAsync(ct);
            var doc  = XDocument.Parse(xml);
            var root = doc.Root; // <geodata>

            if (root == null)
                return (null, FetchOutcome.TransientError, "missing geodata root");

            // Check for API-level error
            var errorCode = root.Element("error")?.Element("code")?.Value?.Trim();
            if (errorCode == "006")
                return (null, FetchOutcome.RateLimited);          // throttled
            if (errorCode is "005" or "007" or "008")
                return (null, FetchOutcome.NotFound);             // bad format / no result
            if (errorCode != null)
                return (null, FetchOutcome.TransientError, "unknown error in response");       // unknown error

            var lattStr  = root.Element("latt")?.Value?.Trim();
            var longtStr = root.Element("longt")?.Value?.Trim();

            if (!double.TryParse(lattStr,  System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(longtStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lon) ||
                (lat == 0 && lon == 0))
                return (null, FetchOutcome.NotFound);

            // Resolve IANA timezone from coordinates; skip Etc/Unknown (open ocean sentinel).
            string? iana = null;
            var tzResult = TimeZoneLookup.GetTimeZone(lat, lon).Result;
            if (!string.IsNullOrWhiteSpace(tzResult) &&
                tzResult.Contains('/') &&
                tzResult != "Etc/Unknown")
                iana = tzResult;

            return (new ApiLookupResult
            {
                // No city or province in the geocoder.ca forward-geocode response.
                // Coordinates and timezone are all this API provides.
                Timezone = iana,
                Lat      = lat,
                Lon      = lon,
            }, FetchOutcome.Found);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            return (null, FetchOutcome.TransientError, $"{ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[Geocoder.ca] Unexpected error for {code}: {ex.Message}");
            return (null, FetchOutcome.TransientError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
