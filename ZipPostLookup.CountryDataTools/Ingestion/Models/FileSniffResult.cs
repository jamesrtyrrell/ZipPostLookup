using System.Text;

namespace ZipPostLookup.CountryDataTools.Ingestion.Models;

/// <summary>
/// File format detection results from Phase 1 (File Sniff).
/// </summary>
public class FileSniffResult
{
    /// <summary>
    /// Detected file format.
    /// </summary>
    public FileFormat Format { get; set; }

    /// <summary>
    /// Detected delimiter character (e.g., ',' or '\t').
    /// </summary>
    public char Delimiter { get; set; }

    /// <summary>
    /// Detected text encoding.
    /// </summary>
    public Encoding Encoding { get; set; } = Encoding.UTF8;

    /// <summary>
    /// Whether the first row should be skipped by readers. True for a real header row
    /// (named columns) and also for a numeric-only "fake header" (a column-index row like
    /// <c>0	1	2	3…</c> from exported data) — the latter has <see cref="HeaderNames"/> null.
    /// </summary>
    public bool HasHeaderRow { get; set; }

    /// <summary>
    /// Expected number of columns.
    /// </summary>
    public int ColumnCount { get; set; }

    /// <summary>
    /// Header names when the first row is a real header. Null when there is no header,
    /// and also null when <see cref="HasHeaderRow"/> is true for a numeric-only fake
    /// header — that row is skipped but carries no usable column names.
    /// </summary>
    public string[]? HeaderNames { get; set; }

    /// <summary>
    /// Reasons why the format is ambiguous (may require LLM disambiguation).
    /// </summary>
    public List<string> AmbiguityReasons { get; set; } = new();
}

/// <summary>
/// Supported file formats for auto-import.
/// </summary>
public enum FileFormat
{
    Unknown,
    Csv,
    Tsv,
    Json,
    Excel
}
