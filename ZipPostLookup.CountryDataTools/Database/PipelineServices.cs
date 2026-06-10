using Dapper;
using Microsoft.Data.SqlClient;
using Z.Dapper.Plus;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Database;

public class PipelineServices : IPipelineServices
{
    private readonly IWorkDbConnectionFactory _factory;

    public PipelineServices(IWorkDbConnectionFactory factory)
        => _factory = factory;

    /// <inheritdoc/>
    public async Task<List<IPipelineSchema>> RetrievePipelineRecordsAsync(
        string commonQuery,
        string countryId = "",
        string runId = "",
        string status = "",
        string prefixWildcard = "")
    {
        if (string.IsNullOrEmpty(commonQuery))
            throw new ArgumentNullException(nameof(commonQuery), "Common query cannot be null or empty");

        var parameters = new DynamicParameters();
        parameters.Add("CountryId",     countryId);
        parameters.Add("RunId",         runId);
        parameters.Add("Status",        status);
        parameters.Add("PrefixWildcard",prefixWildcard);

        await using var conn = (SqlConnection)_factory.CreateConnection();
        try
        {
            if (commonQuery == CommonQueries.GetLatestRun
             || commonQuery == CommonQueries.GetAllRuns)
                return (await conn.QueryAsync<PipelineRuns>(commonQuery, parameters))
                       .Cast<IPipelineSchema>().ToList();

            throw new NotSupportedException(
                $"Query not mapped in RetrievePipelineRecordsAsync. " +
                $"Use inline Dapper for scalar queries (CountRunsByPrefix, CheckRunExists).");
        }
        catch (NotSupportedException) { throw; }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving Pipeline records: {ex.Message}");
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<bool> MergePipelineRecordsAsync(List<IPipelineSchema> pipelineRecords)
    {
        if (pipelineRecords.Count == 0) return true;

        await using var conn = (SqlConnection)_factory.CreateConnection();
        try
        {
            await conn.BulkMergeAsync<IPipelineSchema>(pipelineRecords);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error merging {pipelineRecords.First().GetType().Name}: {ex.Message}");
            return false;
        }
    }
}
