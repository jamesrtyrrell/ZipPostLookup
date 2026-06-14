using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Database.Sql;

public static partial class CommonQueries
{
    // --- Candidates ---

    public static readonly string UpdateCandidateStatus =
        @"UPDATE codes.Candidate
              SET Status = @Status
              WHERE CountryId = @CountryId
                AND ZpCode    = @ZpCode
                AND PlaceName = @PlaceName";

    public static readonly string BulkUpdateCandidateStatus =
        @"UPDATE c
              SET c.Status = u.Status
              FROM codes.Candidate c
              JOIN @Updates u ON u.Code = c.ZpCode AND u.Name = c.PlaceName
              WHERE c.CountryId = @CountryId;";

    public static readonly string GetCandidatesByStatus =
        @"SELECT c.RecordNumber, c.ZpCode, c.PlaceName, c.Timezone,
                     CAST(c.IsDefault AS BIT) AS IsDefault,
                     ca.Value AS Admin1, ca.Code AS Admin1Code
              FROM codes.Candidate c
              LEFT JOIN codes.CandidateAdmins ca
                ON  ca.CandidateId  = c.CandidateId
                AND ca.AdminLevelId = (
                    SELECT MIN(AdminLevelId) FROM codes.AdminLevels
                    WHERE CountryId = c.CountryId AND LevelNumber = 1)
              WHERE c.CountryId = @CountryId
                AND c.Status    = @Status
              ORDER BY c.RecordNumber";

    public static readonly string GetCandidatesByCode =
        @"SELECT c.RecordNumber, c.ZpCode, c.PlaceName, c.Timezone,
                     CAST(c.IsDefault AS BIT) AS IsDefault,
                     ca.Value AS Admin1, ca.Code AS Admin1Code
              FROM codes.Candidate c
              LEFT JOIN codes.CandidateAdmins ca
                ON  ca.CandidateId  = c.CandidateId
                AND ca.AdminLevelId = (
                    SELECT MIN(AdminLevelId) FROM codes.AdminLevels
                    WHERE CountryId = c.CountryId AND LevelNumber = 1)
              WHERE c.CountryId = @CountryId
                AND c.ZpCode    = @ZpCode
              ORDER BY c.PlaceName";

    public static readonly string UpdateCandidateStatusUnfound =
        $@"UPDATE codes.Candidate
              SET Status = '{nameof(CandidateStatus.Unfound)}'
              WHERE CountryId = @CountryId
                AND ZpCode    = @ZpCode";

    public static readonly string GetCandidateStateCode =
        @"SELECT TOP 1 ca.Code
              FROM codes.Candidate c
              JOIN codes.CandidateAdmins ca ON ca.CandidateId = c.CandidateId
              WHERE c.CountryId = @CountryId
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

    // Status counts per run — Candidate editor status summary.
    public static readonly string GetCandidateStatusSummary =
        @"SELECT Status, COUNT(*) AS [Count]
          FROM   codes.Candidate
          WHERE  CountryId = @CountryId
          GROUP  BY Status
          ORDER  BY CASE Status
                      WHEN 'Discrepancy' THEN 1
                      WHEN 'Unfound'     THEN 2
                      WHEN 'Pending'     THEN 3
                      WHEN 'Error'       THEN 4
                      WHEN 'Rejected'    THEN 5
                      WHEN 'Clean'       THEN 6
                      ELSE 7 END";

    // Paginated browse for a single status bucket (Candidate editor).
    public static readonly string GetCandidatesBrowsePage =
        @"SELECT c.ZpCode, c.PlaceName, c.Timezone,
                 CAST(c.IsDefault AS BIT)    AS IsDefault,
                 c.Status,
                 ISNULL(ca.Value, '---')     AS Admin1,
                 ISNULL(ca.Code,  '---')     AS Admin1Code
          FROM   codes.Candidate c
          LEFT   JOIN codes.CandidateAdmins ca
                 ON  ca.CandidateId  = c.CandidateId
                 AND ca.AdminLevelId = (SELECT MIN(AdminLevelId)
                                        FROM   codes.AdminLevels
                                        WHERE  CountryId = c.CountryId AND LevelNumber = 1)
          WHERE  c.CountryId = @CountryId
            AND  c.Status    = @Status
          ORDER  BY c.ZpCode, c.PlaceName
          OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

    public static readonly string GetCandidatesBrowseCount =
        @"SELECT COUNT(*)
          FROM   codes.Candidate
          WHERE  CountryId = @CountryId AND Status = @Status";

    // Set every row for a ZpCode to Rejected regardless of PlaceName.
    public static readonly string RejectCandidateZpCode =
        $@"UPDATE codes.Candidate
           SET    Status = '{nameof(CandidateStatus.Rejected)}'
           WHERE  CountryId = @CountryId AND ZpCode = @ZpCode";

    // Set every row for a ZpCode back to Pending (un-reject), regardless of PlaceName.
    public static readonly string SetCandidatePendingByZpCode =
        $@"UPDATE codes.Candidate
           SET    Status = '{nameof(CandidateStatus.Pending)}'
           WHERE  CountryId = @CountryId AND ZpCode = @ZpCode";

    // All candidate rows for a ZpCode — the candidate-vs-reference comparison view in the
    // ZpCode editor. Maps to CandidateBrowseRow (ZpCode/PlaceName/Timezone/IsDefault/Status/Admin1).
    public static readonly string GetCandidateRowsByCode =
        @"SELECT c.ZpCode, c.PlaceName, c.Timezone,
                 CAST(c.IsDefault AS BIT)    AS IsDefault,
                 c.Status,
                 ISNULL(ca.Value, '---')     AS Admin1,
                 ISNULL(ca.Code,  '---')     AS Admin1Code
          FROM   codes.Candidate c
          LEFT   JOIN codes.CandidateAdmins ca
                 ON  ca.CandidateId  = c.CandidateId
                 AND ca.AdminLevelId = (SELECT MIN(AdminLevelId)
                                        FROM   codes.AdminLevels
                                        WHERE  CountryId = c.CountryId AND LevelNumber = 1)
          WHERE  c.CountryId = @CountryId AND c.ZpCode = @ZpCode
          ORDER  BY c.PlaceName";
}
