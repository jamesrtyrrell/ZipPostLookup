namespace ZipPostLookup.CountryDataTools.Ingestion.Models;

/// <summary>
/// Proposed field-to-column mapping from Phase 3 (correlation).
/// </summary>
public class MappingProposal
{
    /// <summary>
    /// Field mappings (field name → column index + confidence).
    /// </summary>
    public List<FieldMapping> Mappings { get; set; } = new();

    /// <summary>
    /// Whether LLM disambiguation is required.
    /// </summary>
    public bool RequireDisambiguation { get; set; }

    /// <summary>
    /// Reasons why disambiguation is needed.
    /// </summary>
    public string[] AmbiguityReasons { get; set; } = Array.Empty<string>();
}

/// <summary>
/// A single field-to-column mapping.
/// </summary>
public class FieldMapping
{
    /// <summary>
    /// Target field name (e.g., "PlaceName", "Admin1").
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Source column index (null if unmapped).
    /// </summary>
    public int? ColumnIndex { get; set; }

    /// <summary>
    /// Confidence score (0.0–1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Human-readable reasoning for this mapping.
    /// </summary>
    public string Reasoning { get; set; } = string.Empty;
}

/// <summary>
/// A column candidate for a specific field (used internally during correlation).
/// </summary>
public class ColumnCandidate
{
    /// <summary>
    /// Column index.
    /// </summary>
    public int ColumnIndex { get; set; }

    /// <summary>
    /// Field name this column is a candidate for.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score (0.0–1.0).
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Reasoning for this score.
    /// </summary>
    public string Reasoning { get; set; } = string.Empty;
}
