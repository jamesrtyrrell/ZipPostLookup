using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Database;

public interface ICodeServices
{
    /// <summary>
    /// Retrieves records from the codes schema.
    /// Associated CommonQueries: GetCandidatesByStatus, GetCandidatesByCode, GetCandidateStateCode,
    /// GetCandidateAdminLevels, GetPendingDiscrepancies, GetDiscrepancyFieldSummary,
    /// GetDistinctNamesFromDiscrepancies
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
    /// Merges/Upserts records into the codes schema.
    /// Associated CommonQueries: UpdateCandidateStatus, BulkUpdateCandidateStatus,
    /// UpdateCandidateStatusUnfound, MarkCandidatesAsError, UpdateDiscrepancyProcessed,
    /// UpdateDiscrepancyWithOverride, MarkDiscrepanciesProcessed
    /// </summary>
    Task<bool> MergeCodesAsync(List<ICodesSchema> codes);
    
}