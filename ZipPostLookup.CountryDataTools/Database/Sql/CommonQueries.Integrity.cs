namespace ZipPostLookup.CountryDataTools.Database.Sql;

public static partial class CommonQueries
{
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

    // Curated rows whose PlaceName is non-blank but contains no alphabetic character
    // (e.g. "1910", "20 30", "601") — junk colonia/number artefacts that passed curation.
    // Disjoint from GetCuratedBlankPlaceNames: blank/placeholder names are excluded here.
    public static readonly string GetCuratedNonAlphaPlaceNames =
        @"SELECT r.ReferenceId, r.ZpCode, r.PlaceName
          FROM   data.Reference r
          WHERE  r.CountryId = @CountryId
            AND  r.Curated   = 1
            AND  r.Flagged   = 0
            AND  r.PlaceName IS NOT NULL
            AND  LTRIM(RTRIM(r.PlaceName)) NOT IN ('', '---')
            AND  r.PlaceName NOT LIKE '%[A-Za-z]%'
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

    // Gold ZpCodes with at least one unresolved Name discrepancy.
    // Unresolved = ResolvedAt IS NULL (no accept/reject decision applied yet).
    // These warrant human review: the incoming name on a Gold code may be a real alias.
    // NOTE: deliberately does NOT filter on d.CreatedAt > g.GoldAt. That earlier filter
    // ("only conflicts newer than certification") made the check silently report 0 after any
    // GoldCode rebuild — the rebuild resets GoldAt to now, so every pre-existing discrepancy
    // falls outside the window even though it is still genuinely unresolved on a gold code.
    // Silent-catch safe — if data.GoldCode or codes.Discrepancies don't exist yet, the caller
    // catches the exception and returns an empty list.
    public static readonly string GetGoldNameDiscrepancies =
        @"SELECT DISTINCT g.ZpCode
          FROM   data.GoldCode g
          JOIN   codes.Discrepancies d
                 ON  d.CountryId = g.CountryId
                 AND d.ZpCode    = g.ZpCode
          WHERE  g.CountryId    = @CountryId
            AND  d.FieldName    = 'Name'
            AND  d.ResolvedAt  IS NULL
          ORDER  BY g.ZpCode";
}
