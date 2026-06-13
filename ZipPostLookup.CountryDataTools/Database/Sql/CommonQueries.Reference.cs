namespace ZipPostLookup.CountryDataTools.Database.Sql;

public static partial class CommonQueries
{
    // --- Country Info ---

    public static readonly string GetCountryInfoById =
        @"SELECT * FROM data.CountryInfo WHERE CountryId = @CountryId";

    public static readonly string ResetCountryCodeCount =
        @"UPDATE data.CountryInfo SET CodeCount = 0 WHERE CountryId = @CountryId";

    // --- Reference ---

    public static readonly string HasReferenceData =
        @"SELECT COUNT(*) FROM data.Reference WHERE CountryId = @CountryId";

    // Distinct ZpCodes already present for a country — used to skip codes that
    // already exist when importing a bare code-only set (ImportCodesOnlyCommand).
    public static readonly string GetReferenceCodesForCountry =
        @"SELECT DISTINCT ZpCode FROM data.Reference WHERE CountryId = @CountryId";

    // Reference row whose AltNameOf alias matches an incoming place name (enrichment).
    public static readonly string GetReferenceByCodeAndAltName =
        @"SELECT * FROM data.Reference
          WHERE CountryId = @CountryId AND ZpCode = @ZpCode AND AltNameOf = @PlaceName";

    // First "---" placeholder row for a code (left by ImportCodesOnlyCommand) so
    // enrichment can rename it in place instead of inserting a second row.
    public static readonly string GetReferencePlaceholderByCode =
        @"SELECT TOP 1 * FROM data.Reference
          WHERE CountryId = @CountryId AND ZpCode = @ZpCode AND PlaceName = @Placeholder
          ORDER BY ReferenceId";

    // Propagate an API timezone to sibling rows of the same code that still have a
    // blank/placeholder timezone, so MarkCodeAsCurated's TimezoneChecked guard passes.
    public static readonly string PropagateTimezoneToBlankSiblings =
        @"UPDATE data.Reference
          SET    Timezone  = @Timezone, UpdatedAt = SYSUTCDATETIME()
          WHERE  CountryId = @CountryId
            AND  ZpCode    = @ZpCode
            AND  (Timezone IS NULL OR Timezone = '' OR Timezone = '---')";

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
                 r.AltNameOf,
                 CASE WHEN g.ZpCode IS NOT NULL THEN CAST(1 AS BIT)
                      ELSE CAST(0 AS BIT) END AS IsGold
            FROM data.Reference r
            INNER JOIN @Codes c ON c.Code = r.ZpCode
            LEFT  JOIN data.GoldCode g
                   ON  g.CountryId = r.CountryId AND g.ZpCode = r.ZpCode
            WHERE r.CountryId = @CountryId;";

    public static readonly string GetReferenceStateByCodes =
        @"SELECT ZpCode, PlaceName,
                 CAST(TimezoneChecked AS BIT) AS TimezoneChecked,
                 CAST(NameChecked     AS BIT) AS NameChecked
          FROM data.Reference
          WHERE CountryId = @CountryId AND ZpCode IN @Codes";

    // Curated, non-flagged reference rows with their admin levels — for analysis.
    // LEFT JOIN so codes with no admin data are still included (Admin1 falls back to '---').
    // Flagged rows are excluded because they are dropped from every export, so analysing
    // them would inflate compression/size estimates against data that never ships.
    public static readonly string GetAllReferenceDataWithAdmins =
        @"SELECT * FROM data.Reference RData
            LEFT JOIN data.ReferenceAdmins RAdmins ON RData.ReferenceId = RAdmins.ReferenceId
            WHERE RData.CountryId = @CountryId AND RData.Curated = 1 AND RData.Flagged = 0";

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
                 CAST(r.Flagged         AS BIT)  AS Flagged,
                 CAST(CASE WHEN EXISTS (
                     SELECT 1 FROM data.GoldCode g
                     WHERE g.CountryId = r.CountryId AND g.ZpCode = r.ZpCode
                 ) THEN 1 ELSE 0 END AS BIT)     AS IsGold
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
                 CAST(r.Flagged         AS BIT)  AS Flagged,
                 CAST(CASE WHEN EXISTS (
                     SELECT 1 FROM data.GoldCode g
                     WHERE g.CountryId = r.CountryId AND g.ZpCode = r.ZpCode
                 ) THEN 1 ELSE 0 END AS BIT)     AS IsGold
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

    // Back-fill Lat/Lng from a coordinates source, but ONLY on rows whose existing pair is
    // incomplete (one side missing/placeholder). Both coordinates are written together — never
    // a half-pair — so an existing complete pair is left untouched. TimezoneChecked is reset to
    // 0 because the coordinates changed: the prior timezone is no longer verified and must be
    // re-derived (by normalize-tz, or the next coord run). Used by 'Coord Data'.
    public static readonly string UpdateReferenceCoordsBatch =
        @"UPDATE r
            SET    r.Lat             = src.Lat,
                   r.Lng             = src.Lng,
                   r.TimezoneChecked = 0,
                   r.UpdatedAt       = SYSUTCDATETIME()
            FROM   data.Reference r
            JOIN  (VALUES {0}) AS src(ZpCode, Lat, Lng)
                ON  r.CountryId = @CountryId
                AND r.ZpCode    = src.ZpCode
            WHERE  (r.Lat IS NULL OR r.Lat IN ('', '---')
                 OR r.Lng IS NULL OR r.Lng IN ('', '---'));";

    public static readonly string UpdateReferenceNameCheckedBatch =
        @"UPDATE r
            SET    r.NameChecked = 1
            FROM   data.Reference r
            JOIN  (VALUES {0}) AS src(ZpCode, PlaceName)
                ON  r.CountryId  = @CountryId
                AND r.ZpCode     = src.ZpCode
                AND r.PlaceName  = src.PlaceName
            WHERE  r.NameChecked = 0;";

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

    // Replace one deprecated IANA timezone with its canonical form for a single country.
    // The deprecated→canonical mapping is a country rule and lives in CountryRules
    // (ICountryRules.DeprecatedTimezoneAliases); normalize-tz loops over those maps and runs
    // this generic, idempotent statement once per alias. No timezone rules belong in SQL.
    public static readonly string NormalizeTimezoneAlias =
        @"UPDATE data.Reference
          SET    Timezone  = @Canonical,
                 UpdatedAt = SYSUTCDATETIME()
          WHERE  CountryId = @CountryId
            AND  Timezone  = @Deprecated";

    // Remove US reference rows whose ZIP code is outside the USPS-assigned range.
    // Bounds are supplied as @MinZip/@MaxZip params — the single source of truth is
    // UsCountryRules.MinUsZip/MaxUsZip (callers pass them in; no magic numbers here).
    // Run PurgeOutOfRangeUsAdmins first to satisfy the FK constraint, then PurgeOutOfRangeUs.
    public static readonly string PurgeOutOfRangeUsAdmins =
        @"DELETE ra FROM data.ReferenceAdmins ra
          JOIN data.Reference r ON r.ReferenceId = ra.ReferenceId
          WHERE r.CountryId = 'US'
            AND TRY_CAST(r.ZpCode AS INT) IS NOT NULL
            AND (TRY_CAST(r.ZpCode AS INT) < @MinZip
              OR TRY_CAST(r.ZpCode AS INT) > @MaxZip)";

    public static readonly string PurgeOutOfRangeUs =
        @"DELETE FROM data.Reference
          WHERE CountryId = 'US'
            AND TRY_CAST(ZpCode AS INT) IS NOT NULL
            AND (TRY_CAST(ZpCode AS INT) < @MinZip
              OR TRY_CAST(ZpCode AS INT) > @MaxZip)";

    // Count of US reference rows outside the valid range — preview before purge.
    public static readonly string CountOutOfRangeUs =
        @"SELECT COUNT(*)
          FROM   data.Reference
          WHERE  CountryId = 'US'
            AND  TRY_CAST(ZpCode AS INT) IS NOT NULL
            AND  (TRY_CAST(ZpCode AS INT) < @MinZip
               OR TRY_CAST(ZpCode AS INT) > @MaxZip)";

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
}
