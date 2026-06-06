using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Z.Dapper.Plus;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.DSV;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Database.Repositories;

/// <summary>
/// SQL Server implementation of <see cref="ICandidateRepository"/>.
/// Inserts candidates row-by-row via Dapper so that the generated
/// <c>CandidateId</c> identity value is available for the follow-up
/// <c>codes.candidate_admins</c> inserts.
/// </summary>
public sealed class SqlCandidateRepository : ICandidateRepository
{
    private readonly IWorkDbConnectionFactory _factory;

    public SqlCandidateRepository(IWorkDbConnectionFactory factory)
        => _factory = factory;

    /// <inheritdoc/>
    public async Task InsertBatchAsync(
        string runId, string countryCode, IReadOnlyList<CodesCandidate> candidates)
    {
        if (candidates.Count == 0) { return; }
        
        await using var conn = (SqlConnection)_factory.CreateConnection();
        
        var candidateChunks = candidates.Chunk(5000);
        var counter = 0;
        var codesCandidatesEnumerable = candidateChunks.ToList();
        
        foreach (var codeCandidates in codesCandidatesEnumerable)
        {
     
            conn.BulkInsert(codeCandidates)
                .AlsoBulkMerge(x => x.AdminCandidateList);

            counter++;
            if(counter % 5 == 0)
            {
                Console.WriteLine($"Processed {counter} chunks of {codesCandidatesEnumerable.Count()}");
            }
        }
        
    }


    /// <inheritdoc/>
    public async Task UpdateStatusAsync(
        string runId, string countryCode,
        string code, string name, CandidateStatus status)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(
            CommonQueries.UpdateCandidateStatus,
            new
            {
                CountryId = countryCode.ToUpperInvariant(),
                RunId     = runId,
                ZpCode    = code,
                PlaceName = name,
                Status    = StatusToString(status),
            });
    }

    /// <inheritdoc/>
    public async Task BulkUpdateStatusAsync(
        string runId, string countryCode,
        IReadOnlyList<(string Zip, string City, CandidateStatus Status)> updates)
    {
        if (updates.Count == 0) { return; }

        var table = new DataTable();
        table.Columns.Add("Code",    typeof(string));
        table.Columns.Add("Name",   typeof(string));
        table.Columns.Add("Status", typeof(string));

        foreach (var (zip, city, s) in updates)
        {
            table.Rows.Add(zip, city, StatusToString(s));
        }

        await using var conn = (SqlConnection)_factory.CreateConnection();
        await using var cmd  = conn.CreateCommand();

        cmd.CommandText = CommonQueries.BulkUpdateCandidateStatus;

        cmd.Parameters.AddWithValue("@CountryId", countryCode.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@RunId",     runId);

        var tvp       = cmd.Parameters.AddWithValue("@Updates", table);
        tvp.SqlDbType = SqlDbType.Structured;
        tvp.TypeName  = "codes.StatusUpdateType";

        await cmd.ExecuteNonQueryAsync();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CsvRow>> GetByStatusAsync(
        string runId, string countryCode, CandidateStatus status)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<CandidateDbRow>(
            CommonQueries.GetCandidatesByStatus,
            new
            {
                CountryId = countryCode.ToUpperInvariant(),
                RunId     = runId,
                Status    = StatusToString(status),
            });

        return rows.Select(MapToCsvRow).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CsvRow>> GetByZipAsync(
        string runId, string countryCode, string zip)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<CandidateDbRow>(
            CommonQueries.GetCandidatesByCode,
            new
            {
                CountryId = countryCode.ToUpperInvariant(),
                RunId     = runId,
                ZpCode    = zip,
            });

        return rows.Select(MapToCsvRow).ToList();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // Enum name IS the DB value — "Clean", "ValidationError", etc.
    private static string StatusToString(CandidateStatus status) => status.ToString();

    private static CsvRow MapToCsvRow(CandidateDbRow r) =>
        new CsvRow
        {
            RecordNumber = r.RecordNumber,
            ZpCode       = r.ZpCode,
            PlaceName    = r.PlaceName == "" ? null : r.PlaceName,
            Timezone     = r.Timezone,
            IsDefault    = r.IsDefault,
            Admin1       = r.Admin1,
            Admin1Code   = r.Admin1Code,
        };

}