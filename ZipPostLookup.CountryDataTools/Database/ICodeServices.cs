using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Database;

public interface ICodeServices
{
    /// <summary>
    /// Retrieves records from the codes schema via switch-based dispatch.
    /// Supported queries: GetCandidatesByStatus, GetCandidatesByCode, GetCandidateAdminLevels, GetPendingDiscrepancies.
    /// Projection/scalar queries (GetCandidateStateCode, GetDiscrepancyFieldSummary,
    /// GetDistinctNamesFromDiscrepancies) are not supported here — use inline Dapper.
    /// Caller knows the concrete type behind the interface and casts on receipt.
    /// </summary>
    Task<List<ICodesSchema>> RetrieveCodesRecordsAsync(
        string commonQuery,
        string countryId = "",
        string runId = "",
        string status = "",
        string code = "",
        string name = "",
        long candidateId = 0,
        int adminLevelId = 0,
        bool acceptIncoming = false,
        string overrideTimezone = "",
        string overrideName = "",
        string state = "",
        string stateName = "",
        string timezone = "",
        bool curated = false);

    /// <summary>
    /// Bulk-merges records into the codes schema via Dapper Plus BulkMergeAsync.
    /// List items must be concrete Dbo types (CodesCandidate, CodesDiscrepancies, etc.).
    /// Single-item saves: wrap in a list — MergeCodesAsync([myRecord]).
    /// </summary>
    Task<bool> MergeCodesAsync(List<ICodesSchema> codes);
}
