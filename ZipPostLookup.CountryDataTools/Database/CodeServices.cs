using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Z.Dapper.Plus;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Database;

public class CodeServices : ICodeServices
{
    private readonly IWorkDbConnectionFactory _factory;

    public CodeServices(IWorkDbConnectionFactory factory)
        => _factory = factory;

    /// <inheritdoc/>
    public async Task<List<ICodesSchema>> RetrieveCodesRecordsAsync(
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
        bool curated = false)
    {
        if (string.IsNullOrEmpty(commonQuery))
            throw new ArgumentNullException(nameof(commonQuery), "Common query cannot be null or empty");

        var parameters = new DynamicParameters();
        parameters.Add("CountryId",       countryId);
        parameters.Add("RunId",           runId);
        parameters.Add("Status",          status);
        parameters.Add("Code",            code);
        parameters.Add("Name",            name);
        parameters.Add("ZpCode",          code);
        parameters.Add("PlaceName",       name);
        parameters.Add("CandidateId",     candidateId);
        parameters.Add("AdminLevelId",    adminLevelId);
        parameters.Add("AcceptIncoming",  acceptIncoming);
        parameters.Add("OverrideTimezone",overrideTimezone);
        parameters.Add("OverrideName",    overrideName);
        parameters.Add("State",           state);
        parameters.Add("StateName",       stateName);
        parameters.Add("Timezone",        timezone);
        parameters.Add("Curated",         curated, DbType.Boolean);

        await using var conn = (SqlConnection)_factory.CreateConnection();
        try
        {
            if (commonQuery == CommonQueries.GetCandidatesByStatus
             || commonQuery == CommonQueries.GetCandidatesByCode)
                return (await conn.QueryAsync<CodesCandidate>(commonQuery, parameters))
                       .Cast<ICodesSchema>().ToList();

            if (commonQuery == CommonQueries.GetCandidateAdminLevels)
                return (await conn.QueryAsync<CodesAdminLevel>(commonQuery, parameters))
                       .Cast<ICodesSchema>().ToList();

            if (commonQuery == CommonQueries.GetPendingDiscrepancies)
                return (await conn.QueryAsync<CodesDiscrepancies>(commonQuery, parameters))
                       .Cast<ICodesSchema>().ToList();

            throw new NotSupportedException(
                $"Query not mapped in RetrieveCodesRecordsAsync. " +
                $"Use inline Dapper for projection/scalar queries.");
        }
        catch (NotSupportedException) { throw; }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving Codes records: {ex.Message}");
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<bool> MergeCodesAsync(List<ICodesSchema> codes)
    {
        if (codes.Count == 0) return true;

        await using var conn = (SqlConnection)_factory.CreateConnection();
        try
        {
            await conn.BulkMergeAsync<ICodesSchema>(codes);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error merging {codes.First().GetType().Name}: {ex.Message}");
            return false;
        }
    }
}
