using Dapper;
using Microsoft.Data.SqlClient;
using ZipPostLookup.CountryDataTools.Database.Sql;

namespace ZipPostLookup.CountryDataTools.Database.Repositories;

/// <summary>
/// Persists and retrieves API call counts from data.ApiUsage.
/// Daily APIs reset at 00:00 UTC; monthly APIs reset on the 1st of the next calendar month UTC.
/// </summary>
internal static class ApiUsageRepository
{
    /// <summary>
    /// Returns the current-period call count for every tracked API.
    /// Daily APIs return today's count; monthly APIs return the calendar-month total.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, int>> GetCurrentPeriodUsageAsync(
        SqlConnection conn)
    {
        try
        {
            var rows = await conn.QueryAsync<(string ApiName, int CallCount)>(
                CommonQueries.GetCurrentPeriodUsage);
            return rows.ToDictionary(r => r.ApiName, r => r.CallCount,
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"  ⚠  Could not load current period API usage counts: {ex.Message}");
            return new Dictionary<string, int>();
        }
    }

    /// <summary>
    /// Atomically increments the call count for <paramref name="apiName"/> on today's UTC date.
    /// Computes ResetTime based on the limit type: next midnight UTC for daily, first of next
    /// month UTC for monthly. Silent on failure — usage tracking must never break enrichment.
    /// </summary>
    public static async Task RecordCallAsync(
        SqlConnection conn, string apiName, int? dailyLimit, int? monthlyLimit = null)
    {
        try
        {
            var resetTime = ComputeResetTime(monthlyLimit.HasValue);

            await conn.ExecuteAsync(
                CommonQueries.UpsertApiUsage,
                new
                {
                    ApiName      = apiName,
                    DailyLimit   = dailyLimit,
                    MonthlyLimit = monthlyLimit,
                    ResetTime    = resetTime
                });
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"  ⚠  Could not record API usage for {apiName}: {ex.Message}");
        }
    }

    private static DateTimeOffset ComputeResetTime(bool isMonthly)
    {
        var utcNow = DateTimeOffset.UtcNow;
        if (isMonthly)
        {
            // First of next calendar month at 00:00 UTC
            var firstOfThisMonth = new DateTimeOffset(utcNow.Year, utcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
            return firstOfThisMonth.AddMonths(1);
        }
        // Next midnight UTC
        return new DateTimeOffset(utcNow.Date.AddDays(1), TimeSpan.Zero);
    }
}
