namespace ZipPostLookup.CountryDataTools.Database.Sql;

public static partial class CommonQueries
{
    // --- Discrepancies ---

    // Existing Name-discrepancy keys (ZpCode + incoming InValue) for a batch of codes, regardless of
    // resolved state. Ingestion consults this to avoid recreating a Name discrepancy that already
    // exists for the same (code, name) — whether still open (stops per-RunId duplicate rows) or
    // already resolved (respects a prior accept/reject so the backlog does not regrow).
    public static readonly string GetNameDiscrepancyKeysForBatch =
        @"SELECT DISTINCT d.ZpCode, ISNULL(d.InValue, '') AS InValue
          FROM   codes.Discrepancies d
          INNER JOIN @Codes c ON c.Code = d.ZpCode
          WHERE  d.CountryId = @CountryId AND d.FieldName = 'Name'";

    public static readonly string GetPendingDiscrepancies =
        @"SELECT
                CountryId, RunId, ZpCode, PlaceName, AdminLevelId,
                FieldName, RefValue, InValue, OverrideValue, AcceptIncoming,
                CAST(Process AS BIT) AS Process
              FROM codes.Discrepancies
              WHERE CountryId = @CountryId
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
                AND ZpCode    = @ZpCode
                AND PlaceName = @PlaceName";

    public static readonly string GetDistinctNamesFromDiscrepancies =
        @"SELECT DISTINCT PlaceName FROM codes.Discrepancies
              WHERE CountryId = @CountryId
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
                AND ZpCode    = @ZpCode
                AND PlaceName = @PlaceName
                AND Process   = 0";

    public static readonly string MarkDiscrepanciesProcessed =
        @"UPDATE codes.Discrepancies
              SET Process        = 1,
                  AcceptIncoming = 0,
                  ResolvedAt     = SYSUTCDATETIME()
              WHERE CountryId = @CountryId
                AND ZpCode    = @ZpCode
                AND Process   = 0";

    // Closes Name discrepancies whose InValue is already present as a data.Reference PlaceName
    // for the same (CountryId, ZpCode) — i.e. the alias was added by some other path and the
    // discrepancy was never closed. Marks AcceptIncoming=1 (the name is recognised) and stamps
    // ResolvedAt. Called as a cleanup pass at the end of auto-promote.
    public static readonly string CloseAlreadyPromotedDiscrepancies =
        @"UPDATE d
          SET    d.Process        = 1,
                 d.AcceptIncoming = 1,
                 d.ResolvedAt     = SYSUTCDATETIME()
          FROM   codes.Discrepancies d
          WHERE  d.CountryId  = @CountryId
            AND  d.FieldName  = 'Name'
            AND  d.ResolvedAt IS NULL
            AND  EXISTS (
                SELECT 1
                FROM   data.Reference r
                WHERE  r.CountryId = d.CountryId
                  AND  r.ZpCode    = d.ZpCode
                  AND  r.PlaceName = d.InValue
            )";

    // ── Phase 3: Auto-promote candidate aliases ────────────────────────────────

    // Fetches candidate aliases from the deduplicated OpenNameDiscrepancies view — these are
    // distinct (ZpCode, InValue) pairs that are: non-blank, not already present as a data.Reference
    // row, and have at least one unresolved Name discrepancy. Phase 3 compares each InValue against
    // all existing PlaceNames for the code via PlaceNameNormalizer; matches are promoted to AltNameOf
    // rows and the discrepancies resolved.
    public static readonly string GetCandidateAliasesForCountry =
        @"SELECT d.ZpCode, d.InValue, d.RefValue
          FROM   pipeline.OpenNameDiscrepancies d
          WHERE  d.CountryId = @CountryId
            AND  d.InValueBlank = 0
            AND  d.InValueAlreadyARow = 0
          ORDER BY d.ZpCode";

    // All existing PlaceNames for a given code, for equivalence checking against a candidate alias.
    // Returns the canonical reference PlaceName plus any existing AltNameOf variants. Phase 3 uses
    // this to find which (if any) existing name the candidate InValue is equivalent to.
    public static readonly string GetExistingPlaceNamesForCode =
        @"SELECT DISTINCT PlaceName
          FROM   data.Reference
          WHERE  CountryId = @CountryId
            AND  ZpCode    = @ZpCode
          ORDER BY PlaceName";

    // Resolves all unresolved Name discrepancies for a (ZpCode, InValue) pair after a matching
    // AltNameOf row has been inserted into data.Reference. Marks AcceptIncoming=1 (the alias was
    // accepted), Process=1, and ResolvedAt=now. Phase 3 calls this once per promoted alias.
    public static readonly string ResolveDiscrepanciesForPromotedAlias =
        @"UPDATE codes.Discrepancies
              SET AcceptIncoming = 1,
                  Process        = 1,
                  ResolvedAt     = SYSUTCDATETIME()
              WHERE CountryId = @CountryId
                AND ZpCode    = @ZpCode
                AND FieldName = 'Name'
                AND InValue   = @InValue
                AND ResolvedAt IS NULL";
}
