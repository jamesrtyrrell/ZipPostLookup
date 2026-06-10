using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Database.Repositories;

// =============================================================================
// IRunRepository
//
// Manages pipeline.runs — one row per import session.
// =============================================================================

/// <summary>
/// Manages <c>pipeline.runs</c> — the provenance record for every import session.
/// </summary>
public interface IRunRepository
{
    /// <summary>
    /// Creates a new run row and returns the generated run_id.
    /// Format: run_{yyyyMMdd}_{NNN} — NNN auto-increments within the same day.
    /// </summary>
    Task<string> CreateRunAsync(string countryCode, string sourceFilename);

    /// <summary>Marks the run as complete and stamps completed_at.</summary>
    Task CompleteRunAsync(string runId);

    /// <summary>Returns all runs for the given country, newest first.</summary>
    Task<IReadOnlyList<RunSummary>> GetRunsAsync(string countryCode);
}

/// <summary>Lightweight summary of a pipeline.runs row.</summary>
public sealed record RunSummary(
    string RunId,
    string CountryId,
    string SourceFilename,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    string? Notes
);


// =============================================================================
// ICandidateRepository
//
// Manages [codes].[candidate] — the immutable raw import rows.
// =============================================================================

/// <summary>
/// Manages <c>[codes].[candidate]</c> — one row per imported code, never modified
/// after insert. Status is updated as the pipeline processes each row.
/// </summary>
public interface ICandidateRepository
{
    /// <summary>
    /// Bulk-inserts <paramref name="candidates"/> into [codes].[candidate] and
    /// [codes].[candidate]_admins for the given run.
    /// </summary>
    Task InsertBatchAsync(IReadOnlyList<CodesCandidate> candidates);
}

// =============================================================================
// IDiscrepancyRepository
//
// Manages codes.discrepancies (per-field rows) and pipeline.decisions (audit).
// =============================================================================

/// <summary>
/// Manages <c>codes.discrepancies</c> — one row per differing field per candidate
/// row — and <c>pipeline.decisions</c> — the immutable audit trail.
/// </summary>
public interface IDiscrepancyRepository
{
    /// <summary>
    /// Inserts decomposed discrepancy rows — one per differing field.
    /// Each <see cref="DiscrepancyInput"/> represents a single field comparison
    /// (ref value vs incoming value) for a specific zip+Name pair.
    /// Existing (CountryId, run_id, zip, Name, field_name) combinations are
    /// skipped (upsert-style deduplication).
    /// </summary>
    Task AppendAsync(string runId, string countryCode,
        IReadOnlyList<DiscrepancyInput> inputs);

    /// <summary>
    /// Returns all unresolved discrepancies (Process=0) for the given run.
    /// </summary>
    Task<IReadOnlyList<CodesDiscrepancies>> GetPendingAsync(string runId,
        string countryCode);

    /// <summary>
    /// Bulk-inserts a batch of pre-built <see cref="PipelineDecisions"/> rows into
    /// pipeline.decisions in a single round-trip.
    ///
    /// Used by ImportCandidatesCommand for Rule-5 auto-rejections, where
    /// no discrepancy rows exist to update and candidate status is already
    /// handled by the chunk BulkUpdate — making a full ApplyDecisionAsync
    /// call per row wasteful.
    /// </summary>
    Task BulkInsertDecisionsAsync(IReadOnlyList<PipelineDecisions> decisions);
}

/// <summary>
/// Input for a single field-level discrepancy — used when appending new discrepancy
/// rows. Replaces the retired <c>DiscrepancyRecord</c> pipeline type.
/// One candidate row may produce multiple <see cref="DiscrepancyInput"/> entries
/// (one per differing field: Name, state, state_name, timezone, IsDefault).
/// </summary>
public sealed record DiscrepancyInput(
    string Code,
    string Name,
    int? AdminLevelId,
    string FieldName,   // "Name" | "state" | "state_name" | "timezone" | "IsDefault"
    string? RefValue,   // value from data.reference
    string? InValue,    // value from the candidate
    string? Notes = null
);


// =============================================================================
// IReferenceRepository
//
// Manages [data].[reference] — the read-only embedded CSV data loaded once on init.
// =============================================================================

/// <summary>
/// Manages [data].[reference] — the rows loaded from the embedded
/// ZipPostLookup Data/**/**.csv files. Read-only after the initial load.
/// </summary>
public interface IReferenceRepository
{
    /// <summary>
    /// Returns true if reference data has been loaded for the given country.
    /// </summary>
    Task<bool> HasDataAsync(string countryCode);

    /// <summary>
    /// Bulk-loads the embedded CSV data for <paramref name="countryCode"/> into
    /// [data].[reference]. Skips rows that already exist (idempotent).
    /// Also updates [data].[country_info].[CodeCount] after loading.
    /// </summary>
    Task LoadFromEmbeddedCsvAsync(string countryCode);

    /// <summary>Returns the row count currently in [data].[reference] for this country.</summary>
    Task<int> GetCountAsync(string countryCode);

    /// <summary>
    /// Deletes all rows in [data].[reference] for the given country.
    /// Used by importref --force to clear stale data before a full re-import.
    /// Also resets data.country_info.CodeCount to 0.
    /// </summary>
    Task DeleteAllAsync(string countryCode);
}