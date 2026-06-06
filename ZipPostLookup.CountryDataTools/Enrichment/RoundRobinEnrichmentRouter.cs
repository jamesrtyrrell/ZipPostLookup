using ZipPostLookup.CountryDataTools.Enrichment.Api;

namespace ZipPostLookup.CountryDataTools.Enrichment;

internal sealed class RoundRobinEnrichmentRouter
{
    private readonly List<IEnrichmentApi> _active;
    private readonly Dictionary<IEnrichmentApi, int> _callCounts = [];
    private readonly List<(string ApiName, FetchOutcome Outcome)> _lastCallLog = [];
    private int _index;

    public RoundRobinEnrichmentRouter(IReadOnlyList<IEnrichmentApi> apis)
    {
        _active = [..apis];
    }

    public bool AnyAvailable => _active.Count > 0;

    /// <summary>
    /// Per-API outcomes for the most recent LookupAsync call.
    /// Contains one entry per API that was actually called (RateLimited calls excluded).
    /// Cleared at the start of each LookupAsync. Use this to record usage and transient
    /// errors for every API tried, not just the one that ultimately succeeded.
    /// </summary>
    public IReadOnlyList<(string ApiName, FetchOutcome Outcome)> LastCallLog => _lastCallLog;

    /// <summary>
    /// Pre-populates the call counter for an API from a persisted source (e.g. data.ApiUsage).
    /// Call before the first LookupAsync so daily-limit checks account for calls made in
    /// earlier sessions today.
    /// </summary>
    public void SeedCallCount(string apiName, int count)
    {
        var api = _active.FirstOrDefault(
            a => a.Name.Equals(apiName, StringComparison.OrdinalIgnoreCase));
        if (api != null)
            _callCounts[api] = count;
    }

    /// <summary>
    /// Returns the number of calls made to each API this session, keyed by API name.
    /// Includes APIs that have since been removed from rotation.
    /// </summary>
    public IReadOnlyDictionary<string, int> CallCounts =>
        _callCounts.ToDictionary(kv => kv.Key.Name, kv => kv.Value);

    public async Task<(ApiLookupResult? Result, string? ApiName, FetchOutcome Outcome)> LookupAsync(
        string country, string code, string? stateAbbr = null, CancellationToken ct = default)
    {
        _lastCallLog.Clear();
        var triedThisCode = new HashSet<IEnrichmentApi>();

        while (_active.Count > 0)
        {
            var api = _active[_index % _active.Count];

            // Drop from rotation if this API has hit its declared per-period limit (daily or monthly).
            var effectiveLimit = api.DailyLimit ?? api.MonthlyLimit;
            if (effectiveLimit.HasValue &&
                _callCounts.GetValueOrDefault(api) >= effectiveLimit.Value)
            {
                _active.Remove(api);
                if (_active.Count > 0) _index %= _active.Count;
                continue;
            }

            // Don't retry an API that already had a transient error for this code.
            // If every remaining active API has been tried, give up.
            if (triedThisCode.Contains(api))
            {
                if (_active.All(a => triedThisCode.Contains(a)))
                    break;
                _index = (_index + 1) % _active.Count;
                continue;
            }

            _callCounts[api] = _callCounts.GetValueOrDefault(api) + 1;
            triedThisCode.Add(api);
            var (result, outcome) = await api.LookupAsync(country, code, stateAbbr, ct);

            if (outcome == FetchOutcome.RateLimited)
            {
                // RateLimited: remove from rotation and try next. Not added to LastCallLog
                // because the original behaviour was to not record rate-limit calls in usage.
                _active.Remove(api);
                if (_active.Count > 0) _index %= _active.Count;
                continue;
            }

            _lastCallLog.Add((api.Name, outcome));
            _index = (_index + 1) % _active.Count;

            if (outcome == FetchOutcome.TransientError)
                continue;   // temporary failure — try next API for this code

            return (result, api.Name, outcome);
        }

        return (null, null, FetchOutcome.TransientError);
    }
}
