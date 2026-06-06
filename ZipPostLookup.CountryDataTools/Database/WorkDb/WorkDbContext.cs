using ZipPostLookup.CountryDataTools.Database.Repositories;

namespace ZipPostLookup.CountryDataTools.Database.WorkDb;

/// <summary>
/// The single composition root for the database layer.
///
/// Commands obtain a <see cref="WorkDbContext"/> via <see cref="WorkDbContext.LoadAsync"/>
/// which walks up the directory tree for workdb.json, builds the right factory,
/// tests the connection, and exposes all repositories.
/// </summary>
public sealed class WorkDbContext
{
    private readonly IWorkDbConnectionFactory _factory;

    /// <summary>Country code  in this context is scoped to, e.g. "US".</summary>
    public string CountryCode { get; }

    /// <summary>
    /// The active run ID from workdb.json. Commands use this as the default
    /// run ID so they don't need to pass it around explicitly.
    /// </summary>
    public string ActiveRunId { get; }

    /// <summary>
    /// The directory that contains workdb.json — i.e. the repo root.
    /// Use this instead of Directory.GetCurrentDirectory() when building
    /// paths to project files, so commands work from any subdirectory.
    /// </summary>
    public string RepoRoot { get; }

    /// <summary>Manages pipeline.runs.</summary>
    public IRunRepository Runs { get; }

    /// <summary>Manages [codes].[candidate].</summary>
    public ICandidateRepository Candidates { get; }

    /// <summary>Manages [codes].[discrepancies] and [pipeline].[decisions].</summary>
    public IDiscrepancyRepository Discrepancies { get; }

    /// <summary>Manages [data].[reference] and data.country_info CodeCount.</summary>
    public IReferenceRepository Reference { get; }
    
    // -------------------------------------------------------------------------

    private WorkDbContext(
        string countryCode,
        string activeRunId,
        string repoRoot,
        IWorkDbConnectionFactory factory)
    {
        _factory      = factory;
        CountryCode   = countryCode;
        ActiveRunId   = activeRunId;
        RepoRoot      = repoRoot;
        Runs          = new SqlRunRepository(factory);
        Candidates    = new SqlCandidateRepository(factory);
        Discrepancies = new SqlDiscrepancyRepository(factory);
        Reference     = new SqlReferenceRepository(factory);
    }

    /// <summary>
    /// Exposes the underlying factory so commands can construct additional
    /// repositories without adding them to this context permanently.
    /// </summary>
    public IWorkDbConnectionFactory GetFactory() => _factory;

    /// <summary>
    /// Searches <paramref name="workingDirectory"/> and its parents for workdb.json,
    /// builds the appropriate connection factory, tests the connection, and
    /// returns a ready-to-use <see cref="WorkDbContext"/>.
    /// </summary>
    public static async Task<WorkDbContext> LoadAsync(string workingDirectory)
    {
        var configPath = WorkDbConfig.FindConfigFile(workingDirectory)
            ?? throw new FileNotFoundException(
                $"No {WorkDbConfig.FileName} found in '{workingDirectory}' or any parent directory. " +
                $"Run 'countrydatatools workdb init --country XX --connection \"...\"' to create one.");

        var config  = WorkDbConfig.Load(configPath);
        var factory = WorkDbConnectionFactory.Create(config);

        var (ok, error) = await factory.TestConnectionAsync();
        if (!ok)
            throw new InvalidOperationException(
                $"Cannot connect to the working database: {error}\n" +
                $"Check workdb.json at: {configPath}");

        return new WorkDbContext(
            config.CountryCode.ToUpperInvariant(),
            config.ActiveRunId,
            Path.GetDirectoryName(configPath)!,
            factory);
    }

    /// <summary>
    /// Creates a <see cref="WorkDbContext"/> directly from a <see cref="WorkDbConfig"/>
    /// without searching for a file. Useful for 'workdb init' and tests.
    /// </summary>
    public static WorkDbContext FromConfig(WorkDbConfig config)
    {
        var factory = WorkDbConnectionFactory.Create(config);
        return new WorkDbContext(
            config.CountryCode.ToUpperInvariant(),
            config.ActiveRunId,
            Directory.GetCurrentDirectory(),
            factory);
    }
}
