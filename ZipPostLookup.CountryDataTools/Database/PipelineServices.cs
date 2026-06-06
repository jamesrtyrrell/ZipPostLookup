using Microsoft.Data.SqlClient;
using Dapper;
using Z.Dapper.Plus;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Database;

public class PipelineServices : IPipelineServices
{
    private readonly IWorkDbConnectionFactory _factory;

    public PipelineServices(IWorkDbConnectionFactory factory)
    {
        _factory = factory;
    }
    
    /// <summary>
    /// Retrieves records from the pipeline schema.
    /// Associated CommonQueries: CountRunsByPrefix, CheckRunExists, GetLatestRun, GetAllRuns
    /// </summary>
    public async Task<List<IPipelineSchema>> RetrievePipelineRecordsAsync(
        string commonQuery,
        string countryId = "",
        string runId = "",
        string status = "",
        string prefixWildcard = "")
    {
        if (string.IsNullOrEmpty(commonQuery))
        {
            throw new ArgumentNullException(nameof(commonQuery), "Common query cannot be null or empty");
        }

        var parameters = new DynamicParameters();
        parameters.Add("CountryId", countryId);
        parameters.Add("RunId", runId);
        parameters.Add("Status", status);
        parameters.Add("PrefixWildcard", prefixWildcard);

        await using var conn = (SqlConnection)_factory.CreateConnection();
        try
        {
            var result = (await conn.QueryAsync<IPipelineSchema>(commonQuery, parameters)).ToList();
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving Pipeline records: {ex.Message}");
        }

        return new List<IPipelineSchema>();
    }

    /// <summary>
    /// Merges/Upserts records into the pipeline schema.
    /// Associated CommonQueries: 
    /// </summary>
    public async Task<bool> MergePipelineRecordsAsync(List<IPipelineSchema> pipelineRecords)
    {
        if (pipelineRecords.Count == 0) { return true; }

        await using var conn = (SqlConnection)_factory.CreateConnection();
        try
        {
            await conn.BulkMergeAsync<IPipelineSchema>(pipelineRecords);
            return true;
        }
        catch (Exception ex)
        {
            var type = pipelineRecords.First().GetType();
            Console.WriteLine($"Error merging {type.Name}: {ex.Message}");
        }
        return false;
    }
}