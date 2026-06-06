using System.Data;

namespace ZipPostLookup.CountryDataTools.Database.WorkDb;

/// <summary>
/// Factory that creates open database connections for the working database.
///
/// This is the provider seam: swap in a different implementation to point the
/// entire pipeline at a different database engine without touching any command
/// or repository code.
///
/// Current implementation : SqlServerConnectionFactory (Docker / local SQL Server)
/// Reserved for future use : PostgresConnectionFactory, SqliteConnectionFactory
///
/// Usage:
///   var factory = WorkDbConnectionFactory.Create(config);Z
///   using var conn = factory.CreateConnection();   // always open and ready
/// </summary>
public interface IWorkDbConnectionFactory
{
    /// <summary>
    /// Creates and returns an open <see cref="IDbConnection"/>.
    /// The caller is responsible for disposing it.
    /// </summary>
    IDbConnection CreateConnection();

    /// <summary>
    /// Opens a test connection and verifies the ZipPostLookupWorkDB schema is
    /// reachable. Returns (true, null) on success or (false, errorMessage) on
    /// failure. Never throws.
    /// </summary>
    Task<(bool Ok, string? Error)> TestConnectionAsync();
}
