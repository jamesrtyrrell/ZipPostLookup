using ZipPostLookup.CountryDataTools.Ingestion.Models;

namespace ZipPostLookup.CountryDataTools.Ingestion.AutoImport;

/// <summary>
/// Phase 3: Column-to-field correlation via oracle entry comparison.
/// Uses Levenshtein similarity and type inference.
/// </summary>
public class ColumnCorrelationService
{
    /// <summary>
    /// Correlate columns to fields using oracle hit results.
    /// </summary>
    /// <param name="sniff">File sniff result.</param>
    /// <param name="probe">Oracle probe result.</param>
    /// <param name="sampleRows">Sample rows for analysis.</param>
    /// <returns>Mapping proposal with confidence scores.</returns>
    public MappingProposal Correlate(
        FileSniffResult sniff,
        ProbeResult probe,
        string[][] sampleRows)
    {
        var postalCol = probe.PostalCodeColumnIndex;
        var columnCount = sniff.ColumnCount;

        // Initialize score matrix [colIdx][fieldName] → list of similarity scores
        var scores = InitializeScoreMatrix(columnCount);

        // Accumulate similarity scores per oracle hit
        foreach (var hit in probe.SampleHits)
        {
            if (hit.RowIndex >= sampleRows.Length)
                continue;

            var row = sampleRows[hit.RowIndex];
            var oracle = hit.Entry;

            for (int colIdx = 0; colIdx < row.Length && colIdx < columnCount; colIdx++)
            {
                if (colIdx == postalCol)
                    continue; // Skip postal code column

                var cellValue = row[colIdx]?.Trim();
                if (string.IsNullOrWhiteSpace(cellValue))
                    continue;

                // PlaceName similarity
                var placeNameSim = LevenshteinSimilarity(cellValue, oracle.PlaceName ?? string.Empty);
                scores[colIdx]["PlaceName"].Add(placeNameSim);

                // Admin1 similarity (name and code)
                var admin1NameSim = LevenshteinSimilarity(cellValue, oracle.Admin1 ?? string.Empty);
                var admin1CodeSim = LevenshteinSimilarity(cellValue, oracle.Admin1Code ?? string.Empty);
                scores[colIdx]["Admin1"].Add(admin1NameSim);
                scores[colIdx]["Admin1Code"].Add(admin1CodeSim);

                // Timezone exact match (IANA format hint: "America/New_York")
                if (cellValue.Contains("/"))
                {
                    if (cellValue.Equals(oracle.Timezone, StringComparison.OrdinalIgnoreCase))
                        scores[colIdx]["Timezone"].Add(1.0);
                    else
                        scores[colIdx]["Timezone"].Add(0.5); // IANA-like but wrong
                }

                // Coordinate type inference (basic range check only - no oracle comparison)
                // CodeEntry doesn't expose coordinates, so we just detect numeric columns
                if (double.TryParse(cellValue, out var numValue))
                {
                    // Latitude range: -90 to 90
                    if (numValue >= -90 && numValue <= 90)
                    {
                        scores[colIdx]["Latitude"].Add(0.7); // Heuristic: looks like a latitude
                    }
                    // Longitude range: -180 to 180
                    if (numValue >= -180 && numValue <= 180)
                    {
                        scores[colIdx]["Longitude"].Add(0.7); // Heuristic: looks like a longitude
                    }
                }
            }
        }

        // Compute confidence per (column, field): coverage × avg_similarity
        var fieldCandidates = new Dictionary<string, List<ColumnCandidate>>();
        var fields = new[] { "PlaceName", "Admin1", "Admin1Code", "Timezone", "Latitude", "Longitude" };

        foreach (var field in fields)
        {
            fieldCandidates[field] = new List<ColumnCandidate>();
            for (int col = 0; col < columnCount; col++)
            {
                if (col == postalCol)
                    continue;

                var scoreList = scores[col][field];
                if (scoreList.Count == 0)
                    continue;

                var avgSim = scoreList.Average();
                var coverage = scoreList.Count / (double)probe.SampleHits.Count;
                var confidence = coverage * avgSim;

                if (confidence > 0.3) // Threshold for consideration
                {
                    fieldCandidates[field].Add(new ColumnCandidate
                    {
                        ColumnIndex = col,
                        FieldName = field,
                        Confidence = confidence,
                        Reasoning = $"{coverage:P0} coverage × {avgSim:P0} similarity"
                    });
                }
            }
        }

        // Greedy assignment: highest-confidence unmapped pairing first
        var mappings = new List<FieldMapping>();
        var assignedColumns = new HashSet<int> { postalCol };

        var allCandidates = fieldCandidates.Values
            .SelectMany(c => c)
            .OrderByDescending(c => c.Confidence)
            .ToList();

        foreach (var candidate in allCandidates)
        {
            if (assignedColumns.Contains(candidate.ColumnIndex))
                continue;
            if (mappings.Any(m => m.FieldName == candidate.FieldName))
                continue; // Field already mapped

            mappings.Add(new FieldMapping
            {
                FieldName = candidate.FieldName,
                ColumnIndex = candidate.ColumnIndex,
                Confidence = candidate.Confidence,
                Reasoning = candidate.Reasoning
            });
            assignedColumns.Add(candidate.ColumnIndex);
        }

        // Add postal code mapping (confidence = probe hit rate)
        mappings.Insert(0, new FieldMapping
        {
            FieldName = "ZpCode",
            ColumnIndex = postalCol,
            Confidence = probe.ColumnHitRates[postalCol],
            Reasoning = $"Oracle hit rate {probe.ColumnHitRates[postalCol]:P0}"
        });

        // Check required fields
        var hasPlaceName = mappings.Any(m => m.FieldName == "PlaceName" && m.Confidence >= 0.6);
        var hasAdmin = mappings.Any(m => m.FieldName.StartsWith("Admin1"));
        var hasCoords = mappings.Any(m => m.FieldName == "Latitude") && mappings.Any(m => m.FieldName == "Longitude");

        var ambiguities = new List<string>();
        if (!hasPlaceName)
            ambiguities.Add("PlaceName confidence < 60% — manual confirmation needed");
        if (!hasAdmin && !hasCoords)
            ambiguities.Add("Neither Admin1 nor Coordinates mapped — one is required");

        // Detect competing candidates (within 15% confidence)
        foreach (var field in fieldCandidates.Keys)
        {
            var candidates = fieldCandidates[field].OrderByDescending(c => c.Confidence).ToList();
            if (candidates.Count > 1 && (candidates[0].Confidence - candidates[1].Confidence) < 0.15)
            {
                ambiguities.Add($"{field}: columns {candidates[0].ColumnIndex} and {candidates[1].ColumnIndex} within 15% confidence");
            }
        }

        return new MappingProposal
        {
            Mappings = mappings,
            RequireDisambiguation = ambiguities.Count > 0,
            AmbiguityReasons = ambiguities.ToArray()
        };
    }

    /// <summary>
    /// Calculate Levenshtein similarity (0.0–1.0).
    /// 1.0 = identical, 0.0 = completely different.
    /// </summary>
    private double LevenshteinSimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return 0.0;

        var distance = LevenshteinDistance(a.ToLowerInvariant(), b.ToLowerInvariant());
        var maxLen = Math.Max(a.Length, b.Length);
        return 1.0 - (distance / (double)maxLen);
    }

    /// <summary>
    /// Calculate Levenshtein edit distance using dynamic programming.
    /// </summary>
    private int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a))
            return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b))
            return a.Length;

        var m = a.Length;
        var n = b.Length;
        var d = new int[m + 1, n + 1];

        // Initialize first column and row
        for (int i = 0; i <= m; i++)
            d[i, 0] = i;
        for (int j = 0; j <= n; j++)
            d[0, j] = j;

        // Fill matrix
        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                var cost = (a[i - 1] == b[j - 1]) ? 0 : 1;

                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1,      // deletion
                             d[i, j - 1] + 1),     // insertion
                    d[i - 1, j - 1] + cost         // substitution
                );
            }
        }

        return d[m, n];
    }

    /// <summary>
    /// Initialize score accumulator matrix.
    /// </summary>
    private Dictionary<int, Dictionary<string, List<double>>> InitializeScoreMatrix(int columnCount)
    {
        var matrix = new Dictionary<int, Dictionary<string, List<double>>>();
        var fields = new[] { "PlaceName", "Admin1", "Admin1Code", "Timezone", "Latitude", "Longitude" };

        for (int col = 0; col < columnCount; col++)
        {
            matrix[col] = new Dictionary<string, List<double>>();
            foreach (var field in fields)
            {
                matrix[col][field] = new List<double>();
            }
        }

        return matrix;
    }
}
