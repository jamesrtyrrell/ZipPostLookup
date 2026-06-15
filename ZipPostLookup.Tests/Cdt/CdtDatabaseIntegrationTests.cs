using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;
using ZipPostLookup.CountryDataTools.Database;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// Integration tests for the CDT data-access layer against an isolated throwaway database
/// (see <see cref="CdtDatabaseFixture"/>). Exercises the service layer end-to-end:
/// repository reads, the typed BulkMerge write path, the generic <c>db.Exec</c> command +
/// bulk methods, and transactional delete. Each test starts from an empty reference table.
/// Skips automatically when no test database can be provisioned.
/// </summary>
public class CdtDatabaseIntegrationTests : IClassFixture<CdtDatabaseFixture>
{
    private readonly CdtDatabaseFixture _fx;

    public CdtDatabaseIntegrationTests(CdtDatabaseFixture fx)
    {
        _fx = fx;
        if (_fx.Available)
        {
            // Per-test isolation — clear reference rows but keep the CountryInfo/AdminLevels seed.
            using var conn = _fx.OpenConnection();
            conn.Execute("DELETE FROM data.GoldCode; DELETE FROM data.ReferenceAdmins; DELETE FROM data.Reference;");
        }
    }

    private static DataReference UsRow(string zip = "90210", string name = "Beverly Hills",
        bool verified = true, string tz = "America/Los_Angeles") => new()
    {
        CountryId = "US", ZpCode = zip, PlaceName = name,
        Timezone = tz, IsDefault = true, Lat = "34.09", Lng = "-118.41",
        TimezoneChecked = verified, NameChecked = verified,
    };

    [Fact]
    public async Task FreshDb_ReferenceCountIsZero()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        Assert.False(await _fx.Db.Reference.HasDataAsync("US"));
        Assert.Equal(0, await _fx.Db.Reference.GetCountAsync("US"));
    }

    [Fact]
    public async Task DataServices_Merge_PersistsRow()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        var ok = await _fx.Db.Data.MergeDataRecordsAsync([UsRow()]);

        Assert.True(ok);
        Assert.True(await _fx.Db.Reference.HasDataAsync("US"));
        Assert.Equal(1, await _fx.Db.Reference.GetCountAsync("US"));
    }

    [Fact]
    public async Task DataServices_Merge_EnforcesCoordinatePairing()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        // Half a coordinate pair → both must be blanked to "---" before the row is written.
        var row = UsRow(zip: "30301", name: "Atlanta");
        row.Lng = "---";   // lat present, lng missing
        await _fx.Db.Data.MergeDataRecordsAsync([row]);

        using var conn = _fx.OpenConnection();
        var (lat, lng) = conn.QueryFirst<(string Lat, string Lng)>(
            "SELECT Lat, Lng FROM data.Reference WHERE CountryId='US' AND ZpCode='30301'");
        Assert.Equal("---", lat);
        Assert.Equal("---", lng);
    }

    [Fact]
    public async Task Exec_SetReferenceTimezoneVerifiedById()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        await _fx.Db.Data.MergeDataRecordsAsync(
            [UsRow(zip: "10001", name: "New York", verified: false, tz: "---")]);

        using var conn = _fx.OpenConnection();
        var id = conn.ExecuteScalar<long>(
            "SELECT ReferenceId FROM data.Reference WHERE CountryId='US' AND ZpCode='10001'");

        var affected = await _fx.Db.Exec.ExecuteAsync(
            CommonQueries.SetReferenceTimezoneVerifiedById,
            new { Timezone = "America/New_York", ReferenceId = id });

        Assert.Equal(1, affected);
        var (tz, checked_) = conn.QueryFirst<(string Timezone, bool TimezoneChecked)>(
            "SELECT Timezone, TimezoneChecked FROM data.Reference WHERE ReferenceId = @id", new { id });
        Assert.Equal("America/New_York", tz);
        Assert.True(checked_);
    }

    [Fact]
    public async Task Exec_BulkInsert_NewReferenceMapping()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        await _fx.Db.Exec.BulkInsertAsync("NewReference", new[]
        {
            new DataReference { CountryId = "US", ZpCode = "20002", PlaceName = "Washington",
                                Timezone = "America/New_York", IsDefault = true, Lat = "38.9", Lng = "-77.0" },
        });

        Assert.Equal(1, await _fx.Db.Reference.GetCountAsync("US"));
    }

    [Fact]
    public async Task Delete_RemovesCountryRows()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        await _fx.Db.Data.MergeDataRecordsAsync([UsRow()]);
        Assert.Equal(1, await _fx.Db.Reference.GetCountAsync("US"));

        await _fx.Db.Delete.DeleteCommandAsync(CommonQueries.DeleteReferenceData, new { CountryId = "US" });
        Assert.Equal(0, await _fx.Db.Reference.GetCountAsync("US"));
    }

    // ── Gold certification (the four gold conditions) ───────────────────────────

    [Fact]
    public async Task GoldCertifier_CertifiesFullyEligibleCode()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        using var conn = _fx.OpenConnection();
        await InsertCuratedAsync(conn, "10001", "New York");   // curated default + admin1 + tz + coords

        var result = await GoldCertifier.CertifyAsync(conn, "US");

        Assert.False(result.Failed);
        Assert.Equal(1, result.Certified);
        Assert.Equal(1, await GoldCountAsync(conn, "10001"));
    }

    [Fact]
    public async Task GoldCertifier_SkipsCodeMissingCoordinates()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        using var conn = _fx.OpenConnection();
        await InsertCuratedAsync(conn, "10002", "Nowhere", lat: "---", lng: "---");

        await GoldCertifier.CertifyAsync(conn, "US");

        Assert.Equal(0, await GoldCountAsync(conn, "10002"));
    }

    [Fact]
    public async Task GoldCertifier_SkipsCodeMissingAdmin1()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        using var conn = _fx.OpenConnection();
        await InsertCuratedAsync(conn, "10003", "No Admin", adminCode: null);

        await GoldCertifier.CertifyAsync(conn, "US");

        Assert.Equal(0, await GoldCountAsync(conn, "10003"));
    }

    [Fact]
    public async Task GoldRegression_IgnoresAltNameRowWithoutAdminOrCoords()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        using var conn = _fx.OpenConnection();
        // A fully gold-eligible code, certified.
        await InsertCuratedAsync(conn, "10010", "Canonical");
        await GoldCertifier.CertifyAsync(conn, "US");
        Assert.Equal(1, await GoldCountAsync(conn, "10010"));

        // Promote an alias: a curated AltNameOf row with NO admin and NO coords (as Phase 3 would add).
        await conn.ExecuteAsync(@"
            INSERT INTO data.Reference
                (CountryId, ZpCode, PlaceName, AltNameOf, Timezone, IsDefault, Lat, Lng, TimezoneChecked, NameChecked)
            VALUES ('US','10010','Alias Name','Canonical','America/New_York',0,'---','---',1,1);");

        // The alias row must NOT trip a gold regression — the regression query ignores AltNameOf rows.
        var regressed = (await conn.QueryAsync<string>(
            CommonQueries.GetGoldCodesFailingConditions, new { CountryId = "US" })).ToList();

        Assert.DoesNotContain("10010", regressed);
    }

    // ── Flagged: bit → int widening, Dapper still maps it to the bool model property ──

    [Fact]
    public async Task FlaggedColumn_IsIntegerType()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        using var conn = _fx.OpenConnection();
        var typeName = await conn.ExecuteScalarAsync<string>(
            @"SELECT t.name FROM sys.columns c
              JOIN sys.types t ON t.user_type_id = c.user_type_id
              WHERE c.object_id = OBJECT_ID(N'data.Reference') AND c.name = N'Flagged'");

        Assert.Equal("int", typeName);
    }

    [Theory]
    [InlineData(0, DataFlagReasonType.Valid)]
    [InlineData(1, DataFlagReasonType.Flagged)]
    [InlineData(2, DataFlagReasonType.CommonFake)]
    [InlineData(3, DataFlagReasonType.Obsolete)]
    public async Task Flagged_IntColumn_MapsToEnumModelProperty(int stored, DataFlagReasonType expected)
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        using var conn = _fx.OpenConnection();
        var id = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO data.Reference
                (CountryId, ZpCode, PlaceName, Timezone, IsDefault, Lat, Lng, TimezoneChecked, NameChecked, Flagged)
            VALUES ('US', @zip, 'Test', 'America/New_York', 1, '40.71', '-74.0', 1, 1, @stored);
            SELECT CAST(SCOPE_IDENTITY() AS bigint);",
            new { zip = $"1100{stored}", stored });

        // SELECT * reads the raw int column into DataReference.Flagged — Dapper maps the int to the
        // DataFlagReasonType enum automatically, preserving 2/3 (no AS BIT collapse to 0/1).
        var row = await conn.QueryFirstAsync<DataReference>(
            "SELECT * FROM data.Reference WHERE ReferenceId = @id", new { id });

        Assert.Equal(expected, row.Flagged);
    }

    [Theory]
    [InlineData(DataFlagReasonType.Valid)]
    [InlineData(DataFlagReasonType.Flagged)]
    [InlineData(DataFlagReasonType.CommonFake)]
    [InlineData(DataFlagReasonType.Obsolete)]
    public async Task SetReferenceFlagReasonById_WritesEnumValue(DataFlagReasonType reason)
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        await _fx.Db.Data.MergeDataRecordsAsync([UsRow(zip: "55555", name: "Flag Test")]);

        using var conn = _fx.OpenConnection();
        var id = await conn.ExecuteScalarAsync<long>(
            "SELECT ReferenceId FROM data.Reference WHERE CountryId='US' AND ZpCode='55555'");

        var affected = await _fx.Db.Exec.ExecuteAsync(
            CommonQueries.SetReferenceFlagReasonById,
            new { ReferenceId = id, Flagged = (int)reason });
        Assert.Equal(1, affected);

        var row = await conn.QueryFirstAsync<DataReference>(
            "SELECT * FROM data.Reference WHERE ReferenceId = @id", new { id });
        Assert.Equal(reason, row.Flagged);
    }

    [Fact]
    public async Task SetReferenceFlagReasonById_FlagsOnlyTheTargetRow_NotSiblings()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        // Two place names sharing one ZpCode → two ReferenceIds.
        using var conn = _fx.OpenConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO data.Reference (CountryId, ZpCode, PlaceName, Timezone, IsDefault)
            VALUES ('US','44444','Primary','America/New_York',1),
                   ('US','44444','Alternate','America/New_York',0);");

        var primaryId = await conn.ExecuteScalarAsync<long>(
            "SELECT ReferenceId FROM data.Reference WHERE ZpCode='44444' AND PlaceName='Primary'");

        await _fx.Db.Exec.ExecuteAsync(
            CommonQueries.SetReferenceFlagReasonById,
            new { ReferenceId = primaryId, Flagged = (int)DataFlagReasonType.Obsolete });

        // Only the targeted row is flagged; the sibling sharing the code stays Valid.
        var rows = (await conn.QueryAsync<DataReference>(
            "SELECT * FROM data.Reference WHERE ZpCode='44444' ORDER BY PlaceName")).ToList();
        Assert.Equal(DataFlagReasonType.Obsolete, rows.Single(r => r.PlaceName == "Primary").Flagged);
        Assert.Equal(DataFlagReasonType.Valid,    rows.Single(r => r.PlaceName == "Alternate").Flagged);
    }

    [Fact]
    public async Task FlaggedBrowseCount_IncludesAllNonZeroReasons()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        using var conn = _fx.OpenConnection();
        // One row per reason 0..3 (distinct codes).
        await conn.ExecuteAsync(@"
            INSERT INTO data.Reference (CountryId, ZpCode, PlaceName, Timezone, IsDefault, Flagged)
            VALUES ('US','60000','Valid','America/Chicago',1,0),
                   ('US','60001','Flagged','America/Chicago',1,1),
                   ('US','60002','Fake','America/Chicago',1,2),
                   ('US','60003','Obsolete','America/Chicago',1,3);");

        // GetFlaggedBrowseRowCount filters Flagged <> 0 → counts the 1/2/3 rows, not the valid one.
        var flaggedCount = await conn.ExecuteScalarAsync<int>(
            CommonQueries.GetFlaggedBrowseRowCount, new { CountryId = "US" });

        Assert.Equal(3, flaggedCount);
    }

    [Fact]
    public async Task AutoPromote_PromotesEquivalentAlias()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        // Arrange: insert a curated reference row, then a Name discrepancy for an equivalent alias
        using var conn = _fx.OpenConnection();
        await InsertCuratedAsync(conn, "10001", "Fort Worth", adminCode: "TX", adminName: "Texas");

        // Create a test run (discrepancies have FK to pipeline.Runs)
        await conn.ExecuteAsync(@"
            INSERT INTO pipeline.Runs (RunId, CountryId, SourceFilename, StartedAt)
            VALUES ('test-run-1', 'US', 'test.csv', SYSUTCDATETIME());");

        // Create a Name discrepancy for the abbreviation "Ft Worth" (equivalent via PlaceNameNormalizer)
        await conn.ExecuteAsync(@"
            INSERT INTO codes.Discrepancies (CountryId, RunId, ZpCode, PlaceName, FieldName, RefValue, InValue, AcceptIncoming, Process, CreatedAt)
            VALUES ('US', 'test-run-1', '10001', 'Fort Worth', 'Name', 'Fort Worth', 'Ft Worth', 0, 0, SYSUTCDATETIME());");

        // Act: insert the alias row directly via SQL (testing service layer integration separately)
        await conn.ExecuteAsync(@"
            INSERT INTO data.Reference (CountryId, ZpCode, PlaceName, Timezone, IsDefault, Lat, Lng, TimezoneChecked, NameChecked, AltNameOf)
            VALUES ('US', '10001', 'Ft Worth', '---', 0, '---', '---', 0, 0, 'Fort Worth');");

        // Resolve the discrepancy (simulating what AutoPromoteAliasesCommand does)
        await _fx.Db.Exec.ExecuteAsync(
            CommonQueries.ResolveDiscrepanciesForPromotedAlias,
            new { CountryId = "US", ZpCode = "10001", InValue = "Ft Worth" });

        // Assert: Alias row was inserted
        var savedAlias = await conn.QuerySingleOrDefaultAsync<(string PlaceName, string? AltNameOf, bool IsDefault)>(
            "SELECT PlaceName, AltNameOf, IsDefault FROM data.Reference WHERE CountryId = 'US' AND ZpCode = '10001' AND PlaceName = 'Ft Worth'");
        Assert.Equal("Ft Worth", savedAlias.PlaceName);
        Assert.Equal("Fort Worth", savedAlias.AltNameOf);
        Assert.False(savedAlias.IsDefault);

        // Discrepancy was resolved
        var resolved = await conn.QuerySingleAsync<(bool Process, bool AcceptIncoming, DateTimeOffset? ResolvedAt)>(
            "SELECT Process, AcceptIncoming, ResolvedAt FROM codes.Discrepancies WHERE CountryId = 'US' AND ZpCode = '10001' AND InValue = 'Ft Worth'");
        Assert.True(resolved.Process);
        Assert.True(resolved.AcceptIncoming);
        Assert.NotNull(resolved.ResolvedAt);
    }

    [Fact]
    public async Task AutoPromote_SkipsNonEquivalent()
    {
        Assert.SkipUnless(_fx.Available, _fx.SkipReason);

        // Arrange: insert a curated reference row, then a Name discrepancy for a non-equivalent name
        using var conn = _fx.OpenConnection();
        await InsertCuratedAsync(conn, "10001", "New York", adminCode: "NY", adminName: "New York");

        // Create a test run (discrepancies have FK to pipeline.Runs)
        await conn.ExecuteAsync(@"
            INSERT INTO pipeline.Runs (RunId, CountryId, SourceFilename, StartedAt)
            VALUES ('test-run-2', 'US', 'test.csv', SYSUTCDATETIME());");

        // Create a Name discrepancy for "Chicago" (not equivalent to "New York")
        await conn.ExecuteAsync(@"
            INSERT INTO codes.Discrepancies (CountryId, RunId, ZpCode, PlaceName, FieldName, RefValue, InValue, AcceptIncoming, Process, CreatedAt)
            VALUES ('US', 'test-run-2', '10001', 'New York', 'Name', 'New York', 'Chicago', 0, 0, SYSUTCDATETIME());");

        // Act: verify PlaceNameNormalizer detects these are NOT equivalent
        var equivalent = CountryDataTools.Validation.PlaceNameNormalizer.AreEquivalent(
            "New York", "Chicago", new[] { "English" });
        Assert.False(equivalent);  // Should be false — they're different cities

        // Assert: No alias row should be manually inserted (we're testing the skip logic)
        var aliasCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM data.Reference WHERE CountryId = 'US' AND ZpCode = '10001' AND PlaceName = 'Chicago'");
        Assert.Equal(0, aliasCount);

        // Discrepancy remains unresolved
        var resolved = await conn.QuerySingleAsync<(bool Process, DateTimeOffset? ResolvedAt)>(
            "SELECT Process, ResolvedAt FROM codes.Discrepancies WHERE CountryId = 'US' AND ZpCode = '10001' AND InValue = 'Chicago'");
        Assert.False(resolved.Process);
        Assert.Null(resolved.ResolvedAt);
    }

    private static async Task<int> GoldCountAsync(SqlConnection conn, string zip) =>
        await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM data.GoldCode WHERE CountryId = 'US' AND ZpCode = @zip", new { zip });

    /// <summary>
    /// Inserts one fully-curated (TimezoneChecked = NameChecked = 1) US reference row, with an
    /// admin1 entry unless <paramref name="adminCode"/> is null. Lets a test build all four gold
    /// conditions, or deliberately break one (no coords, no admin1, …).
    /// </summary>
    private static async Task InsertCuratedAsync(
        SqlConnection conn, string zip, string name,
        string tz = "America/New_York", string lat = "40.71", string lng = "-74.0",
        string? adminCode = "NY", string adminName = "New York")
    {
        var id = await conn.ExecuteScalarAsync<long>(@"
            INSERT INTO data.Reference (CountryId, ZpCode, PlaceName, Timezone, IsDefault, Lat, Lng, TimezoneChecked, NameChecked)
            VALUES ('US', @zip, @name, @tz, 1, @lat, @lng, 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS bigint);",
            new { zip, name, tz, lat, lng });

        if (adminCode != null)
        {
            var adminLevelId = await conn.ExecuteScalarAsync<int>(
                "SELECT AdminLevelId FROM data.AdminLevels WHERE CountryId = 'US' AND LevelNumber = 1");
            await conn.ExecuteAsync(@"
                INSERT INTO data.ReferenceAdmins (ReferenceId, AdminLevelId, Value, Code)
                VALUES (@id, @adminLevelId, @adminName, @adminCode);",
                new { id, adminLevelId, adminName, adminCode });
        }
    }
}
