using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.SqlClient;
using Z.Dapper.Plus;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Models.Enums;

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
            Process      = false,
        }).ToList();

        await using var conn = (SqlConnection)_factory.CreateConnection();
        conn.BulkInsert(rows);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CodesDiscrepancies>> GetPendingAsync(
        string runId, string countryCode)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<CodesDiscrepancies>(
            CommonQueries.GetPendingDiscrepancies,
            new { CountryId = countryCode.ToUpperInvariant(), RunId = runId });

        return rows.ToList();
    }

    /// <inheritdoc/>
    public async Task ApplyDecisionAsync(
        string runId, string countryCode,
        string code, string name, DecisionRequest decision)
    {
        await using var conn = (SqlConnection)_factory.CreateConnection();
        await using var tx   = conn.BeginTransaction();

        try
        {
            // 1. Mark all field rows for this (ZpCode, PlaceName) as processed.
            await conn.ExecuteAsync(
                CommonQueries.UpdateDiscrepancyProcessed,
                new
                {
                    CountryId        = countryCode.ToUpperInvariant(),
                    RunId            = runId,
                    ZpCode           = code,
                    PlaceName        = name,
                    decision.AcceptIncoming,
                    OverrideTimezone = decision.OverrideTimezone ?? (object)DBNull.Value,
                },
                tx);

            // 2. Update candidate status.
            await conn.ExecuteAsync(
                CommonQueries.UpdateCandidateStatus,
                new
                {
                    CountryId = countryCode.ToUpperInvariant(),
                    RunId     = runId,
                    ZpCode    = code,
                    PlaceName = name,
                    Status    = decision.AcceptIncoming
                                    ? nameof(CandidateStatus.Clean)
                                    : nameof(CandidateStatus.Rejected),
                },
                tx);

            // 3. Insert the immutable audit row into pipeline.decisions.
            await conn.InsertAsync(new PipelineDecisions
            {
                CountryId      = countryCode.ToUpperInvariant(),
                RunId          = runId,
                ZpCode         = code,
                PlaceName      = name,
                AcceptIncoming = decision.AcceptIncoming,
                DecidedBy      = decision.DecidedBy      ?? string.Empty,
                Notes          = decision.Notes           ?? string.Empty,
                CreatedAt      = DateTimeOffset.UtcNow,
            }, tx);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task BulkInsertDecisionsAsync(IReadOnlyList<PipelineDecisions> decisions)
    {
        if (decisions.Count == 0) { return; }

        await using var conn = (SqlConnection)_factory.CreateConnection();
        conn.BulkInsert(decisions);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DiscrepancyFieldSummary>> GetFieldSummaryAsync(
        string runId, string countryCode)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<DiscrepancyFieldSummary>(
            CommonQueries.GetDiscrepancyFieldSummary,
            new { CountryId = countryCode.ToUpperInvariant(), RunId = runId });

        return rows.ToList();
    }
}