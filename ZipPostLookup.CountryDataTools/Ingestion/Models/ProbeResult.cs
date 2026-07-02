using ZipPostLookup.Core;
using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Ingestion.Models;

/// <summary>
/// Oracle probe results from Phase 2 (postal code column detection).
/// </summary>
public class ProbeResult
{
    /// <summary>
    /// Index of the column identified as the postal code column.
    /// </summary>
    public int PostalCodeColumnIndex { get; set; }

    /// <summary>
    /// Dominant country detected from oracle hits (e.g., "US", "CA", "MX").
    /// </summary>
    public string DominantCountry { get; set; } = string.Empty;

    /// <summary>
    /// Hit rates per column (column index → percentage 0.0–1.0).
    /// </summary>
    public Dictionary<int, double> ColumnHitRates { get; set; } = new();

    /// <summary>
    /// Country tallies per column (column index → tally).
    /// </summary>
    public Dictionary<int, CountryTally> ColumnCountryTallies { get; set; } = new();

    /// <summary>
    /// Sample oracle hits for correlation phase.
    /// </summary>
    public List<OracleHit> SampleHits { get; set; } = new();

    /// <summary>
    /// Postal codes that were NOT found in the built-in registries (oracle misses).
    /// </summary>
    public List<string> MissedCodes { get; set; } = new();

    /// <summary>
    /// Whether two or more columns have hit rates within 10% (ambiguous).
    /// </summary>
    public bool IsAmbiguous { get; set; }
}

/// <summary>
/// A single oracle hit result.
/// </summary>
public class OracleHit
{
    /// <summary>
    /// Row index in the sample.
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// Column index in the sample.
    /// </summary>
    public int ColumnIndex { get; set; }

    /// <summary>
    /// Input value that was looked up.
    /// </summary>
    public string InputValue { get; set; } = string.Empty;

    /// <summary>
    /// Country that matched the code.
    /// </summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>
    /// The CodeEntry returned by the oracle.
    /// </summary>
    public CodeEntry Entry { get; set; } = null!;
}

/// <summary>
/// Country tally tracker for per-column country distribution.
/// </summary>
public class CountryTally
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

    public void Increment(string countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
            return;

        var key = countryCode.ToUpperInvariant();
        _counts[key] = _counts.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    public string GetWinner()
    {
        if (_counts.Count == 0)
            return "US"; // Default fallback

        return _counts.OrderByDescending(kv => kv.Value).First().Key;
    }

    public int Total => _counts.Values.Sum();

    public int GetCount(string countryCode)
    {
        return _counts.TryGetValue(countryCode.ToUpperInvariant(), out var count) ? count : 0;
    }
}
