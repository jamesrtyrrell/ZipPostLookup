namespace ZipPostLookup.CountryDataTools.Database.Sql;

public static partial class CommonQueries
{
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
}
