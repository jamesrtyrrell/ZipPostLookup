using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ZipPostLookup.CountryDataTools.Database.Sql;

namespace ZipPostLookup.CountryDataTools.Database.WorkDb;

/// <summary>
/// Creates connections to a SQL Server instance (local Docker or any SQL Server).
///
/// Connection string format (matches the Docker container we created):
///   Server=localhost,1433;Database=ZipPostLookupWorkDB;
///   User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true
///
/// The connection string is read from workdb.json — never hardcoded or stored
/// in source control.
/// </summary>
public sealed class SqlServerConnectionFactory : IWorkDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlServerConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));

        // Ensure MARS is enabled so concurrent Dapper queries on the same connection
        // don't throw. The codebase avoids concurrent queries by convention, but this
        // makes the factory safe regardless.
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString)
        {
            MultipleActiveResultSets = true
        };
        _connectionString = builder.ConnectionString;
    }

    /// <summary>
    /// Creates a factory from a <see cref="WorkDbConfig"/>.
    /// Throws <see cref="NotSupportedException"/> if the config's provider is not "sqlserver".
    /// </summary>
    public static SqlServerConnectionFactory FromConfig(WorkDbConfig config)
    {
        if (!config.Provider.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException(
                $"SqlServerConnectionFactory cannot handle provider '{config.Provider}'. " +
                $"Expected 'sqlserver'.");

        return new SqlServerConnectionFactory(config.ConnectionString);
    }

    /// <inheritdoc/>
    public IDbConnection CreateConnection()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }

    /// <inheritdoc/>
    public async Task<(bool Ok, string? Error)> TestConnectionAsync()
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Verify the expected schemas exist — a lightweight schema reachability check
            // that confirms the script was actually run, not just that the server is up.
            var schemas = (await conn.QueryAsync<string>(
                CommonQueries.GetSchemaVerification)).ToList();

            var missing = new[] { "codes", "data", "pipeline" }
                .Except(schemas, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missing.Count > 0)
                return (false,
                    $"Connected to SQL Server but the following schemas are missing: " +
                    $"{string.Join(", ", missing)}. " +
                    $"Run zippostlookup_workdb_sqlserver.sql against ZipPostLookupWorkDB first.");

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
