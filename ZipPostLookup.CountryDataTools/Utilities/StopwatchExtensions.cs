using System.Diagnostics;

namespace ZipPostLookup.CountryDataTools.Utilities;

public static class StopwatchExtensions
{
    /// <summary>
    ///     Returns a human-readable elapsed time string scaled to the duration.
    ///     Examples: "10m 10s", "53s", "103ms"
    /// </summary>
    public static string ZipPostLookupTaskElapsedTime(this Stopwatch stopwatch)
    {
        var ts = stopwatch.Elapsed;

        if (ts.TotalMinutes >= 1)
        {
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        }

        if (ts.TotalSeconds >= 1)
        {
            return $"{(int)ts.TotalSeconds}s";
        }

        return $"{ts.Milliseconds}ms";
    }
}