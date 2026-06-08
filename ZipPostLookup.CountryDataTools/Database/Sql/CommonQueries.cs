
using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Database.Sql;

public static class CommonQueries
{
    // --- Country Info ---

    public static readonly string GetCountryInfoById =
        @"SELECT * FROM data.CountryInfo WHERE CountryId = @CountryId";
    
    public static readonly string ResetCountryCodeCount =
        @"UPDATE data.CountryInfo SET CodeCount = 0 WHERE CountryId = @CountryId";

    // --- Reference ---

    public static readonly string HasReferenceData =
        @"SELECT COUNT(*) FROM data.Reference WHERE CountryId = @CountryId";

    public static readonly string GetAdminLevels =
        @"SELECT LevelNumber, AdminLevelId FROM data.AdminLevels WHERE CountryId = @CountryId";

    public static readonly string GetAdminLevelNames =
        @"SELECT LevelName FROM data.AdminLevels
          WHERE CountryId = @CountryId ORDER BY LevelNumber";

    public static readonly string GetAdminLevelIdByLevel =
        @"SELECT AdminLevelId FROM data.AdminLevels WHERE CountryId = @CountryId AND LevelNumber = 1";

    public static readonly string GetReferenceByCode =
        @"SELECT * FROM data.Reference WHERE CountryId = @CountryId AND ZpCode = @ZpCode";

    public static readonly string GetReferenceByCodeAndName =
        @"SELECT * FROM data.Reference WHERE CountryId = @CountryId AND ZpCode = @ZpCode AND PlaceName = @PlaceName";

    public static readonly string GetReferenceForImportBatch =
        @"SELECT r.ReferenceId, r.ZpCode, r.PlaceName, r.Timezone,
                 CAST(r.IsDefault       AS BIT) AS IsDefault,
                 CAST(r.TimezoneChecked AS BIT) AS TimezoneChecked,
                 CAST(r.Curated         AS BIT) AS Curated,
                 CAST(r.Flagged         AS BIT) AS Flagged,
                 ISNULL(r.Lat, '---') AS Lat, ISNULL(r.Lng, '---') AS Lng,
                 r.AltNameOf
            FROM data.Reference r
            INNER JOIN @Codes c ON c.Code = r.ZpCode
            WHERE r.CountryId = @CountryId;";

    public static readonly string GetReferenceStateByCodes =
        @"SELECT ZpCode, PlaceName,
                 CAST(TimezoneChecked AS BIT) AS TimezoneChecked,
                 CAST(NameChecked     AS BIT) AS NameChecked
          FROM data.Reference
          WHERE CountryId = @CountryId AND ZpCode IN @Codes";

    public static readonly string GetAllReferenceDataWithAdmins =
        @"SELECT * FROM data.Reference RData
            INNER JOIN data.ReferenceAdmins RAdmins ON RData.ReferenceId = RAdmins.ReferenceId
            WHERE CountryId = @CountryId AND Curated = 1";

    public static readonly string CountReferenceByCode =
        @"SELECT COUNT(*) FROM data.Reference WHERE CountryId = @CountryId AND ZpCode = @ZpCode";

    public static readonly string RenameZpCode =
        @"UPDATE data.Reference SET ZpCode = @NewCode WHERE ReferenceId = @ReferenceId";

    // Returns one row per distinct ZpCode that is not yet curated (TimezoneChecked=0 OR NameChecked=0).
    // Admin1Code is taken from the level-1 admin of the first reference row for that code.
    // AltNameOf rows are excluded — they share curation state with their canonical row.
    // Flagged codes are excluded — they are not valid and should not be enriched.
    public static readonly string GetUncuratedReferenceCodes =
        @"SELECT r.ZpCode,
                 MAX(ISNULL(ra.Code, '')) AS Admin1Code
          FROM   data.Reference r
          LEFT   JOIN data.ReferenceAdmins ra
                 ON  ra.ReferenceId  = r.ReferenceId
                 AND ra.AdminLevelId = (SELECT MIN(AdminLevelId) FROM data.AdminLevels
                                        WHERE CountryId = r.CountryId AND LevelNumber = 1)
          WHERE  r.CountryId = @CountryId
            AND  r.Curated   = 0
            AND  r.AltNameOf IS NULL
            AND  r.Flagged   = 0
          GROUP  BY r.ZpCode
          ORDER  BY r.ZpCode";

    public static readonly string DeleteReferenceData =
        @"DELETE FROM data.Reference WHERE CountryId = @CountryId";

    public static readonly string GetDistinctCodeCount =
        @"SELECT COUNT(DISTINCT ZpCode)
          FROM   data.Reference
          WHERE  CountryId = @CountryId AND Curated = 1";

    // Curation progress for ALL countries in one query — for the side-by-side stats panel.
    public static readonly string GetAllCountryCurationStats =
        @"SELECT
              CountryId,
              SUM(CAST(TimezoneChecked AS INT))                                      AS TotalTimezoneChecked,
              SUM(CAST(NameChecked     AS INT))                                      AS TotalNameChecked,
              SUM(CAST(Curated         AS INT))                                      AS TotalCurated,
              COUNT(*)                                                               AS Total,
              SUM(CAST(TimezoneChecked AS INT) - CAST(Curated AS INT))               AS Remaining,
              CAST(SUM(CAST(Curated AS INT)) * 100.0 / NULLIF(COUNT(*), 0)
                   AS DECIMAL(5,2))                                                  AS PctComplete
          FROM data.Reference
          WHERE flagged = 0 
          GROUP BY CountryId
          ORDER BY CountryId";

    // Curation progress per country — mirrors the ad-hoc query used in SSMS.
    // Remaining = rows where TZ is checked but name is not (the typical in-progress state).
    public static readonly string GetCurationStats =
        @"SELECT
              SUM(CAST(TimezoneChecked AS INT))                                      AS TotalTimezoneChecked,
              SUM(CAST(NameChecked     AS INT))                                      AS TotalNameChecked,
              SUM(CAST(Curated         AS INT))                                      AS TotalCurated,
              COUNT(*)                                                               AS Total,
              SUM(CAST(TimezoneChecked AS INT) - CAST(Curated AS INT))               AS Remaining,
              CAST(SUM(CAST(Curated AS INT)) * 100.0 / NULLIF(COUNT(*), 0)
                   AS DECIMAL(5,2))                                                  AS PctComplete
          FROM data.Reference
          WHERE CountryId = @CountryId";

    // Count of distinct uncurated, non-flagged codes (for paginator denominator).
    public static readonly string GetUncuratedCodeCount =
        @"SELECT COUNT(DISTINCT ZpCode)
          FROM   data.Reference
          WHERE  CountryId = @CountryId AND Curated = 0 AND AltNameOf IS NULL AND Flagged = 0";

    // One row per uncurated ZpCode with the default entry's name/timezone and aggregate curation flags.
    // MIN(TimezoneChecked) = 1 iff ALL rows for that code are TZ-checked (same for NameChecked).
    public static readonly string GetUncuratedCodesPage =
        @"SELECT r.ZpCode,
                 COALESCE(MAX(CASE WHEN r.IsDefault = 1 THEN r.PlaceName END),
                          MAX(r.PlaceName))                         AS PlaceName,
                 COALESCE(MAX(CASE WHEN r.IsDefault = 1 THEN r.Timezone END),
                          MAX(r.Timezone))                          AS Timezone,
                 CAST(MIN(CAST(r.TimezoneChecked AS INT)) AS BIT)   AS TimezoneChecked,
                 CAST(MIN(CAST(r.NameChecked     AS INT)) AS BIT)   AS NameChecked,
                 MAX(ISNULL(ra.Value, '---'))                       AS Admin1,
                 MAX(ISNULL(ra.Code,  '---'))                       AS Admin1Code,
                 COUNT(*)                                           AS NameCount
          FROM   data.Reference r
          LEFT   JOIN data.ReferenceAdmins ra
                 ON  ra.ReferenceId  = r.ReferenceId
                 AND ra.AdminLevelId = (SELECT MIN(AdminLevelId) FROM data.AdminLevels
                                         WHERE CountryId = r.CountryId AND LevelNumber = 1)
          WHERE  r.CountryId = @CountryId
            AND  r.Curated   = 0
            AND  r.AltNameOf IS NULL
          GROUP  BY r.ZpCode
          ORDER  BY r.ZpCode
          OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

    // All rows for a specific code with admin data and curation flags (for the editor detail view).
    public static readonly string GetReferenceRowsByCode =
        @"SELECT r.ZpCode, r.PlaceName, r.Timezone,
                 CAST(r.IsDefault       AS BIT) AS IsDefault,
                 ISNULL(ra.Value, '---')        AS Admin1,
                 ISNULL(ra.Code,  '---')        AS Admin1Code,
                 CAST(r.TimezoneChecked AS BIT) AS TimezoneChecked,
                 CAST(r.NameChecked     AS BIT) AS NameChecked,
                 r.AltNameOf
          FROM data.Reference r
          LEFT JOIN data.ReferenceAdmins ra
              ON  ra.ReferenceId  = r.ReferenceId
              AND ra.AdminLevelId = (SELECT MIN(AdminLevelId) FROM data.AdminLevels
                                     WHERE CountryId = r.CountryId AND LevelNumber = 1)
          WHERE r.CountryId = @CountryId
            AND r.ZpCode    = @ZpCode
          ORDER BY r.IsDefault DESC, r.PlaceName";

    // Extended detail rows including Lat/Lng, both admin levels, and Flagged.
    // Used by the ZpCode editor detail and edit screens.
    public static readonly string GetCodeDetailRows =
        @"SELECT r.ReferenceId, r.ZpCode, r.PlaceName, r.Timezone,
                 CAST(r.IsDefault       AS BIT) AS IsDefault,
                 ISNULL(r.Lat,  '---')          AS Lat,
                 ISNULL(r.Lng,  '---')          AS Lng,
                 r.AltNameOf,
                 ISNULL(ra1.Value, '---')        AS Admin1,
                 ISNULL(ra1.Code,  '---')        AS Admin1Code,
                 ISNULL(ra2.Value, '---')        AS Admin2,
                 ISNULL(ra2.Code,  '---')        AS Admin2Code,
                 CAST(r.TimezoneChecked AS BIT)  AS TimezoneChecked,
                 CAST(r.NameChecked     AS BIT)  AS NameChecked,
                 CAST(r.Flagged         AS BIT)  AS Flagged
          FROM data.Reference r
          LEFT JOIN data.ReferenceAdmins ra1
              ON  ra1.ReferenceId  = r.ReferenceId
              AND ra1.AdminLevelId = (SELECT MIN(AdminLevelId) FROM data.AdminLevels
                                      WHERE CountryId = r.CountryId AND LevelNumber = 1)
          LEFT JOIN data.ReferenceAdmins ra2
              ON  ra2.ReferenceId  = r.ReferenceId
              AND ra2.AdminLevelId = (SELECT MIN(AdminLevelId) FROM data.AdminLevels
                                      WHERE CountryId = r.CountryId AND LevelNumber = 2)
          WHERE r.CountryId = @CountryId
            AND r.ZpCode    = @ZpCode
          ORDER BY r.IsDefault DESC, r.PlaceName";

    // Reload a single detail row after an in-place edit.
    public static readonly string GetCodeDetailRowById =
        @"SELECT r.ReferenceId, r.ZpCode, r.PlaceName, r.Timezone,
                 CAST(r.IsDefault       AS BIT) AS IsDefault,
                 ISNULL(r.Lat,  '---')          AS Lat,
                 ISNULL(r.Lng,  '---')          AS Lng,
                 r.AltNameOf,
                 ISNULL(ra1.Value, '---')        AS Admin1,
                 ISNULL(ra1.Code,  '---')        AS Admin1Code,
                 ISNULL(ra2.Value, '---')        AS Admin2,
                 ISNULL(ra2.Code,  '---')        AS Admin2Code,
                 CAST(r.TimezoneChecked AS BIT)  AS TimezoneChecked,
                 CAST(r.NameChecked     AS BIT)  AS NameChecked,
                 CAST(r.Flagged         AS BIT)  AS Flagged
          FROM data.Reference r
          LEFT JOIN data.ReferenceAdmins ra1
              ON  ra1.ReferenceId  = r.ReferenceId
              AND ra1.AdminLevelId = (SELECT MIN(AdminLevelId) FROM data.AdminLevels
                                      WHERE CountryId = r.CountryId AND LevelNumber = 1)
          LEFT JOIN data.ReferenceAdmins ra2
              ON  ra2.ReferenceId  = r.ReferenceId
              AND ra2.AdminLevelId = (SELECT MIN(AdminLevelId) FROM data.AdminLevels
                                      WHERE CountryId = r.CountryId AND LevelNumber = 2)
          WHERE r.ReferenceId = @ReferenceId";

    // Individual reference rows for the browse screen (all uncurated, including AltNameOf rows).
    public static readonly string GetBrowseRowsPage =
        @"SELECT r.ZpCode, r.PlaceName, r.Timezone,
                 CAST(r.IsDefault       AS BIT) AS IsDefault,
                 ISNULL(r.Lat,  '---')          AS Lat,
                 ISNULL(r.Lng,  '---')          AS Lng,
                 r.AltNameOf,
                 ISNULL(ra.Value, '---')         AS Admin1,
                 ISNULL(ra.Code,  '---')         AS Admin1Code,
                 CAST(r.TimezoneChecked AS BIT)  AS TimezoneChecked,
                 CAST(r.NameChecked     AS BIT)  AS NameChecked,
                 CAST(r.Flagged         AS BIT)  AS Flagged
          FROM data.Reference r
          LEFT JOIN data.ReferenceAdmins ra
              ON  ra.ReferenceId  = r.ReferenceId
              AND ra.AdminLevelId = (SELECT MIN(AdminLevelId) FROM data.AdminLevels
                                     WHERE CountryId = r.CountryId AND LevelNumber = 1)
          WHERE r.CountryId = @CountryId
            AND r.Curated   = 0
            AND r.Flagged   = 0
          ORDER BY r.ZpCode, r.IsDefault DESC, r.PlaceName
          OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

    // Row count for browse pagination (uncurated, non-flagged rows).
    public static readonly string GetBrowseRowCount =
        @"SELECT COUNT(*)
          FROM   data.Reference
          WHERE  CountryId = @CountryId AND Curated = 0 AND Flagged = 0";

    // Flagged browse — mirrors the above pair but filtered to Flagged = 1.
    public static readonly string GetFlaggedBrowseRowsPage =
        @"SELECT r.ZpCode, r.PlaceName, r.Timezone,
                 CAST(r.IsDefault       AS BIT) AS IsDefault,
                 ISNULL(r.Lat,  '---')          AS Lat,
                 ISNULL(r.Lng,  '---')          AS Lng,
                 r.AltNameOf,
                 ISNULL(ra.Value, '---')         AS Admin1,
                 ISNULL(ra.Code,  '---')         AS Admin1Code,
                 CAST(r.TimezoneChecked AS BIT)  AS TimezoneChecked,
                 CAST(r.NameChecked     AS BIT)  AS NameChecked,
                 CAST(r.Flagged         AS BIT)  AS Flagged
          FROM data.Reference r
          LEFT JOIN data.ReferenceAdmins ra
              ON  ra.ReferenceId  = r.ReferenceId
              AND ra.AdminLevelId = (SELECT MIN(AdminLevelId) FROM data.AdminLevels
                                     WHERE CountryId = r.CountryId AND LevelNumber = 1)
          WHERE r.CountryId = @CountryId
            AND r.Flagged   = 1
          ORDER BY r.ZpCode, r.IsDefault DESC, r.PlaceName
          OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

    public static readonly string GetFlaggedBrowseRowCount =
        @"SELECT COUNT(*)
          FROM   data.Reference
          WHERE  CountryId = @CountryId AND Flagged = 1";

    // Flag / unflag all rows for a ZpCode — flagged codes are excluded from exports.
    public static readonly string FlagCode =
        @"UPDATE data.Reference
          SET    Flagged   = 1,
                 UpdatedAt = SYSUTCDATETIME()
          WHERE  CountryId = @CountryId AND ZpCode = @ZpCode";

    public static readonly string UnflagCode =
        @"UPDATE data.Reference
          SET    Flagged   = 0,
                 UpdatedAt = SYSUTCDATETIME()
          WHERE  CountryId = @CountryId AND ZpCode = @ZpCode";

    // Per-field in-place updates for the row editor (target a single ReferenceId).
    public static readonly string UpdateReferenceTimezoneById =
        @"UPDATE data.Reference
          SET    Timezone  = @Timezone,
                 UpdatedAt = SYSUTCDATETIME()
          WHERE  ReferenceId = @ReferenceId";

    public static readonly string UpdateReferencePlaceNameById =
        @"UPDATE data.Reference
          SET    PlaceName = @PlaceName,
                 UpdatedAt = SYSUTCDATETIME()
          WHERE  ReferenceId = @ReferenceId";

    public static readonly string UpdateReferenceLatById =
        @"UPDATE data.Reference
          SET    Lat       = @Lat,
                 UpdatedAt = SYSUTCDATETIME()
          WHERE  ReferenceId = @ReferenceId";

    public static readonly string UpdateReferenceLngById =
        @"UPDATE data.Reference
          SET    Lng       = @Lng,
                 UpdatedAt = SYSUTCDATETIME()
          WHERE  ReferenceId = @ReferenceId";

    public static readonly string UpdateReferenceAltNameOfById =
        @"UPDATE data.Reference
          SET    AltNameOf = @AltNameOf,
                 UpdatedAt = SYSUTCDATETIME()
          WHERE  ReferenceId = @ReferenceId";

    // Reference rows that have no admin level 1 entry, a blank/placeholder entry,
    // or an all-numeric entry (INEGI-format codes like "19" that should be SEPOMEX
    // abbreviations like "NLE"). All-numeric codes are treated as wrong-format and
    // will be overwritten by normalize-admins with the correct abbreviation.
    public static readonly string GetReferencesMissingAdmin1 =
        @"SELECT r.ReferenceId, r.ZpCode
          FROM   data.Reference r
          LEFT JOIN data.ReferenceAdmins ra
                 ON  ra.ReferenceId  = r.ReferenceId
                 AND ra.AdminLevelId = (SELECT TOP 1 AdminLevelId FROM data.AdminLevels
                                        WHERE CountryId = r.CountryId AND LevelNumber = 1)
          WHERE  r.CountryId = @CountryId
            AND (   ra.ReferenceId IS NULL
                 OR ra.Code = '---'
                 OR ra.Code = ''
                 OR ra.Code NOT LIKE '%[^0-9]%')";

    // Rows that already have an alphabetic admin1 code set — used by the reconcile
    // pass in normalize-admins to correct codes that are wrong but not "missing".
    public static readonly string GetExistingAdmin1ForReconcile =
        @"SELECT r.ReferenceId, r.ZpCode, ra.Code AS StoredCode
          FROM   data.Reference r
          JOIN   data.ReferenceAdmins ra
                 ON  ra.ReferenceId  = r.ReferenceId
                 AND ra.AdminLevelId = (SELECT TOP 1 AdminLevelId FROM data.AdminLevels
                                        WHERE CountryId = r.CountryId AND LevelNumber = 1)
          WHERE  r.CountryId = @CountryId
            AND  ra.Code IS NOT NULL
            AND  ra.Code != ''
            AND  ra.Code != '---'
            AND  ra.Code LIKE '%[^0-9]%'";

    public static readonly string UpsertReferenceAdmin =
        @"MERGE data.ReferenceAdmins AS t
          USING (SELECT @ReferenceId AS ReferenceId, @AdminLevelId AS AdminLevelId) AS s
              ON t.ReferenceId  = s.ReferenceId
             AND t.AdminLevelId = s.AdminLevelId
          WHEN MATCHED THEN
              UPDATE SET Value = @Value, Code = @Code
          WHEN NOT MATCHED THEN
              INSERT (ReferenceId, AdminLevelId, Value, Code, CreatedAt)
              VALUES (@ReferenceId, @AdminLevelId, @Value, @Code, SYSUTCDATETIME());";

    // Look up the AdminLevelId PK for a given country + level number.
    public static readonly string GetAdminLevelIdForLevel =
        @"SELECT TOP 1 AdminLevelId
          FROM   data.AdminLevels
          WHERE  CountryId   = @CountryId
            AND  LevelNumber = @LevelNumber";

    // Run once to add the Flagged column.  Safe to repeat (IF NOT EXISTS guard).
    public static readonly string MigrateAddFlaggedColumn =
        @"IF NOT EXISTS (
              SELECT 1 FROM sys.columns
              WHERE  object_id = OBJECT_ID('data.Reference')
                AND  name      = 'Flagged'
          )
          ALTER TABLE data.Reference ADD Flagged BIT NOT NULL DEFAULT 0;";

    // Set both curation flags for every row sharing a ZpCode.
    // TimezoneChecked is only raised to 1 when the row actually has a timezone value —
    // rows with a blank/placeholder timezone are left with their current TimezoneChecked
    // state so they surface in the "checked but blank" integrity check.
    public static readonly string MarkCodeAsCurated =
        @"UPDATE data.Reference
          SET    TimezoneChecked = CASE
                     WHEN Timezone IS NOT NULL
                      AND Timezone NOT IN ('', '---') THEN 1
                     ELSE TimezoneChecked
                 END,
                 NameChecked     = 1,
                 UpdatedAt       = SYSUTCDATETIME()
          WHERE  CountryId = @CountryId
            AND  ZpCode    = @ZpCode";

    // Only raises TimezoneChecked when the row has an actual timezone value.
    public static readonly string MarkCodeTimezoneChecked =
        @"UPDATE data.Reference
          SET    TimezoneChecked = 1,
                 UpdatedAt       = SYSUTCDATETIME()
          WHERE  CountryId  = @CountryId
            AND  ZpCode     = @ZpCode
            AND  Timezone   IS NOT NULL
            AND  Timezone   NOT IN ('', '---')";

    public static readonly string MarkCodeNameChecked =
        @"UPDATE data.Reference
          SET    NameChecked = 1,
                 UpdatedAt   = SYSUTCDATETIME()
          WHERE  CountryId = @CountryId
            AND  ZpCode    = @ZpCode";

    // Alt-name rows whose canonical code is curated but they themselves are not.
    // These are "orphaned" — they should have been propagated when the canonical was curated.
    public static readonly string GetOrphanAltNameCount =
        @"SELECT COUNT(*)
          FROM   data.Reference alt
          WHERE  alt.CountryId = @CountryId
            AND  alt.AltNameOf IS NOT NULL
            AND  alt.Curated   = 0
            AND  EXISTS (
                SELECT 1 FROM data.Reference canon
                WHERE  canon.CountryId = alt.CountryId
                  AND  canon.ZpCode    = alt.ZpCode
                  AND  canon.AltNameOf IS NULL
                  AND  canon.Curated   = 1
            )";

    // For each ZpCode that has more than one IsDefault=1 row, demote all but the
    // "winner" to IsDefault=0. Winner priority: non-AltNameOf row first, then
    // lowest ReferenceId (original import order) as a deterministic tie-breaker.
    // Codes with exactly one IsDefault=1 row are untouched.
    public static readonly string FixDuplicateIsDefaults =
        @"WITH Winners AS (
              SELECT ReferenceId
              FROM (
                  SELECT ReferenceId,
                         ROW_NUMBER() OVER (
                             PARTITION BY ZpCode
                             ORDER BY CASE WHEN AltNameOf IS NULL THEN 0 ELSE 1 END,
                                      ReferenceId
                         ) AS Rn
                  FROM   data.Reference
                  WHERE  CountryId = @CountryId
                    AND  IsDefault = 1
              ) t
              WHERE Rn = 1
          )
          UPDATE data.Reference
          SET    IsDefault = 0,
                 UpdatedAt = SYSUTCDATETIME()
          WHERE  CountryId  = @CountryId
            AND  IsDefault  = 1
            AND  ReferenceId NOT IN (SELECT ReferenceId FROM Winners)";

    // Mark all orphaned alt-name rows as curated (propagate from their canonical row).
    public static readonly string FixOrphanAltNames =
        @"UPDATE alt
          SET    alt.TimezoneChecked = 1,
                 alt.NameChecked     = 1,
                 alt.UpdatedAt       = SYSUTCDATETIME()
          FROM   data.Reference alt
          WHERE  alt.CountryId = @CountryId
            AND  alt.AltNameOf IS NOT NULL
            AND  alt.Curated   = 0
            AND  EXISTS (
                SELECT 1 FROM data.Reference canon
                WHERE  canon.CountryId = alt.CountryId
                  AND  canon.ZpCode    = alt.ZpCode
                  AND  canon.AltNameOf IS NULL
                  AND  canon.Curated   = 1
            )";

    // Update timezone and mark TZ-checked for every row of a ZpCode.
    public static readonly string UpdateCodeTimezone =
        @"UPDATE data.Reference
          SET    Timezone        = @Timezone,
                 TimezoneChecked = 1,
                 UpdatedAt       = SYSUTCDATETIME()
          WHERE  CountryId = @CountryId
            AND  ZpCode    = @ZpCode";

    public static readonly string GetAdminDivisionCounts =
        @"SELECT ra.Code AS AdminCode,
                 COUNT(*)                  AS NameCount,
                 COUNT(DISTINCT r.ZpCode)  AS ZipCount
          FROM   data.Reference r
          JOIN   data.ReferenceAdmins ra ON ra.ReferenceId = r.ReferenceId
          WHERE  r.CountryId = @CountryId AND r.Curated = 1
          GROUP  BY ra.Code";

    public static readonly string GetDistinctTimezoneCount =
        @"SELECT COUNT(DISTINCT Timezone)
          FROM   data.Reference
          WHERE  CountryId = @CountryId
            AND  AltNameOf IS NULL
            AND  Timezone  IS NOT NULL
            AND  Timezone  NOT IN ('---', '')";

    public static readonly string GetCuratedReferenceCount =
        @"SELECT COUNT(*) FROM data.reference WHERE CountryId = @CountryId AND Curated = 1";

    // Returns rows that have valid coordinates but either:
    //   (a) TimezoneChecked = 0 — never verified, or
    //   (b) TimezoneChecked = 1 but Timezone is empty/--- (marked checked without
    //       actually setting the timezone — data quality bug).
    // Used by normalize-tz to resolve IANA timezone from lat/lng.
    public static readonly string GetUnverifiedWithCoords =
        @"SELECT ReferenceId, ZpCode, Lat, Lng, Timezone
          FROM data.Reference
          WHERE CountryId = @CountryId
            AND Lat NOT IN ('---', '') AND Lat  IS NOT NULL
            AND Lng NOT IN ('---', '') AND Lng  IS NOT NULL
            AND (TimezoneChecked = 0
              OR Timezone IS NULL
              OR Timezone = ''
              OR Timezone = '---')";

    public static readonly string UpdateReferenceNameChecked =
        @"UPDATE data.Reference
              SET    NameChecked = 1,
                     UpdatedAt   = SYSUTCDATETIME()
              WHERE  CountryId       = @CountryId
                AND  ZpCode          = @ZpCode
                AND  TimezoneChecked = 1
                AND  NameChecked     = 0";

    public static readonly string GetReferenceAdmin =
        @"SELECT * FROM data.ReferenceAdmins
            WHERE ReferenceId = @ReferenceId AND AdminLevelId = @AdminLevelId";

    public static readonly string ExportReferenceData =
        @"SELECT
                r.ZpCode,
                r.PlaceName,
                r.Timezone,
                CAST(r.IsDefault AS BIT)    AS IsDefault,
                ISNULL(r.Lat, '---')        AS Lat,
                ISNULL(r.Lng, '---')        AS Lng,
                ISNULL(ra.Value, '---')     AS Admin1,
                ISNULL(ra.Code,  '---')     AS Admin1Code
            FROM data.Reference r
            LEFT JOIN data.ReferenceAdmins ra
                ON  ra.ReferenceId  = r.ReferenceId
                AND ra.AdminLevelId = (
                    SELECT MIN(AdminLevelId) FROM data.AdminLevels
                    WHERE CountryId = r.CountryId AND LevelNumber = 1)
            WHERE r.CountryId = @CountryId
              AND r.AltNameOf IS NULL
            ORDER BY r.ZpCode, r.IsDefault DESC, r.PlaceName";

    public static readonly string ExportReferenceDataCuratedOnly =
        @"SELECT
                r.ZpCode,
                r.PlaceName,
                r.Timezone,
                CAST(r.IsDefault AS BIT)    AS IsDefault,
                ISNULL(r.Lat, '---')        AS Lat,
                ISNULL(r.Lng, '---')        AS Lng,
                ISNULL(ra.Value, '---')     AS Admin1,
                ISNULL(ra.Code,  '---')     AS Admin1Code
            FROM data.Reference r
            LEFT JOIN data.ReferenceAdmins ra
                ON  ra.ReferenceId  = r.ReferenceId
                AND ra.AdminLevelId = (
                    SELECT MIN(AdminLevelId) FROM data.AdminLevels
                    WHERE CountryId = r.CountryId AND LevelNumber = 1)
            WHERE r.CountryId = @CountryId
              AND r.Curated   = 1
              AND r.AltNameOf IS NULL
              AND r.Flagged   = 0
            ORDER BY r.ZpCode, r.IsDefault DESC, r.PlaceName";

    public static readonly string ExportReferenceDataWithCuration =
        @"SELECT
                r.ZpCode,
                r.PlaceName,
                r.Timezone,
                CAST(r.IsDefault       AS BIT) AS IsDefault,
                ISNULL(r.Lat,  '---')          AS Lat,
                ISNULL(r.Lng,  '---')          AS Lng,
                ISNULL(ra.Value, '---')        AS Admin1,
                ISNULL(ra.Code,  '---')        AS Admin1Code,
                CAST(r.TimezoneChecked AS BIT) AS TimezoneChecked,
                CAST(r.NameChecked     AS BIT) AS NameChecked,
                r.AltNameOf
            FROM data.Reference r
            LEFT JOIN data.ReferenceAdmins ra
                ON  ra.ReferenceId  = r.ReferenceId
                AND ra.AdminLevelId = (
                    SELECT MIN(AdminLevelId) FROM data.AdminLevels
                    WHERE CountryId = r.CountryId AND LevelNumber = 1)
            WHERE r.CountryId = @CountryId
            ORDER BY r.ZpCode, r.IsDefault DESC, r.PlaceName";

    public static readonly string ExportReferenceDataCuratedOnlyWithCuration =
        @"SELECT
                r.ZpCode,
                r.PlaceName,
                r.Timezone,
                CAST(r.IsDefault       AS BIT) AS IsDefault,
                ISNULL(r.Lat,  '---')          AS Lat,
                ISNULL(r.Lng,  '---')          AS Lng,
                ISNULL(ra.Value, '---')        AS Admin1,
                ISNULL(ra.Code,  '---')        AS Admin1Code,
                CAST(r.TimezoneChecked AS BIT) AS TimezoneChecked,
                CAST(r.NameChecked     AS BIT) AS NameChecked,
                r.AltNameOf
            FROM data.Reference r
            LEFT JOIN data.ReferenceAdmins ra
                ON  ra.ReferenceId  = r.ReferenceId
                AND ra.AdminLevelId = (
                    SELECT MIN(AdminLevelId) FROM data.AdminLevels
                    WHERE CountryId = r.CountryId AND LevelNumber = 1)
            WHERE r.CountryId = @CountryId
              AND r.Curated   = 1
              AND r.Flagged   = 0
            ORDER BY r.ZpCode, r.IsDefault DESC, r.PlaceName";

    // Returns 1 if the incoming name matches any canonical name OR is the
    // canonical of a known alternate for this code. Used in Import Rule 6 to
    // avoid creating a Name discrepancy for abbreviation-equivalent names.
    public static readonly string GetAltNameMatch =
        @"SELECT TOP 1 1
          FROM data.Reference
          WHERE CountryId = @CountryId
            AND ZpCode     = @ZpCode
            AND (PlaceName  = @PlaceName
              OR AltNameOf  = @PlaceName)";

    public static readonly string UpdateReferenceTimezoneBatch =
        @"UPDATE r
            SET    r.Timezone        = src.Timezone,
                   r.TimezoneChecked = 1
            FROM   data.Reference r
            JOIN  (VALUES {0}) AS src(ZpCode, Timezone)
                ON  r.CountryId = @CountryId
                AND r.ZpCode    = src.ZpCode
            WHERE  r.TimezoneChecked = 0;";

    public static readonly string UpdateReferenceNameCheckedBatch =
        @"UPDATE r
            SET    r.NameChecked = 1
            FROM   data.Reference r
            JOIN  (VALUES {0}) AS src(ZpCode, PlaceName)
                ON  r.CountryId  = @CountryId
                AND r.ZpCode     = src.ZpCode
                AND r.PlaceName  = src.PlaceName
            WHERE  r.NameChecked = 0;";

    // --- Candidates ---

    public static readonly string UpdateCandidateStatus =
        @"UPDATE codes.Candidate
              SET Status = @Status
              WHERE CountryId = @CountryId
                AND RunId     = @RunId
                AND ZpCode    = @ZpCode
                AND PlaceName = @PlaceName";

    public static readonly string BulkUpdateCandidateStatus =
        @"UPDATE c
              SET c.Status = u.Status
              FROM codes.Candidate c
              JOIN @Updates u ON u.Code = c.ZpCode AND u.Name = c.PlaceName
              WHERE c.CountryId = @CountryId
                AND c.RunId     = @RunId";

    public static readonly string GetCandidatesByStatus =
        @"SELECT c.RecordNumber, c.ZpCode, c.PlaceName, c.Timezone,
                     CAST(c.IsDefault AS NVARCHAR(5)) AS IsDefault,
                     ca.Value AS Admin1, ca.Code AS Admin1Code
              FROM codes.Candidate c
              LEFT JOIN codes.CandidateAdmins ca
                ON  ca.CandidateId  = c.CandidateId
                AND ca.AdminLevelId = (
                    SELECT MIN(AdminLevelId) FROM codes.AdminLevels
                    WHERE CountryId = c.CountryId AND LevelNumber = 1)
              WHERE c.CountryId = @CountryId
                AND c.RunId     = @RunId
                AND c.Status    = @Status
              ORDER BY c.RecordNumber";

    public static readonly string GetCandidatesByCode =
        @"SELECT c.RecordNumber, c.ZpCode, c.PlaceName, c.Timezone,
                     CAST(c.IsDefault AS NVARCHAR(5)) AS IsDefault,
                     ca.Value AS Admin1, ca.Code AS Admin1Code
              FROM codes.Candidate c
              LEFT JOIN codes.CandidateAdmins ca
                ON  ca.CandidateId  = c.CandidateId
                AND ca.AdminLevelId = (
                    SELECT MIN(AdminLevelId) FROM codes.AdminLevels
                    WHERE CountryId = c.CountryId AND LevelNumber = 1)
              WHERE c.CountryId = @CountryId
                AND c.RunId     = @RunId
                AND c.ZpCode    = @ZpCode
              ORDER BY c.PlaceName";

    public static readonly string UpdateCandidateStatusUnfound =
        $@"UPDATE codes.Candidate
              SET Status = '{nameof(CandidateStatus.Unfound)}'
              WHERE CountryId = @CountryId
                AND RunId     = @RunId
                AND ZpCode    = @ZpCode";

    public static readonly string GetCandidateStateCode =
        @"SELECT TOP 1 ca.Code
              FROM codes.Candidate c
              JOIN codes.CandidateAdmins ca ON ca.CandidateId = c.CandidateId
              WHERE c.CountryId = @CountryId
                AND c.RunId     = @RunId
                AND c.ZpCode    = @ZpCode
                AND ca.AdminLevelId = (
                    SELECT MIN(AdminLevelId) FROM codes.AdminLevels
                    WHERE CountryId = c.CountryId AND LevelNumber = 1)";

    public static readonly string GetCandidateAdminLevels =
        @"SELECT LevelNumber, AdminLevelId FROM codes.AdminLevels WHERE CountryId = @CountryId";

    public static readonly string MarkCandidatesAsError =
        $@"UPDATE codes.Candidate
              SET Status = '{nameof(CandidateStatus.Error)}'
              WHERE CountryId = @CountryId AND RunId = @RunId";

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

    // --- Discrepancies ---

    public static readonly string GetPendingDiscrepancies =
        @"SELECT
                CountryId, RunId, ZpCode, PlaceName, AdminLevelId,
                FieldName, RefValue, InValue, OverrideValue, AcceptIncoming,
                CAST(Process AS BIT) AS Process
              FROM codes.Discrepancies
              WHERE CountryId = @CountryId
                AND RunId     = @RunId
                AND Process   = 0
              ORDER BY ZpCode, PlaceName, FieldName";

    public static readonly string UpdateDiscrepancyProcessed =
        @"UPDATE codes.Discrepancies
              SET AcceptIncoming = @AcceptIncoming,
                  Process        = 1,
                  OverrideValue  = CASE
                      WHEN FieldName = 'timezone' THEN @OverrideTimezone
                      ELSE OverrideValue
                  END,
                  ResolvedAt     = SYSUTCDATETIME()
              WHERE CountryId = @CountryId
                AND RunId     = @RunId
                AND ZpCode    = @ZpCode
                AND PlaceName = @PlaceName";

    public static readonly string GetDiscrepancyFieldSummary =
        @"SELECT FieldName,
                    COUNT(*) AS Total,
                    COUNT(CASE WHEN Process = 0 THEN 1 END) AS Unresolved,
                    COUNT(CASE WHEN Process = 1 AND AcceptIncoming = 1 THEN 1 END) AS Accepted,
                    COUNT(CASE WHEN Process = 1 AND AcceptIncoming = 0 THEN 1 END) AS Rejected
              FROM codes.Discrepancies
              WHERE CountryId = @CountryId
                AND RunId     = @RunId
              GROUP BY FieldName
              ORDER BY Unresolved DESC, Total DESC";

    public static readonly string GetDistinctNamesFromDiscrepancies =
        @"SELECT DISTINCT PlaceName FROM codes.Discrepancies
              WHERE CountryId = @CountryId AND RunId = @RunId
                AND ZpCode = @ZpCode AND Process = 0";

    public static readonly string UpdateDiscrepancyWithOverride =
        @"UPDATE codes.Discrepancies
              SET OverrideValue  = CASE
                      WHEN FieldName = 'Name'       THEN @OverrideName
                      WHEN FieldName = 'state'      THEN @State
                      WHEN FieldName = 'state_name' THEN @StateName
                      WHEN FieldName = 'timezone'   THEN @Timezone
                      ELSE OverrideValue
                  END,
                  AcceptIncoming = 1,
                  Process        = 1
              WHERE CountryId = @CountryId
                AND RunId     = @RunId
                AND ZpCode    = @ZpCode
                AND PlaceName = @PlaceName
                AND Process   = 0";

    public static readonly string MarkDiscrepanciesProcessed =
        @"UPDATE codes.Discrepancies
              SET Process        = 1,
                  AcceptIncoming = 0
              WHERE CountryId = @CountryId
                AND RunId     = @RunId
                AND ZpCode    = @ZpCode
                AND Process   = 0";

    // --- System ---

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

    // Reset TimezoneChecked=0 on any row where the timezone is blank/null/placeholder
    // but TimezoneChecked is currently 1. This undoes false "verified" marks so the
    // rows surface in the uncurated browse list for manual fixing (or are immediately
    // re-resolved by the coordinate resolver if they have lat/lng).
    public static readonly string ResetBlankTimezoneChecked =
        @"UPDATE data.Reference
          SET    TimezoneChecked = 0,
                 UpdatedAt       = SYSUTCDATETIME()
          WHERE  TimezoneChecked = 1
            AND  (Timezone IS NULL OR Timezone = '' OR Timezone = '---')";

    // Normalise all deprecated IANA timezone aliases to their canonical equivalents.
    // Run this across all countries; it is safe to repeat (idempotent).
    public static readonly string NormalizeDeprecatedTimezones =
        @"UPDATE data.Reference
          SET Timezone = CASE Timezone
              WHEN 'America/Nipigon'     THEN 'America/Toronto'
              WHEN 'America/Thunder_Bay' THEN 'America/Toronto'
              WHEN 'America/Rainy_River' THEN 'America/Winnipeg'
              WHEN 'America/Pangnirtung' THEN 'America/Iqaluit'
              WHEN 'America/Glace_Bay'   THEN 'America/Halifax'
              WHEN 'America/Creston'     THEN 'America/Phoenix'
              WHEN 'America/Shiprock'    THEN 'America/Denver'
          END
          WHERE Timezone IN (
              'America/Nipigon', 'America/Thunder_Bay', 'America/Rainy_River',
              'America/Pangnirtung', 'America/Glace_Bay', 'America/Creston',
              'America/Shiprock'
          );";

    // Remove US reference rows whose ZIP code is outside the USPS-assigned range 00501–99950.
    // Run PurgeOutOfRangeUsAdmins first to satisfy the FK constraint, then PurgeOutOfRangeUs.
    public static readonly string PurgeOutOfRangeUsAdmins =
        @"DELETE ra FROM data.ReferenceAdmins ra
          JOIN data.Reference r ON r.ReferenceId = ra.ReferenceId
          WHERE r.CountryId = 'US'
            AND TRY_CAST(r.ZpCode AS INT) IS NOT NULL
            AND (TRY_CAST(r.ZpCode AS INT) < 501
              OR TRY_CAST(r.ZpCode AS INT) > 99950)";

    public static readonly string PurgeOutOfRangeUs =
        @"DELETE FROM data.Reference
          WHERE CountryId = 'US'
            AND TRY_CAST(ZpCode AS INT) IS NOT NULL
            AND (TRY_CAST(ZpCode AS INT) < 501
              OR TRY_CAST(ZpCode AS INT) > 99950)";

    // Count of US reference rows outside the valid range — preview before purge.
    public static readonly string CountOutOfRangeUs =
        @"SELECT COUNT(*)
          FROM   data.Reference
          WHERE  CountryId = 'US'
            AND  TRY_CAST(ZpCode AS INT) IS NOT NULL
            AND  (TRY_CAST(ZpCode AS INT) < 501
               OR TRY_CAST(ZpCode AS INT) > 99950)";

    // Delete ReferenceAdmins for rows that became true duplicates after timezone normalisation.
    // A row is a true duplicate when its (CountryId, ZpCode, PlaceName) already has an IsDefault=1 row
    // with the same (now-canonical) timezone, making this IsDefault=0 row redundant.
    // Uses IN(subquery) so the admin and reference deletes target the identical ReferenceId set.
    public static readonly string DeleteDeprecatedAliasDuplicateAdmins =
        @"DELETE FROM data.ReferenceAdmins
          WHERE ReferenceId IN (
              SELECT r.ReferenceId
              FROM data.Reference r
              WHERE r.IsDefault = 0
                AND EXISTS (
                    SELECT 1 FROM data.Reference r2
                    WHERE r2.CountryId = r.CountryId
                      AND r2.ZpCode    = r.ZpCode
                      AND r2.PlaceName = r.PlaceName
                      AND r2.IsDefault = 1
                      AND r2.Timezone  = r.Timezone
                )
          );";

    // Delete Reference rows that became true duplicates after timezone normalisation (see above).
    // Run DeleteDeprecatedAliasDuplicateAdmins first to satisfy the FK constraint.
    public static readonly string DeleteDeprecatedAliasDuplicates =
        @"DELETE FROM data.Reference
          WHERE ReferenceId IN (
              SELECT r.ReferenceId
              FROM data.Reference r
              WHERE r.IsDefault = 0
                AND EXISTS (
                    SELECT 1 FROM data.Reference r2
                    WHERE r2.CountryId = r.CountryId
                      AND r2.ZpCode    = r.ZpCode
                      AND r2.PlaceName = r.PlaceName
                      AND r2.IsDefault = 1
                      AND r2.Timezone  = r.Timezone
                )
          );";

    // Detect admin level 1 codes that have more than one distinct name value for a country.
    // Returns one row per minority variant: the code, the dominant name it will be normalised to,
    // the minority name, and how many rows carry it. Ties broken alphabetically (deterministic).
    public static readonly string DetectAdminNameVariants =
        @"WITH Counts AS (
            SELECT ra.Code, ra.Value, COUNT(*) AS Cnt
            FROM   data.ReferenceAdmins ra
            JOIN   data.Reference       r  ON r.ReferenceId  = ra.ReferenceId
            JOIN   data.AdminLevels     al ON al.AdminLevelId = ra.AdminLevelId
                                           AND al.LevelNumber  = 1
            WHERE  r.CountryId = @CountryId
            GROUP  BY ra.Code, ra.Value
          ),
          Dominant AS (
            SELECT Code, Value AS DominantValue
            FROM (
                SELECT Code, Value,
                       ROW_NUMBER() OVER (PARTITION BY Code ORDER BY Cnt DESC, Value ASC) AS Rn
                FROM   Counts
            ) Ranked
            WHERE  Rn = 1
          )
          SELECT c.Code, d.DominantValue, c.Value AS MinorityValue, c.Cnt AS MinorityCnt
          FROM   Counts   c
          JOIN   Dominant d ON d.Code = c.Code
          WHERE  c.Value <> d.DominantValue
          ORDER  BY c.Code, c.Cnt DESC";

    // Normalise all minority admin level 1 name variants to the dominant spelling.
    // Run DetectAdminNameVariants first to preview what will change.
    public static readonly string NormalizeAdminNames =
        @"WITH Counts AS (
            SELECT ra.Code, ra.Value, COUNT(*) AS Cnt
            FROM   data.ReferenceAdmins ra
            JOIN   data.Reference       r  ON r.ReferenceId  = ra.ReferenceId
            JOIN   data.AdminLevels     al ON al.AdminLevelId = ra.AdminLevelId
                                           AND al.LevelNumber  = 1
            WHERE  r.CountryId = @CountryId
            GROUP  BY ra.Code, ra.Value
          ),
          Dominant AS (
            SELECT Code, Value AS DominantValue
            FROM (
                SELECT Code, Value,
                       ROW_NUMBER() OVER (PARTITION BY Code ORDER BY Cnt DESC, Value ASC) AS Rn
                FROM   Counts
            ) Ranked
            WHERE  Rn = 1
          )
          UPDATE ra
          SET    ra.Value = d.DominantValue
          FROM   data.ReferenceAdmins ra
          JOIN   data.Reference       r  ON r.ReferenceId  = ra.ReferenceId
          JOIN   data.AdminLevels     al ON al.AdminLevelId = ra.AdminLevelId
                                        AND al.LevelNumber  = 1
          JOIN   Dominant             d  ON d.Code = ra.Code
          WHERE  r.CountryId  = @CountryId
            AND  ra.Value    <> d.DominantValue";

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

    public static readonly string GetTestDataSet =
        @"DECLARE @AdminLevelId INT = (
                SELECT MIN(al.AdminLevelId)
                FROM   data.AdminLevels al
                WHERE  al.CountryId   = @CountryId
                AND    al.LevelNumber = 1
            );

            WITH SampledCodes AS (
                SELECT TOP (@Count) r.ZpCode
                FROM   data.Reference r
                WHERE  r.CountryId = @CountryId
                AND    r.Curated   = 1
                AND    r.IsDefault = 1
                AND    r.Flagged   = 0
                GROUP BY r.ZpCode
                ORDER BY NEWID()
            ),
            RankedDefaults AS (
                SELECT r.ZpCode,
                       r.PlaceName,
                       r.Timezone,
                       r.IsDefault,
                       ra.Value AS Admin1,
                       ra.Code  AS Admin1Code,
                       ROW_NUMBER() OVER (
                           PARTITION BY r.ZpCode
                           ORDER BY r.PlaceName DESC
                       ) AS Rn
                FROM   data.Reference r
                INNER JOIN SampledCodes sc ON sc.ZpCode = r.ZpCode
                LEFT  JOIN data.ReferenceAdmins ra
                        ON  ra.ReferenceId  = r.ReferenceId
                        AND ra.AdminLevelId = @AdminLevelId
                WHERE  r.CountryId = @CountryId
                AND    r.Curated   = 1
                AND    r.IsDefault = 1
                AND    r.Flagged   = 0
            )
            SELECT ZpCode, PlaceName, Timezone, IsDefault, Admin1, Admin1Code
            FROM   RankedDefaults
            WHERE  Rn = 1";

    public static readonly string GetNonDefaultTestDataSet =
        @"SELECT TOP (@Count)
                     r.ZpCode,
                     r.PlaceName,
                     r.Timezone,
                     CAST(r.IsDefault AS BIT) AS IsDefault,
                     ra.Value AS Admin1,
                     ra.Code  AS Admin1Code
              FROM   data.Reference r
              LEFT JOIN data.ReferenceAdmins ra
                     ON  ra.ReferenceId  = r.ReferenceId
                     AND ra.AdminLevelId = (
                             SELECT MIN(al.AdminLevelId)
                             FROM   data.AdminLevels al
                             WHERE  al.CountryId   = r.CountryId
                               AND  al.LevelNumber = 1)
              WHERE  r.CountryId = @CountryId
                AND  r.Curated   = 1
                AND  r.IsDefault = 0
                AND  r.AltNameOf IS NULL
                AND  r.Flagged   = 0
              ORDER  BY NEWID()";

    // =========================================================================
    // CDT DB Integrity queries
    // =========================================================================

    // One admin code per curated ZpCode (default rows only) for correctness checking.
    public static readonly string GetCuratedDefaultAdminCodes =
        @"SELECT DISTINCT r.ZpCode, ISNULL(ra.Code, '---') AS Admin1Code
          FROM   data.Reference r
          LEFT JOIN data.ReferenceAdmins ra
                 ON  ra.ReferenceId  = r.ReferenceId
                 AND ra.AdminLevelId = (SELECT MIN(AdminLevelId) FROM data.AdminLevels
                                        WHERE CountryId = r.CountryId AND LevelNumber = 1)
          WHERE  r.CountryId = @CountryId
            AND  r.Curated   = 1
            AND  r.IsDefault = 1
            AND  r.AltNameOf IS NULL
            AND  r.Flagged   = 0
          ORDER  BY r.ZpCode";

    // AltNameOf rows where the canonical name (AltNameOf value) does not exist
    // as a non-AltNameOf row for the same ZpCode.
    public static readonly string GetOrphanAltNamesDetailed =
        @"SELECT r.ZpCode, r.PlaceName, r.AltNameOf
          FROM   data.Reference r
          WHERE  r.CountryId  = @CountryId
            AND  r.Flagged    = 0
            AND  r.AltNameOf IS NOT NULL
            AND  NOT EXISTS (
                     SELECT 1 FROM data.Reference r2
                     WHERE  r2.CountryId = r.CountryId
                       AND  r2.ZpCode    = r.ZpCode
                       AND  r2.PlaceName = r.AltNameOf
                       AND  r2.AltNameOf IS NULL)
          ORDER  BY r.ZpCode, r.PlaceName";

    // ZpCodes with more than one IsDefault=1 non-AltNameOf curated row.
    public static readonly string GetDuplicateIsDefaultCodes =
        @"SELECT r.ZpCode, COUNT(*) AS DefaultCount
          FROM   data.Reference r
          WHERE  r.CountryId = @CountryId
            AND  r.Curated   = 1
            AND  r.IsDefault = 1
            AND  r.AltNameOf IS NULL
            AND  r.Flagged   = 0
          GROUP  BY r.ZpCode
          HAVING COUNT(*) > 1
          ORDER  BY COUNT(*) DESC, r.ZpCode";

    // Count of curated ZpCodes with missing, blank, or wrong-format admin1.
    public static readonly string GetCuratedMissingAdmin1Count =
        @"SELECT COUNT(DISTINCT r.ZpCode)
          FROM   data.Reference r
          LEFT JOIN data.ReferenceAdmins ra
                 ON  ra.ReferenceId  = r.ReferenceId
                 AND ra.AdminLevelId = (SELECT MIN(AdminLevelId) FROM data.AdminLevels
                                        WHERE CountryId = r.CountryId AND LevelNumber = 1)
          WHERE  r.CountryId = @CountryId
            AND  r.Curated   = 1
            AND  r.AltNameOf IS NULL
            AND  r.Flagged   = 0
            AND (ra.ReferenceId IS NULL
              OR ra.Code = '---'
              OR ra.Code = ''
              OR ra.Code NOT LIKE '%[^0-9]%')";

    // Rows whose ZpCode doesn't match the country's expected format.
    // Pass @ValidPattern as a SQL LIKE pattern: '[0-9][0-9][0-9][0-9][0-9]' for US/MX,
    // '[A-Z][0-9][A-Z]%' for CA.
    public static readonly string GetInvalidZpCodes =
        @"SELECT r.ReferenceId, r.ZpCode, r.PlaceName
          FROM   data.Reference r
          WHERE  r.CountryId = @CountryId
            AND  r.Flagged   = 0
            AND  r.ZpCode NOT LIKE @ValidPattern
          ORDER  BY r.ZpCode";

    // Curated non-AltNameOf codes where no row has IsDefault=1.
    // GetByCode() has no authoritative primary for these codes.
    public static readonly string GetCuratedCodesWithNoDefault =
        @"SELECT r.ZpCode
          FROM   data.Reference r
          WHERE  r.CountryId = @CountryId
            AND  r.Curated   = 1
            AND  r.AltNameOf IS NULL
            AND  r.Flagged   = 0
          GROUP  BY r.ZpCode
          HAVING SUM(CAST(r.IsDefault AS INT)) = 0
          ORDER  BY r.ZpCode";

    // AltNameOf rows that are also marked IsDefault=1.
    // These are returned as the primary result by GetByCode() instead of the canonical row.
    public static readonly string GetAltNameRowsMarkedDefault =
        @"SELECT r.ZpCode, r.PlaceName, r.AltNameOf
          FROM   data.Reference r
          WHERE  r.CountryId = @CountryId
            AND  r.AltNameOf IS NOT NULL
            AND  r.IsDefault = 1
            AND  r.Curated   = 1
            AND  r.Flagged   = 0
          ORDER  BY r.ZpCode";

    // Curated rows with a blank or placeholder PlaceName.
    public static readonly string GetCuratedBlankPlaceNames =
        @"SELECT r.ReferenceId, r.ZpCode, r.PlaceName
          FROM   data.Reference r
          WHERE  r.CountryId = @CountryId
            AND  r.Curated   = 1
            AND  r.Flagged   = 0
            AND  (r.PlaceName IS NULL
               OR LTRIM(RTRIM(r.PlaceName)) = ''
               OR r.PlaceName = '---')
          ORDER  BY r.ZpCode";

    // TimezoneChecked=1 rows with a blank or placeholder Timezone.
    // Claims verified but would return an empty timezone string.
    public static readonly string GetCheckedBlankTimezones =
        @"SELECT r.ReferenceId, r.ZpCode, r.PlaceName
          FROM   data.Reference r
          WHERE  r.CountryId       = @CountryId
            AND  r.TimezoneChecked = 1
            AND  r.Flagged         = 0
            AND  (r.Timezone IS NULL
               OR LTRIM(RTRIM(r.Timezone)) = ''
               OR r.Timezone = '---')
          ORDER  BY r.ZpCode";
}
