using System.Net.Http.Json;
using System.Text.Json;
using ZipPostLookup.CountryDataTools.Pipeline;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

internal sealed class ZiptasticApi : IEnrichmentApi
{
    private static readonly HashSet<string> _countries =
        new(StringComparer.OrdinalIgnoreCase) { "US" };

    private readonly HttpClient _http;

    public ZiptasticApi(HttpClient http) => _http = http;

    public string Name => "Ziptastic";
    public IReadOnlySet<string> SupportedCountries => _countries;
    public int? DailyLimit   => null;
    public int? MonthlyLimit => null;

    public async Task<(ApiLookupResult? Result, FetchOutcome Outcome)> LookupAsync(
        string country, string code, string? stateAbbr, CancellationToken ct = default)
    {
        var url = $"https://ziptasticapi.com/{Uri.EscapeDataString(code)}";

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

            // Error response: { "error_code": "...", "error_message": "..." }
            if (json.TryGetProperty("error_code", out _))
                return (null, FetchOutcome.NotFound);

            var rawCity  = json.TryGetProperty("city",  out var c) ? c.GetString() ?? "" : "";
            var rawState = json.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(rawCity) && string.IsNullOrWhiteSpace(rawState))
                return (null, FetchOutcome.TransientError);

            var stateMatch = StateResolver.Resolve(rawState);
            var admin1Code = stateMatch?.StateCode ?? rawState.ToUpperInvariant();
            var admin1Name = stateMatch?.StateName ?? rawState;

            return (new ApiLookupResult
            {
                PlaceName  = TitleCase(rawCity),
                Admin1Code = admin1Code,
                Admin1Name = admin1Name,
                Timezone   = null,   // Ziptastic does not return coordinates or timezone
            }, FetchOutcome.Found);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, FetchOutcome.TransientError);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[Ziptastic] Unexpected error for {code}: {ex.Message}");
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
