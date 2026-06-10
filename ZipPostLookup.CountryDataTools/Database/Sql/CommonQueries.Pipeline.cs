namespace ZipPostLookup.CountryDataTools.Database.Sql;

public static partial class CommonQueries
{
    // --- Runs ---

    public static readonly string CountRunsByPrefix =
        @"SELECT COUNT(*) FROM pipeline.Runs WHERE RunId LIKE @PrefixWildcard";

    public static readonly string CheckRunExists =
        @"SELECT COUNT(*) FROM pipeline.Runs WHERE RunId = @RunId";

    public static readonly string CheckRunExistsForCountry =
        @"SELECT COUNT(*) FROM pipeline.Runs WHERE RunId = @RunId AND CountryId = @CountryId";

    public static readonly string GetLatestRun =
        @"SELECT TOP 1
                RunId,
                CountryId,
                SourceFilename,
                StartedAt,
                CompletedAt,
                Status,
                Notes
              FROM pipeline.Runs
              WHERE CountryId = @CountryId
              ORDER BY StartedAt DESC";

    public static readonly string GetAllRuns =
        @"SELECT
                RunId,
                CountryId,
                SourceFilename,
                StartedAt,
                CompletedAt,
                Status,
                Notes
              FROM pipeline.Runs
              WHERE CountryId = @CountryId
              ORDER BY StartedAt DESC";
}
