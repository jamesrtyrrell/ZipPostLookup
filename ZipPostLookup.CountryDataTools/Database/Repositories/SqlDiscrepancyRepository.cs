using Dapper;
using Microsoft.Data.SqlClient;
using Z.Dapper.Plus;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Database.Repositories;

/// <summary>
/// SQL Server implementation of <see cref="IDiscrepancyRepository"/>.
///
/// Each <see cref="DiscrepancyInput"/> produces one row in codes.discrepancies
/// per differing field, enabling bulk triage SQL against field_name.
/// pipeline.decisions is written via PipelineDecisions using Z.Dapper.Plus.
/// </summary>
public sealed class SqlDiscrepancyRepository : IDiscrepancyRepository
{
    private readonly IWorkDbConnectionFactory _factory;

    public SqlDiscrepancyRepository(IWorkDbConnectionFactory factory)
        => _factory = factory;

    /// <inheritdoc/>
    public async Task AppendAsync(
        string runId, string countryCode,
        IReadOnlyList<DiscrepancyInput> inputs)
    {
        if (inputs.Count == 0) { return; }

        var cc = countryCode.ToUpperInvariant();

        var rows = inputs.Select(input => new CodesDiscrepancies
        {
            CountryId    = cc,
            RunId        = runId,
            ZpCode       = input.Code,
            PlaceName    = input.Name,
            AdminLevelId = null,
            FieldName    = input.FieldName,
            RefValue     = input.RefValue,
            InValue      = input.InValue,
            Notes        = input.Notes,
            Process      = false,
        }).ToList();

        await using var conn = (SqlConnection)_factory.CreateConnection();
        conn.BulkInsert(rows);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CodesDiscrepancies>> GetPendingAsync(string countryCode)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<CodesDiscrepancies>(
            CommonQueries.GetPendingDiscrepancies,
            new { CountryId = countryCode.ToUpperInvariant() });

        return rows.ToList();
    }

    /// <inheritdoc/>
    public async Task BulkInsertDecisionsAsync(IReadOnlyList<PipelineDecisions> decisions)
    {
        if (decisions.Count == 0) { return; }

        await using var conn = (SqlConnection)_factory.CreateConnection();
        conn.BulkInsert(decisions);
    }
}