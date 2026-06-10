namespace ZipPostLookup.CountryDataTools.Models.Counters;

public sealed class EnrichCandidateCounters
{
    public Dictionary<string, int> ResolvedByApi { get; } = new();
    public int Unfound          { get; set; }
    public int Skipped          { get; set; }
    public int SpecialCodes     { get; set; }
    public int NewNamesInserted { get; set; }

    public int TotalResolved => ResolvedByApi.Values.Sum();

    /// <summary>
    /// Running today total per API name (cross-session, from data.ApiUsage).
    /// Today is the cumulative call count for the UTC day; Limit is the configured cap (null = unlimited).
    /// </summary>
    public Dictionary<string, (int Today, int? Limit)> DailyUsage { get; } = new();

    public Dictionary<string, int> TransientByApi { get; } = new();

    public void IncrementResolved(string apiName)
    {
        ResolvedByApi.TryGetValue(apiName, out var n);
        ResolvedByApi[apiName] = n + 1;
    }

    public void IncrementTransient(string? apiName)
    {
        if (apiName == null) return;
        TransientByApi.TryGetValue(apiName, out var n);
        TransientByApi[apiName] = n + 1;
    }

    public void InitDailyUsage(string apiName, int todayTotal, int? limit) =>
        DailyUsage[apiName] = (todayTotal, limit);

    public void IncrementDailyUsage(string apiName)
    {
        DailyUsage.TryGetValue(apiName, out var current);
        DailyUsage[apiName] = (current.Today + 1, current.Limit);
    }
}
