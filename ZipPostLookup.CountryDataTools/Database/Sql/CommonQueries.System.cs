namespace ZipPostLookup.CountryDataTools.Database.Sql;

public static partial class CommonQueries
{
    // --- System ---

    public static readonly string GetSchemaVerification =
        @"SELECT name FROM sys.schemas WHERE name IN ('data','codes','pipeline') ORDER BY name";

    /// <summary>
    /// Returns the current-period call count for every tracked API.
    /// Daily APIs: today's row only. Monthly APIs: sum of all rows in the current billing
    /// period (identified by rows where SYSUTCDATETIME() is before ResetTime).
    /// Used to seed the enrichment router's in-memory counters at session start.
    /// </summary>
    public static readonly string GetCurrentPeriodUsage =
        @"-- Daily-type APIs: today's single row
          SELECT ApiName, CallCount
          FROM   [data].[ApiUsage]
          WHERE  MonthlyLimit IS NULL
          AND    UsageDate = CAST(GETUTCDATE() AS DATE)

          UNION ALL

          -- Monthly-type APIs: sum across all rows in the active billing period
          SELECT ApiName, SUM(CallCount) AS CallCount
          FROM   [data].[ApiUsage]
          WHERE  MonthlyLimit IS NOT NULL
          AND    SYSUTCDATETIME() < ResetTime
          GROUP BY ApiName";

    /// <summary>
    /// Atomically increments (or inserts) the call count for an API on today's UTC date.
    /// Limit columns are updated on every call so they stay current if config changes.
    /// ResetTime is set on insert and preserved on update (never moves within a period).
    /// </summary>
    public static readonly string UpsertApiUsage =
        @"MERGE [data].[ApiUsage] AS target
          USING (SELECT @ApiName AS ApiName, CAST(GETUTCDATE() AS DATE) AS UsageDate) AS source
          ON    target.ApiName   = source.ApiName
          AND   target.UsageDate = source.UsageDate
          WHEN MATCHED THEN
              UPDATE SET
                  CallCount    = target.CallCount + 1,
                  DailyLimit   = COALESCE(@DailyLimit,   target.DailyLimit),
                  MonthlyLimit = COALESCE(@MonthlyLimit,  target.MonthlyLimit),
                  ResetTime    = COALESCE(target.ResetTime, @ResetTime),
                  UpdatedAt    = SYSUTCDATETIME()
          WHEN NOT MATCHED THEN
              INSERT (ApiName, UsageDate, CallCount, DailyLimit, MonthlyLimit, ResetTime, UpdatedAt)
              VALUES (source.ApiName, source.UsageDate, 1,
                      @DailyLimit, @MonthlyLimit, @ResetTime, SYSUTCDATETIME());";

    public static readonly string ClearPipelineData =
        @"DELETE ca FROM codes.CandidateAdmins ca
              JOIN codes.Candidate c ON c.CandidateId = ca.CandidateId
              WHERE c.CountryId = @CountryId;
          DELETE FROM pipeline.Decisions  WHERE CountryId = @CountryId;
          DELETE FROM codes.Discrepancies WHERE CountryId = @CountryId;
          DELETE FROM codes.Candidate     WHERE CountryId = @CountryId;
          DELETE FROM pipeline.Runs       WHERE CountryId = @CountryId;";

    public static readonly string ResetCountryData =
        $@"DELETE ca FROM codes.CandidateAdmins ca
                JOIN codes.Candidate c ON c.CandidateId = ca.CandidateId
                WHERE c.CountryId = @CountryId;
            DELETE FROM pipeline.Decisions   WHERE CountryId = @CountryId;
            DELETE FROM codes.Discrepancies  WHERE CountryId = @CountryId;
            DELETE FROM codes.Candidate      WHERE CountryId = @CountryId;
            DELETE FROM pipeline.Runs        WHERE CountryId = @CountryId;
            DELETE ra FROM data.ReferenceAdmins ra
                JOIN data.Reference r ON r.ReferenceId = ra.ReferenceId
                WHERE r.CountryId = @CountryId;
            DELETE FROM data.Reference       WHERE CountryId = @CountryId;
            UPDATE data.CountryInfo
            SET CodeCount      = 0,
                DataCurated    = 0,
                CurationStatus = 'NoData'
            WHERE CountryId = @CountryId;";

    // Run once to add the Flagged column.  Safe to repeat (IF NOT EXISTS guard).
    public static readonly string MigrateAddFlaggedColumn =
        @"IF NOT EXISTS (
              SELECT 1 FROM sys.columns
              WHERE  object_id = OBJECT_ID('data.Reference')
                AND  name      = 'Flagged'
          )
          ALTER TABLE data.Reference ADD Flagged BIT NOT NULL DEFAULT 0;";

    // Run once to add Notes to codes.Discrepancies. Safe to repeat (IF NOT EXISTS guard).
    // Notes carries reviewer hints set at import time — e.g. gold-alias warnings.
    public static readonly string MigrateAddDiscrepancyNotesColumn =
        @"IF NOT EXISTS (
              SELECT 1 FROM sys.columns
              WHERE  object_id = OBJECT_ID('codes.Discrepancies')
                AND  name      = 'Notes'
          )
          ALTER TABLE codes.Discrepancies ADD Notes NVARCHAR(500) NULL;";
}
