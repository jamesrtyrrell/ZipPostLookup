using ZipPostLookup.Core;
using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Ingestion.Models;

namespace ZipPostLookup.CountryDataTools.Ingestion.AutoImport;

/// <summary>
/// Phase 2: Oracle-based postal code column detection.
/// Dynamically probes all available countries via CountryRegistry.
/// </summary>
public class OracleProbeService
{
    private readonly Dictionary<string, IZipPostLookup> _lookups;

    public OracleProbeService()
    {
        // Dynamic: load all available countries from embedded resources
        var countries = CountryRegistry.GetAvailableCountries();
        _lookups = countries.ToDictionary(
            cc => (string)cc,
            cc => CountryRegistry.GetLookup(cc)
        );

        if (_lookups.Count == 0)
        {
            throw new InvalidOperationException(
                "No countries available for oracle probing. " +
                "Ensure the ZipPostLookup library has embedded .zpi.br resources.");
        }
    }

    /// <summary>
    /// Probe all columns to identify the postal code column.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="sniff">File sniff result from Phase 1.</param>
    /// <param name="sampleRows">Number of rows to sample (default: 200).</param>
    /// <param name="minHitRate">Minimum hit rate threshold (default: 0.70).</param>
    /// <returns>Probe result with postal code column and country.</returns>
    public ProbeResult Probe(
        string filePath,
        FileSniffResult sniff,
        int sampleRows = 200,
        double minHitRate = 0.70)
    {
        var rows = ReadSampleRows(filePath, sniff, sampleRows);
        var columnCount = sniff.ColumnCount;

        if (rows.Length == 0)
        {
            throw new InvalidOperationException("No rows available for probing (file empty or all blank).");
        }

        // Per-column accumulators
        var hitCounts = new int[columnCount];
        var nonEmptyCounts = new int[columnCount];
        var countryTallies = new Dictionary<int, CountryTally>();
        var hits = new List<OracleHit>();
        var allSeenCodes = new HashSet<string>();
        var missedCodes = new HashSet<string>();

        // Probe each cell
        for (int rowIdx = 0; rowIdx < rows.Length; rowIdx++)
        {
            var cells = rows[rowIdx];
            for (int colIdx = 0; colIdx < cells.Length && colIdx < columnCount; colIdx++)
            {
                var value = cells[colIdx]?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                nonEmptyCounts[colIdx]++;
                allSeenCodes.Add(value);

                // Try all available countries (early exit on first hit)
                CodeEntry? winner = null;
                string? country = null;

                foreach (var (cc, lookup) in _lookups)
                {
                    var hit = lookup.GetByCode(value);
                    if (hit != null)
                    {
                        winner = hit;
                        country = cc;
                        break; // First hit wins
                    }
                }

                if (winner != null && country != null)
                {
                    hitCounts[colIdx]++;
                    hits.Add(new OracleHit
                    {
                        RowIndex = rowIdx,
                        ColumnIndex = colIdx,
                        InputValue = value,
                        Country = country,
                        Entry = winner
                    });

                    if (!countryTallies.ContainsKey(colIdx))
                        countryTallies[colIdx] = new CountryTally();
                    countryTallies[colIdx].Increment(country);
                }
            }
        }

        // Calculate hit rates per column
        var hitRates = new Dictionary<int, double>();
        for (int i = 0; i < columnCount; i++)
        {
            hitRates[i] = nonEmptyCounts[i] > 0
                ? hitCounts[i] / (double)nonEmptyCounts[i]
                : 0.0;
        }

        // Find winner (max hit rate)
        var sorted = hitRates.OrderByDescending(kv => kv.Value).ToList();
        if (sorted.Count == 0 || sorted[0].Value < minHitRate)
        {
            throw new InvalidOperationException(
                $"No column achieved minimum hit rate {minHitRate:P0}. " +
                $"Best: column {(sorted.Count > 0 ? sorted[0].Key : -1)} at {(sorted.Count > 0 ? sorted[0].Value : 0):P0}. " +
                $"Try --min-hit-rate {(sorted.Count > 0 ? Math.Floor(sorted[0].Value * 100) / 100 : 0.5)} or ensure file contains postal codes.");
        }

        var winnerCol = sorted[0].Key;
        var winnerRate = sorted[0].Value;

        // Ambiguity check: second-best within 10%?
        var isAmbiguous = sorted.Count > 1 && (sorted[0].Value - sorted[1].Value) < 0.10;

        // Dominant country
        if (!countryTallies.ContainsKey(winnerCol))
        {
            throw new InvalidOperationException($"Winner column {winnerCol} has no country tally (internal error).");
        }
        var dominantCountry = countryTallies[winnerCol].GetWinner();

        // Track missed codes (codes that appeared in the winner column but didn't hit)
        var winnerColumnHits = hits.Where(h => h.ColumnIndex == winnerCol).Select(h => h.InputValue).ToHashSet();
        foreach (var row in rows)
        {
            if (winnerCol < row.Length)
            {
                var code = row[winnerCol]?.Trim();
                if (!string.IsNullOrWhiteSpace(code) && !winnerColumnHits.Contains(code))
                {
                    missedCodes.Add(code);
                }
            }
        }

        return new ProbeResult
        {
            PostalCodeColumnIndex = winnerCol,
            DominantCountry = dominantCountry,
            ColumnHitRates = hitRates,
            ColumnCountryTallies = countryTallies,
            SampleHits = hits,
            MissedCodes = missedCodes.ToList(),
            IsAmbiguous = isAmbiguous
        };
    }

    /// <summary>
    /// Read sample rows from file (skip header if present).
    /// </summary>
    private string[][] ReadSampleRows(string filePath, FileSniffResult sniff, int sampleRows)
    {
        var allRows = DelimitedFile.ReadRows(filePath, sniff.Delimiter, maxRows: sampleRows + (sniff.HasHeaderRow ? 1 : 0));

        // Skip header if present
        if (sniff.HasHeaderRow && allRows.Count > 0)
        {
            allRows.RemoveAt(0);
        }

        return allRows.ToArray();
    }
}
