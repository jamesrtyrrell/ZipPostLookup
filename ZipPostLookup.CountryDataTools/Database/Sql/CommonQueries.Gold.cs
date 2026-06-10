namespace ZipPostLookup.CountryDataTools.Database.Sql;

public static partial class CommonQueries
{
    // =========================================================================
    // Gold code queries
    // =========================================================================

    // Run once to create the GoldCode table. Safe to repeat (IF NOT EXISTS guard).
    public static readonly string MigrateAddGoldCodeTable =
        @"IF NOT EXISTS (SELECT 1 FROM sys.tables
                        WHERE  schema_id = SCHEMA_ID('data') AND name = 'GoldCode')
          BEGIN
              CREATE TABLE [data].[GoldCode] (
                  [CountryId]     [nvarchar](2)       NOT NULL,
                  [ZpCode]        [nvarchar](20)      NOT NULL,
                  [GoldAt]        [datetimeoffset](7) NOT NULL
                                  CONSTRAINT [DF_GoldCode_GoldAt] DEFAULT (SYSUTCDATETIME()),
                  [ChecksVersion] [int]               NOT NULL
                                  CONSTRAINT [DF_GoldCode_ChecksVersion] DEFAULT (1),
                  CONSTRAINT [PK_GoldCode] PRIMARY KEY CLUSTERED ([CountryId] ASC, [ZpCode] ASC),
                  CONSTRAINT [FK_GoldCode_Country] FOREIGN KEY ([CountryId])
                      REFERENCES [data].[CountryInfo] ([CountryId])
              );
              PRINT 'data.GoldCode: table created.';
          END";

    // Returns NULL when the code is eligible for gold, or a reason string when it is not.
    // Conditions: curated default row exists; all curated rows have admin1; all have timezone; at least one has lat/lng.
    public static readonly string CheckGoldEligibility =
        @"SELECT CASE
              WHEN NOT EXISTS (
                  SELECT 1 FROM data.Reference
                  WHERE CountryId = @CountryId AND ZpCode = @ZpCode
                    AND Curated = 1 AND Flagged = 0 AND IsDefault = 1
              ) THEN 'no curated default row'
              WHEN EXISTS (
                  SELECT 1 FROM data.Reference r
                  LEFT JOIN data.ReferenceAdmins ra
                      ON  ra.ReferenceId  = r.ReferenceId
                      AND ra.AdminLevelId = (SELECT MIN(AdminLevelId) FROM data.AdminLevels
                                             WHERE CountryId = r.CountryId AND LevelNumber = 1)
                  WHERE r.CountryId = @CountryId AND r.ZpCode = @ZpCode
                    AND r.Curated = 1 AND r.Flagged = 0
                    AND (ra.Code IS NULL OR ra.Code IN ('', '---')
                         OR ra.Code NOT LIKE '%[^0-9]%')
              ) THEN 'missing admin1 code'
              WHEN EXISTS (
                  SELECT 1 FROM data.Reference
                  WHERE CountryId = @CountryId AND ZpCode = @ZpCode
                    AND Curated = 1 AND Flagged = 0
                    AND (Timezone IS NULL OR Timezone IN ('', '---'))
              ) THEN 'missing timezone'
              WHEN NOT EXISTS (
                  SELECT 1 FROM data.Reference
                  WHERE CountryId = @CountryId AND ZpCode = @ZpCode
                    AND Curated = 1 AND Flagged = 0
                    AND Lat NOT IN ('---', '') AND Lat IS NOT NULL
                    AND Lng NOT IN ('---', '') AND Lng IS NOT NULL
              ) THEN 'missing lat/lng'
              ELSE NULL
          END";

    // Insert a ZpCode into the gold set if it is not already there.
    public static readonly string SetGoldCode =
        @"IF NOT EXISTS (SELECT 1 FROM data.GoldCode
                        WHERE CountryId = @CountryId AND ZpCode = @ZpCode)
              INSERT INTO data.GoldCode (CountryId, ZpCode, ChecksVersion)
              VALUES (@CountryId, @ZpCode, @ChecksVersion)";

    // Remove a ZpCode from the gold set.
    public static readonly string RevokeGoldCode =
        @"DELETE FROM data.GoldCode
          WHERE CountryId = @CountryId AND ZpCode = @ZpCode";

    // Count of gold-certified codes for a country.
    public static readonly string GetGoldCodeCount =
        @"SELECT COUNT(*) FROM data.GoldCode WHERE CountryId = @CountryId";

    // Gold codes that no longer satisfy all gold conditions (integrity check 11).
    // Returns ZpCode for any gold code that has regressed since certification.
    public static readonly string GetGoldCodesFailingConditions =
        @"SELECT g.ZpCode
          FROM   data.GoldCode g
          WHERE  g.CountryId = @CountryId
            AND (
                -- Lost its curated default row
                NOT EXISTS (
                    SELECT 1 FROM data.Reference r
                    WHERE  r.CountryId = g.CountryId AND r.ZpCode = g.ZpCode
                      AND  r.Curated = 1 AND r.Flagged = 0 AND r.IsDefault = 1
                )
                -- A curated row is now missing admin1
                OR EXISTS (
                    SELECT 1 FROM data.Reference r
                    LEFT JOIN data.ReferenceAdmins ra
                        ON  ra.ReferenceId  = r.ReferenceId
                        AND ra.AdminLevelId = (SELECT MIN(AdminLevelId) FROM data.AdminLevels
                                               WHERE CountryId = r.CountryId AND LevelNumber = 1)
                    WHERE r.CountryId = g.CountryId AND r.ZpCode = g.ZpCode
                      AND r.Curated = 1 AND r.Flagged = 0
                      AND (ra.Code IS NULL OR ra.Code IN ('', '---')
                           OR ra.Code NOT LIKE '%[^0-9]%')
                )
                -- A curated row is now missing timezone
                OR EXISTS (
                    SELECT 1 FROM data.Reference
                    WHERE CountryId = g.CountryId AND ZpCode = g.ZpCode
                      AND Curated = 1 AND Flagged = 0
                      AND (Timezone IS NULL OR Timezone IN ('', '---'))
                )
                -- No curated row has coordinates
                OR NOT EXISTS (
                    SELECT 1 FROM data.Reference
                    WHERE CountryId = g.CountryId AND ZpCode = g.ZpCode
                      AND Curated = 1 AND Flagged = 0
                      AND Lat NOT IN ('---', '') AND Lat IS NOT NULL
                      AND Lng NOT IN ('---', '') AND Lng IS NOT NULL
                )
            )
          ORDER BY g.ZpCode";

    // Remove all gold certifications for a country (used before a force-reimport of reference data).
    public static readonly string RevokeAllGoldCodesForCountry =
        @"DELETE FROM data.GoldCode WHERE CountryId = @CountryId";

    // Bulk-certify all eligible codes for a country in one pass.
    // Inserts into data.GoldCode every distinct ZpCode that passes all four gold conditions
    // and is not already present. Safe to re-run — duplicate-protected by NOT EXISTS guard.
    // Pass @CountryId = 'US' / 'CA' / 'MX'. Returns the number of rows inserted.
    public static readonly string BulkCertifyGoldCode =
        @"INSERT INTO data.GoldCode (CountryId, ZpCode, ChecksVersion)
          SELECT DISTINCT r.CountryId, r.ZpCode, 1
          FROM   data.Reference r
          WHERE  r.CountryId = @CountryId
            AND  r.Curated   = 1
            AND  r.Flagged   = 0
            -- must have a curated default row
            AND  EXISTS (
                SELECT 1 FROM data.Reference
                WHERE  CountryId = r.CountryId AND ZpCode = r.ZpCode
                  AND  Curated = 1 AND Flagged = 0 AND IsDefault = 1
            )
            -- no curated row may have a missing/invalid admin1
            AND  NOT EXISTS (
                SELECT 1 FROM data.Reference r2
                LEFT JOIN data.ReferenceAdmins ra
                    ON  ra.ReferenceId  = r2.ReferenceId
                    AND ra.AdminLevelId = (SELECT MIN(AdminLevelId) FROM data.AdminLevels
                                           WHERE CountryId = r2.CountryId AND LevelNumber = 1)
                WHERE  r2.CountryId = r.CountryId AND r2.ZpCode = r.ZpCode
                  AND  r2.Curated = 1 AND r2.Flagged = 0
                  AND  (ra.Code IS NULL OR ra.Code IN ('', '---')
                        OR ra.Code NOT LIKE '%[^0-9]%')
            )
            -- no curated row may have a missing timezone
            AND  NOT EXISTS (
                SELECT 1 FROM data.Reference
                WHERE  CountryId = r.CountryId AND ZpCode = r.ZpCode
                  AND  Curated = 1 AND Flagged = 0
                  AND  (Timezone IS NULL OR Timezone IN ('', '---'))
            )
            -- at least one curated row must have lat/lng
            AND  EXISTS (
                SELECT 1 FROM data.Reference
                WHERE  CountryId = r.CountryId AND ZpCode = r.ZpCode
                  AND  Curated = 1 AND Flagged = 0
                  AND  Lat NOT IN ('---', '') AND Lat IS NOT NULL
                  AND  Lng NOT IN ('---', '') AND Lng IS NOT NULL
            )
            -- not already certified
            AND  NOT EXISTS (
                SELECT 1 FROM data.GoldCode g
                WHERE  g.CountryId = r.CountryId AND g.ZpCode = r.ZpCode
            )";
}
