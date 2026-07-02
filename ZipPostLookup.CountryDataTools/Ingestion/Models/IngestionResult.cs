namespace ZipPostLookup.CountryDataTools.Ingestion.Models;

/// <summary>
/// Results from Phase 6 (ingestion).
/// </summary>
public class IngestionResult
{
    /// <summary>
    /// Total rows in the source file.
    /// </summary>
    public int TotalRows { get; set; }

    /// <summary>
    /// Number of candidates generated (valid rows).
    /// </summary>
    public int CandidatesGenerated { get; set; }

    /// <summary>
    /// Number of rows inserted into data.Reference.
    /// </summary>
    public int Inserted { get; set; }

    /// <summary>
    /// Number of discrepancies created.
    /// </summary>
    public int Discrepancies { get; set; }

    /// <summary>
    /// Number of rows skipped (already exist, rule-based rejection).
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>
    /// Number of rows rejected (format errors, validation failures).
    /// </summary>
    public int RejectedRows { get; set; }

    /// <summary>
    /// Path to the rejected rows CSV file (if any were rejected).
    /// </summary>
    public string? RejectedFilePath { get; set; }

    /// <summary>
    /// Postal codes that were not found in the built-in oracle (for feedback loop).
    /// </summary>
    public List<string> OracleMissedCodes { get; set; } = new();

    /// <summary>
    /// Whether this was a dry-run (no data written).
    /// </summary>
    public bool DryRun { get; set; }
}
