using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml;
using ZipPostLookup.CountryDataTools.Pipeline;

namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

/// <summary>
/// Template-method base for <see cref="IEnrichmentApi"/> implementations. Owns the shared
/// HTTP call, the universal status-code → <see cref="FetchOutcome"/> mapping
/// (401/403/429 → RateLimited, other non-2xx → TransientError), and the two-tier exception
/// handling. Each concrete API supplies only its URL, body parsing, and — via
/// <see cref="MapStatus"/> — any provider-specific status codes (404, 204, 402, 422, …).
/// </summary>
internal abstract class EnrichmentApiBase : IEnrichmentApi
{
    protected HttpClient Http { get; }

    protected EnrichmentApiBase(HttpClient http) => Http = http;

    public abstract string Name { get; }
    public abstract IReadOnlySet<string> SupportedCountries { get; }
    public virtual int? DailyLimit   => null;
    public virtual int? MonthlyLimit => null;

    /// <summary>
    /// Builds the request URL, or returns null to short-circuit the call as
    /// <see cref="FetchOutcome.NotFound"/> (e.g. when the country has no provider mapping).
    /// </summary>
    protected abstract string? BuildUrl(string country, string code, string? stateAbbr);

    /// <summary>Parses a successful (2xx, post-status-mapping) response into a result.</summary>
    protected abstract Task<FetchResult> ParseAsync(
        HttpResponseMessage response, string country, string code, CancellationToken ct);

    /// <summary>
    /// Optional hook to map provider-specific status codes before the universal mapping
    /// (e.g. 404 → NotFound, 204 → NotFound, 402/422 → RateLimited). Return null to fall through.
    /// </summary>
    protected virtual FetchResult? MapStatus(HttpResponseMessage response) => null;

    public async Task<FetchResult> LookupAsync(
        string country, string code, string? stateAbbr, CancellationToken ct = default)
    {
        var url = BuildUrl(country, code, stateAbbr);
        if (url is null)
            return (null, FetchOutcome.NotFound);

        try
        {
            var response = await Http.GetAsync(url, ct);

            if (MapStatus(response) is { } mapped)
                return mapped;

            // 401/403 (bad/blocked key) — drop from rotation for the rest of the run.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return (null, FetchOutcome.RateLimited);

            if ((int)response.StatusCode == 429)
                return (null, FetchOutcome.RateLimited);

            if (!response.IsSuccessStatusCode)
                return (null, FetchOutcome.TransientError, $"HTTP {(int)response.StatusCode}");

            return await ParseAsync(response, country, code, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or XmlException)
        {
            return (null, FetchOutcome.TransientError, $"{ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[{Name}] Unexpected error for {code}: {ex.Message}");
            return (null, FetchOutcome.TransientError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // ── Shared helpers ──────────────────────────────────────────────────────────

    /// <summary>Reads the response body as a JSON document root.</summary>
    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
        => await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

    /// <summary>Title-cases a space-separated string ("NEW YORK" → "New York").</summary>
    protected static string TitleCase(string input)
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

    /// <summary>
    /// Resolves a canonical (Admin1Code, Admin1Name) from raw state values via
    /// <see cref="StateResolver"/>. <paramref name="rawCode"/> is the state code/abbreviation,
    /// <paramref name="rawName"/> the full state name (pass the same value for both when the API
    /// returns only one). Falls back to the raw inputs when the resolver finds no match —
    /// reproducing each provider's prior fallback exactly.
    /// </summary>
    protected static (string Code, string Name) ResolveAdmin1(string rawCode, string rawName)
    {
        var match = StateResolver.Resolve(rawCode) ?? StateResolver.Resolve(rawName);
        var code  = match?.StateCode ?? rawCode.ToUpperInvariant();
        var name  = match?.StateName ?? rawName;
        return (code, name);
    }
}
