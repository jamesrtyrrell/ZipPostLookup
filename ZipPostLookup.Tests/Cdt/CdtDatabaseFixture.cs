using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Xunit;
using ZipPostLookup.CountryDataTools.Database.Configuration;
using ZipPostLookup.CountryDataTools.Database.WorkDb;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// Provisions an isolated, throwaway SQL Server database for the CDT integration tests:
/// creates <c>ZipPostLookupWorkDB_Test</c> from the canonical schema script, seeds a minimal
/// known dataset (country_info + admin levels), and drops it on teardown — so the developer's
/// curation database is never touched. The base connection is read from <c>workdb.json</c>
/// (the same per-folder config the CLI uses). If no config is found or the server can't be
/// provisioned, <see cref="Available"/> is false and the integration tests skip.
/// </summary>
public sealed class CdtDatabaseFixture : IAsyncLifetime
{
    private const string TestDbName = "ZipPostLookupWorkDB_Test";

    public bool Available { get; private set; }
    public string SkipReason { get; private set; } = "test database unavailable";
    public WorkDbContext Db { get; private set; } = null!;

    private string _masterConn = "";
    private string _testConn = "";

    /// <summary>Opens a fresh connection to the test database (caller disposes). For arrange/assert.</summary>
    public SqlConnection OpenConnection()
    {
        var c = new SqlConnection(_testConn);
        c.Open();
        return c;
    }

    public async ValueTask InitializeAsync()
    {
        var cfgPath = WorkDbConfig.FindConfigFile(AppContext.BaseDirectory);
        if (cfgPath is null) { SkipReason = "no workdb.json found (walking up from the test directory)"; return; }

        WorkDbConfig cfg;
        try { cfg = WorkDbConfig.Load(cfgPath); }
        catch (Exception ex) { SkipReason = $"workdb.json invalid: {ex.Message}"; return; }

        if (!cfg.Provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            SkipReason = $"provider '{cfg.Provider}' not supported by this fixture";
            return;
        }

        try
        {
            _masterConn = new SqlConnectionStringBuilder(cfg.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
            _testConn   = new SqlConnectionStringBuilder(cfg.ConnectionString) { InitialCatalog = TestDbName }.ConnectionString;

            await CreateFreshDatabaseAsync();
            await RunSchemaAsync();   // the schema script seeds CountryInfo (US/CA/MX) + AdminLevels;
                                      // data.Reference is left empty, which the tests rely on.

            DapperPlusConfiguration.Configure();   // register bulk mappings (normally done in Program.cs)

            Db = WorkDbContext.FromConfig(new WorkDbConfig
            {
                Provider         = "sqlserver",
                ConnectionString = _testConn,
                CountryCode      = "US",
                ActiveRunId      = "run_test_001",
            });

            Available = true;
        }
        catch (Exception ex)
        {
            SkipReason = $"could not provision test DB: {ex.Message}";
            try { await DropDatabaseAsync(); } catch { /* best-effort cleanup */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!string.IsNullOrEmpty(_masterConn))
        {
            try { await DropDatabaseAsync(); } catch { /* best-effort cleanup */ }
        }
    }

    // ── Provisioning ───────────────────────────────────────────────────────────

    private async Task CreateFreshDatabaseAsync()
    {
        await using var conn = new SqlConnection(_masterConn);
        await conn.OpenAsync();
        await ExecAsync(conn, $"{DropSql}\nCREATE DATABASE [{TestDbName}];");
    }

    private async Task DropDatabaseAsync()
    {
        await using var conn = new SqlConnection(_masterConn);
        await conn.OpenAsync();
        await ExecAsync(conn, DropSql);
    }

    private static string DropSql => $@"
        IF DB_ID('{TestDbName}') IS NOT NULL
        BEGIN
            ALTER DATABASE [{TestDbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE [{TestDbName}];
        END";

    private async Task RunSchemaAsync()
    {
        // Re-point the script's hardcoded DB name at the throwaway test database.
        var script = (await File.ReadAllTextAsync(LocateSchemaScript()))
            .Replace("ZipPostLookupWorkDB", TestDbName);

        await using var conn = new SqlConnection(_testConn);
        await conn.OpenAsync();

        // SqlClient can't run multi-batch scripts; split on standalone GO lines.
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
                await ExecAsync(conn, batch);
        }
    }

    private static async Task ExecAsync(SqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 120;
        await cmd.ExecuteNonQueryAsync();
    }

    private static string LocateSchemaScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName,
                "ZipPostLookup.CountryDataTools", "Database", "Sql", "zippostlookup_workdb_sqlserver.sql");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate zippostlookup_workdb_sqlserver.sql by walking up from the test directory.");
    }
}
