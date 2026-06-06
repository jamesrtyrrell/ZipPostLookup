using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Database;

public interface IPipelineServices
{
    /// <summary>
    /// Retrieves records from the pipeline schema.
    /// Associated CommonQueries: CountRunsByPrefix, CheckRunExists, GetLatestRun, GetAllRuns
    /// </summary>
    Task<List<IPipelineSchema>> RetrievePipelineRecordsAsync(
        string commonQuery,
        string countryId = "",
        string runId = "",
        string status = "",
        string prefixWildcard = "");
    
    /// <summary>
    /// Merges/Upserts records into the pipeline schema.
    /// Associated CommonQueries: 
    /// </summary>
    Task<bool> MergePipelineRecordsAsync(List<IPipelineSchema> pipelineRecords);

}