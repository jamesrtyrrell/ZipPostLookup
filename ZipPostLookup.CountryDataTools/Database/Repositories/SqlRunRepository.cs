using Dapper;
using Dapper.Contrib.Extensions;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Database.Repositories;

/// <summary>
/// SQL Server implementation of <see cref="IRunRepository"/>.
/// All queries target <c>pipeline.runs</c>.
///
/// Inserts and updates use Dapper.Contrib against the <see cref="PipelineRuns"/>
/// model object. Read queries use plain Dapper and project into <see cref="RunSummary"/>.
/// </summary>
public sealed class SqlRunRepository : IRunRepository
{
    private readonly IWorkDbConnectionFactory _factory;

    public SqlRunRepository(IWorkDbConnectionFactory factory)
        => _factory = factory;

    /// <inheritdoc/>
    public async Task<string> CreateRunAsync(string countryCode, string sourceFilename)
    {
        using var conn = _factory.CreateConnection();

        // Generate a unique run_id: run_{yyyyMMdd}_{NNN}
        // NNN is the count of runs for this country today + 1, zero-padded to 3 digits.
        //
        // The wildcard must be embedded in the parameter value itself — passing
        // @Prefix and concatenating '%' in SQL means SQL Server evaluates
        // @Prefix + '%' as a string expression, not a LIKE pattern, so the
        // COUNT always returns 0 and every run gets the suffix _001.
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var prefix = $"run_{today}_";

        // Count ALL runs today across all countries — the PK is on run_id alone,
        // so a run_20260530_001 created for US blocks MX from using the same id.
        var existingToday = await conn.ExecuteScalarAsync<int>(
            CommonQueries.CountRunsByPrefix,
            new { PrefixWildcard = prefix + "%" });   // wildcard in the value, not in SQL

        // Retry until we find a free slot — handles concurrent inserts or leftover
        // runs from earlier sessions that the count didn't see.
        string runId;
        int attempt = existingToday + 1;
        while (true)
        {
            runId = $"{prefix}{attempt:D3}";
            var exists = await conn.ExecuteScalarAsync<int>(
                CommonQueries.CheckRunExists,
                new { RunId = runId });
            if (exists == 0) { break; }
            attempt++;
        }

        var run = new PipelineRuns
        {
            RunId = runId,
            CountryId = countryCode.ToUpperInvariant(),
            SourceFilename = sourceFilename,
            Status = nameof(RunStatus.InProgress),
            StartedAt = DateTimeOffset.UtcNow,
        };

        await conn.InsertAsync(run);

        return runId;
    }

    /// <inheritdoc/>
    public async Task CompleteRunAsync(string runId)
    {
        using var conn = _factory.CreateConnection();

        var existing = await conn.GetAsync<PipelineRuns>(runId);
        if (existing == null) { return; }

        existing.Status = nameof(RunStatus.Complete);
        existing.CompletedAt = DateTimeOffset.UtcNow;
    
        await conn.UpdateAsync(existing);
    }

    /// <inheritdoc/>
    public async Task FailRunAsync(string runId, string? notes = null)
    {
        using var conn = _factory.CreateConnection();

        var existing = await conn.GetAsync<PipelineRuns>(runId);
        if (existing == null) { return; }

        existing.Status = nameof(RunStatus.Failed);
        existing.CompletedAt = DateTimeOffset.UtcNow;
        existing.Notes = notes ?? existing.Notes;

        await conn.UpdateAsync(existing);
    }

    /// <inheritdoc/>
    public async Task<RunSummary?> GetLatestRunAsync(string countryCode)
    {
        using var conn = _factory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<RunSummary>(
            CommonQueries.GetLatestRun,
            new { CountryId = countryCode.ToUpperInvariant() });
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RunSummary>> GetRunsAsync(string countryCode)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<RunSummary>(
            CommonQueries.GetAllRuns,
            new { CountryId = countryCode.ToUpperInvariant() });

        return rows.ToList();
    }
}