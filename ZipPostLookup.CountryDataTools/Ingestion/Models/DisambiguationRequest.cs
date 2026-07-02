namespace ZipPostLookup.CountryDataTools.Ingestion.Models;

/// <summary>
/// LLM disambiguation request (Phase 4 input).
/// </summary>
public class DisambiguationRequest
{
    /// <summary>
    /// File sniff result.
    /// </summary>
    public FileSniffResult Sniff { get; set; } = null!;

    /// <summary>
    /// Oracle probe result.
    /// </summary>
    public ProbeResult Probe { get; set; } = null!;

    /// <summary>
    /// Initial mapping proposal that needs disambiguation.
    /// </summary>
    public MappingProposal Proposal { get; set; } = null!;

    /// <summary>
    /// Sample rows from the file (for LLM context).
    /// </summary>
    public string[][] SampleRows { get; set; } = Array.Empty<string[]>();

    /// <summary>
    /// Maximum number of sample rows to include in the prompt (default: 5).
    /// </summary>
    public int MaxSampleRows { get; set; } = 5;
}
