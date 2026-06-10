using System.Net;
using System.Text.Json;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

internal sealed class ZiptasticApi : EnrichmentApiBase
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "US" };

    public ZiptasticApi(HttpClient http) : base(http) { }

    public override string Name => "Ziptastic";
    public override IReadOnlySet<string> SupportedCountries => _countries;

    protected override string? BuildUrl(string country, string code, string? stateAbbr) =>
        $"https://ziptasticapi.com/{Uri.EscapeDataString(code)}";

    protected override FetchResult? MapStatus(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.NotFound
            ? new FetchResult(null, FetchOutcome.NotFound)
            : null;

    protected override async Task<FetchResult> ParseAsync(
        HttpResponseMessage response, string country, string code, CancellationToken ct)
    {
        var json = await ReadJsonAsync(response, ct);

        // Error response: { "error_code": "...", "error_message": "..." }
        if (json.TryGetProperty("error_code", out _))
            return (null, FetchOutcome.NotFound);

        var rawCity  = json.TryGetProperty("city",  out var c) ? c.GetString() ?? "" : "";
        var rawState = json.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(rawCity) && string.IsNullOrWhiteSpace(rawState))
            return (null, FetchOutcome.TransientError, "empty city and state in response body");

        var (admin1Code, admin1Name) = ResolveAdmin1(rawState, rawState);

        return (new ApiLookupResult
        {
            PlaceName  = TitleCase(rawCity),
            Admin1Code = admin1Code,
            Admin1Name = admin1Name,
            Timezone   = null,   // Ziptastic does not return coordinates or timezone
        }, FetchOutcome.Found);
    }
}
