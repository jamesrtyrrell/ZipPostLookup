namespace ZipPostLookup.CountryDataTools.Database.WorkDb;

/// <summary>
/// Static factory resolver — inspects <see cref="WorkDbConfig.Provider"/> and
/// returns the correct <see cref="IWorkDbConnectionFactory"/> implementation.
///
/// Adding a new provider requires:
///   1. Implementing <see cref="IWorkDbConnectionFactory"/>
///   2. Adding a case here
///   3. Adding the ADO.NET driver NuGet package to the .csproj
///
/// No other files need to change.
/// </summary>
public static class WorkDbConnectionFactory
{
    /// <summary>
    /// Creates the appropriate <see cref="IWorkDbConnectionFactory"/> for the
    /// provider named in <paramref name="config"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Thrown when the provider is not recognised.
    /// </exception>
    public static IWorkDbConnectionFactory Create(WorkDbConfig config) =>
        config.Provider.ToLowerInvariant() switch
        {
            "sqlserver" => SqlServerConnectionFactory.FromConfig(config),
            // "postgres"  => PostgresConnectionFactory.FromConfig(config),
            // "sqlite"    => SqliteConnectionFactory.FromConfig(config),
            _ => throw new NotSupportedException(
                $"Unknown database provider '{config.Provider}'. " +
                $"Supported values: sqlserver")
        };
}
