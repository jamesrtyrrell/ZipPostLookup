using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Database;

public interface IPipelineServices
{
    /// <summary>
    /// Retrieves records from the pipeline schema via switch-based dispatch.
    /// Supported queries: GetLatestRun, GetAllRuns → returns List&lt;PipelineRuns&gt; cast to List&lt;IPipelineSchema&gt;.
    /// Scalar queries (CountRunsByPrefix, CheckRunExists) are not supported — use inline Dapper.
    /// Caller knows the concrete type behind the interface and casts on receipt.
    /// </summary>
    Task<List<IPipelineSchema>> RetrievePipelineRecordsAsync(
        string commonQuery,
        string countryId = "",
        string runId = "",
        string status = "",
        string prefixWildcard = "");

    /// <summary>
    /// Bulk-merges records into the pipeline schema via Dapper Plus BulkMergeAsync.
    /// List items must be concrete Dbo types (PipelineRuns, PipelineDecisions).
    /// Single-item saves: wrap in a list — MergePipelineRecordsAsync([myRecord]).
    /// </summary>
    Task<bool> MergePipelineRecordsAsync(List<IPipelineSchema> pipelineRecords);
}
