using ZipPostLookup.CountryDataTools.DSV;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Models.Enums;

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

    /// <summary>Marks the run as failed.</summary>
    Task FailRunAsync(string runId, string? notes = null);

    /// <summary>Returns the most recent run for the given country, or null.</summary>
    Task<RunSummary?> GetLatestRunAsync(string countryCode);

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
// Manages codes.candidate — the immutable raw import rows.
// =============================================================================

/// <summary>
/// Manages <c>codes.candidate</c> — one row per imported code, never modified
/// after insert. Status is updated as the pipeline processes each row.
/// </summary>
public interface ICandidateRepository
{
    /// <summary>
    /// Bulk-inserts <paramref name="candidates"/> into codes.candidate and
    /// codes.candidate_admins for the given run.
    /// </summary>
    Task InsertBatchAsync(string runId, string countryCode,
        IReadOnlyList<CodesCandidate> candidates);

    /// <summary>
    /// Updates the status of a single candidate row.
    /// </summary>
    Task UpdateStatusAsync(string runId, string countryCode,
        string code, string name, CandidateStatus status);

    /// <summary>
    /// Bulk-updates status for all rows matching the given zips within a run.
    /// Used by FullScanner to mark clean rows in one pass.
    /// </summary>
    Task BulkUpdateStatusAsync(string runId, string countryCode,
        IReadOnlyList<(string Zip, string City, CandidateStatus Status)> updates);

    /// <summary>
    /// Returns all candidate rows for the given run and status.
    /// </summary>
    Task<IReadOnlyList<CsvRow>> GetByStatusAsync(string runId, string countryCode,
        CandidateStatus status);

    /// <summary>Returns candidate rows for a specific zip within a run.</summary>
    Task<IReadOnlyList<CsvRow>> GetByZipAsync(string runId, string countryCode,
        string zip);
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
    /// Applies a reviewer decision to all field rows for a (zip, Name) pair:
    ///   - Sets AcceptIncoming and Process=1
    ///   - Optionally sets OverrideValue when a third-party correct value is known
    ///   - Inserts a row into pipeline.decisions
    ///   - Updates codes.candidate.status to 'clean' or 'rejected'
    /// All changes are committed in a single transaction.
    /// Used for human-reviewed decisions. For auto-rejection bulk paths use
    /// <see cref="BulkInsertDecisionsAsync"/>.
    /// </summary>
    Task ApplyDecisionAsync(string runId, string countryCode,
        string code, string name, DecisionRequest decision);

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

    /// <summary>
    /// Returns a per-field triage summary for the given run — how many
    /// discrepancies exist per field, and how many are resolved vs pending.
    /// </summary>
    Task<IReadOnlyList<DiscrepancyFieldSummary>> GetFieldSummaryAsync(
        string runId, string countryCode);
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
    string? InValue     // value from the candidate
);

/// <summary>Reviewer decision to apply to a (zip, Name) pair.</summary>
public sealed record DecisionRequest(
    bool AcceptIncoming,
    string? OverrideTimezone = null,
    string? OverrideStateName = null,
    string? DecidedBy = null,
    string? Notes = null
);

/// <summary>Per-field discrepancy count summary for triage.</summary>
public sealed record DiscrepancyFieldSummary(
    string FieldName,
    int Total,
    int Unresolved,
    int Accepted,
    int Rejected
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

    /// <summary>Returns all reference entries for a specific zip.</summary>
    Task<IReadOnlyList<DataReference>> GetByZipAsync(string countryCode, string zip);

    /// <summary>Returns the row count currently in [data].[reference] for this country.</summary>
    Task<int> GetCountAsync(string countryCode);

    /// <summary>
    /// Deletes all rows in [data].[reference] for the given country.
    /// Used by importref --force to clear stale data before a full re-import.
    /// Also resets data.country_info.CodeCount to 0.
    /// </summary>
    Task DeleteAllAsync(string countryCode);
}