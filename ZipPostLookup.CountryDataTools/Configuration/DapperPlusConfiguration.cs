using Z.Dapper.Plus;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Configuration;

/// <summary>
/// Registers all Z.Dapper.Plus entity mappings at application startup.
/// Call <see cref="Configure"/> once from Program.cs before any commands run.
///
/// Named mappings are used where the same model needs different column sets
/// for different operations — for example, inserting a full reference row vs
/// updating only coordinates, or inserting a full candidate vs updating only Status.
/// </summary>
public static class DapperPlusConfiguration
{
    public static void Configure()
    {
        ConfigureDataReference();
        ConfigureDataReferenceAdmin();
        ConfigureCodesCandidate();
        ConfigureCodesCandidateAdmin();
        ConfigureCodesDiscrepancies();
        ConfigurePipelineDecisions();
        ConfigureDataAdminLevels();
    }

    private static void ConfigureDataAdminLevels()
    {
        DapperPlusManager.Entity<DataAdminLevel>()
            .Table("data.AdminLevels")
            .Identity(x => x.AdminLevelId, true)
            .Key( x => new { x.CountryId, x.LevelNumber })
            .ForeignKey(x => x.CountryId, "CountryId")
            .Map(x => x.LevelNumber, "LevelNumber")
            .Map(x => x.CodeType, "CodeType")
            .Map(x => x.LevelName, "LevelName")
            .Map(x => x.Aliases, "Aliases")
            .Map(x => x.CreatedAt, "CreatedAt");
    }

    // =========================================================================
    // data.reference
    // =========================================================================

    private static void ConfigureDataReference()
    {
        // Default mapping — bulk insert/merge of full reference rows (ingest ref).
        // AfterAction propagates the generated ReferenceId PK back to child admin rows.
        DapperPlusManager.Entity<DataReference>()
            .Table("data.Reference")
            .Identity(x => x.ReferenceId, true)
            .Key(x => x.CountryId, "CountryId")
            .Key(x => x.ZpCode, "ZpCode")
            .Key(x => x.PlaceName, "PlaceName")
            .Map(x => x.Timezone, "Timezone")
            .Map(x => x.IsDefault, "IsDefault")
            .Map(x => x.Lat, "Lat")
            .Map(x => x.Lng, "Lng")
            .Map(x => x.TimezoneChecked, "TimezoneChecked")
            .Map(x => x.NameChecked, "NameChecked")
            .Ignore(x => x.Curated)
            .Ignore(x => x.Admin1)
            .Ignore(x => x.Admin1Code)
            .Ignore(x => x.AdminReferenceList)
            .AfterAction((actionKind, dataReference) =>
            {
                if (!IsInsertOrMerge(actionKind)) { return; }
                SetReferenceIdOnAdmins(dataReference);
            });

        // Named mapping — bulk-inserts genuinely new reference rows discovered during
        // candidate processing (ImportCandidatesCommand). No admin rows to propagate,
        // and CreatedAt/UpdatedAt are excluded so DB DEFAULT SYSUTCDATETIME() fires.
        DapperPlusManager.Entity<DataReference>("NewReference")
            .Table("data.Reference")
            .Identity(x => x.ReferenceId, true)
            .Map(x => x.CountryId, "CountryId")
            .Map(x => x.ZpCode, "ZpCode")
            .Map(x => x.PlaceName, "PlaceName")
            .Map(x => x.Timezone, "Timezone")
            .Map(x => x.IsDefault, "IsDefault")
            .Map(x => x.Lat, "Lat")
            .Map(x => x.Lng, "Lng")
            .Map(x => x.TimezoneChecked, "TimezoneChecked")
            .Map(x => x.NameChecked, "NameChecked")
            .Ignore(x => x.Curated)
            .Ignore(x => x.Admin1)
            .Ignore(x => x.Admin1Code)
            .Ignore(x => x.AdminReferenceList);

        // Named mapping — back-fills lat/lng and sets TimezoneChecked=1 on existing
        // reference rows where coordinates were missing (ImportCandidatesCommand).
        // Keyed on ReferenceId so the UPDATE is a direct PK lookup per row.
        DapperPlusManager.Entity<DataReference>("CoordUpdate")
            .Table("data.Reference")
            .Key(x => x.ReferenceId, "ReferenceId")
            .Map(x => x.Lat, "Lat")
            .Map(x => x.Lng, "Lng")
            .Map(x => x.TimezoneChecked, "TimezoneChecked");
    }

    // =========================================================================
    // data.reference_admins
    // =========================================================================

    private static void ConfigureDataReferenceAdmin()
    {
        DapperPlusManager.Entity<DataReferenceAdmin>()
            .Table("data.ReferenceAdmins")
            .Identity(x => x.ReferenceAdminId, true)
            .Key(x => x.ReferenceId, "ReferenceId")
            .Key(x => x.AdminLevelId, "AdminLevelId")
            .Key(x => x.Code, "Code");
    }

    // =========================================================================
    // codes.candidate
    // =========================================================================

    private static void ConfigureCodesCandidate()
    {
        // Default mapping — bulk insert of all candidate rows (ImportCandidatesCommand step 4).
        // AfterAction propagates the generated CandidateId PK back to child admin rows.
        DapperPlusManager.Entity<CodesCandidate>()
            .Table("codes.Candidate")
            .BatchSize(1000)
            .Identity(x => x.CandidateId, true)
            .AfterAction((actionKind, codesCandidate) =>
            {
                if (!IsInsertOrMerge(actionKind)) { return; }
                SetCandidateIdOnAdmins(codesCandidate);
            });

        // Named mapping — updates only Status per candidate at chunk flush time
        // (ImportCandidatesCommand). CandidateId is the identity so SQL Server
        // can locate each row without a table scan.
        DapperPlusManager.Entity<CodesCandidate>("CandidateStatusOnly")
            .Table("codes.Candidate")
            .Identity(x => x.CandidateId, true)
            .Map(x => x.CandidateId, "CandidateId")
            .Map(x => x.Status, "Status");
    }

    // =========================================================================
    // codes.candidate_admins
    // =========================================================================

    private static void ConfigureCodesCandidateAdmin()
    {
        DapperPlusManager.Entity<CodesCandidateAdmin>()
            .Table("codes.CandidateAdmins")
            .Identity(x => x.CandidateAdminId, true)
            .Key(x => x.CandidateId, "CandidateId")
            .Key(x => x.AdminLevelId, "AdminLevelId")
            .Key(x => x.Code, "Code");
    }

    // =========================================================================
    // codes.discrepancies
    // =========================================================================

    private static void ConfigureCodesDiscrepancies()
    {
        DapperPlusManager.Entity<CodesDiscrepancies>()
            .Table("codes.Discrepancies")
            .Identity(x => x.discrepancyId, true)
            .Map(x => x.CountryId, "CountryId")
            .Map(x => x.RunId, "RunId")
            .Map(x => x.ZpCode, "ZpCode")
            .Map(x => x.PlaceName, "PlaceName")
            .Map(x => x.AdminLevelId, "AdminLevelId")
            .Map(x => x.FieldName, "FieldName")
            .Map(x => x.RefValue, "RefValue")
            .Map(x => x.InValue, "InValue")
            .Map(x => x.Process, "Process");
    }

    // =========================================================================
    // pipeline.decisions
    // =========================================================================

    private static void ConfigurePipelineDecisions()
    {
        // Used by BulkInsertDecisionsAsync to write Rule-5 auto-rejection audit rows
        // in a single round-trip rather than one InsertAsync per decision.
        DapperPlusManager.Entity<PipelineDecisions>()
            .Table("pipeline.Decisions")
            .Identity(x => x.DecisionId, true)
            .Map(x => x.CountryId,      "CountryId")
            .Map(x => x.RunId,          "RunId")
            .Map(x => x.ZpCode,         "ZpCode")
            .Map(x => x.PlaceName,      "PlaceName")
            .Map(x => x.AcceptIncoming, "AcceptIncoming")
            .Map(x => x.DecidedBy,      "DecidedBy")
            .Map(x => x.Notes,          "Notes")
            .Map(x => x.CreatedAt,      "CreatedAt");
    }

    // =========================================================================
    // AfterAction helpers
    // =========================================================================

    private static bool IsInsertOrMerge(DapperPlusActionKind actionKind) =>
        actionKind is DapperPlusActionKind.Insert or DapperPlusActionKind.Merge;

    private static void SetReferenceIdOnAdmins(DataReference dataReference)
    {
        if (!dataReference.AdminReferenceList.Any()) { return; }
        dataReference.AdminReferenceList.ForEach(admin =>
            admin.ReferenceId = dataReference.ReferenceId);
    }

    private static void SetCandidateIdOnAdmins(CodesCandidate codesCandidate)
    {
        if (!codesCandidate.AdminCandidateList.Any()) { return; }
        codesCandidate.AdminCandidateList.ForEach(admin =>
            admin.CandidateId = codesCandidate.CandidateId);
    }
}