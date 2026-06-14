using Xunit;
using ZipPostLookup.CountryDataTools.Export;
using ZipPostLookup.CountryDataTools.Export.Stages;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// Unit tests for the export pipeline stages — range compression (homogeneous-prefix collapse
/// with a significance gate) and string indexing (timezone + admin1 lookup tables). Internal
/// types, reached via InternalsVisibleTo. Pure transforms.
/// </summary>
public class ExportStageTests
{
    private static ExportRow Row(string zip, string name = "Town", string tz = "America/Toronto",
        string admin = "Ontario", string adminCode = "ON") => new()
    {
        ZpCode = zip, PlaceName = name, Timezone = tz, IsDefault = true,
        Admin1 = admin, Admin1Code = adminCode,
    };

    // ── RangeCompressStage ──────────────────────────────────────────────────────

    [Fact]
    public void RangeCompress_HomogeneousGroup_CollapsesToRangeRow()
    {
        var rows = new List<ExportRow> { Row("A1A1A1"), Row("A1A1B2"), Row("A1A2C3"), Row("A1A9Z9") };

        var (result, _) = new RangeCompressStage("CA").Apply(rows, new ExportMeta());

        var r = Assert.Single(result);   // 4 homogeneous A1A* rows → one range row
        Assert.Equal("A1A1**:A1A9**", r.ZpCode);
        Assert.True(r.IsDefault);
        Assert.Equal("ON", r.Admin1Code);
    }

    [Fact]
    public void RangeCompress_HeterogeneousGroup_NotCompressed()
    {
        var rows = new List<ExportRow> { Row("A1A1A1", name: "Alpha"), Row("A1A1B2", name: "Bravo") };

        var (result, _) = new RangeCompressStage("CA").Apply(rows, new ExportMeta());

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, r => r.ZpCode.Contains(':'));
    }

    [Fact]
    public void RangeCompress_BelowSignificanceThreshold_PassesThrough()
    {
        var rows = new List<ExportRow> { Row("A1A1A1"), Row("A1A1B2") };   // 50% reduction available

        // Threshold above the achievable reduction → keep all codes as exact rows.
        var (result, _) = new RangeCompressStage("CA", minReductionRatio: 0.99).Apply(rows, new ExportMeta());

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, r => r.ZpCode.Contains(':'));
    }

    [Fact]
    public void RangeCompress_SingletonGroups_Unchanged()
    {
        var rows = new List<ExportRow> { Row("A1A1A1"), Row("B2B2B2") };   // distinct prefixes

        var (result, _) = new RangeCompressStage("CA").Apply(rows, new ExportMeta());

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, r => r.ZpCode.Contains(':'));
    }

    // ── StringIndexStage ────────────────────────────────────────────────────────

    [Fact]
    public void StringIndex_BuildsSortedUniqueTables()
    {
        var rows = new List<ExportRow>
        {
            Row("A", tz: "America/Toronto",   admin: "Ontario",          adminCode: "ON"),
            Row("B", tz: "America/Vancouver", admin: "British Columbia", adminCode: "BC"),
            Row("C", tz: "America/Toronto",   admin: "Ontario",          adminCode: "ON"),
        };

        var (_, meta) = new StringIndexStage().Apply(rows, new ExportMeta());

        Assert.Equal(new[] { "America/Toronto", "America/Vancouver" }, meta.TimezoneIndex);
        Assert.Equal(new[] { ("BC", "British Columbia"), ("ON", "Ontario") }, meta.AdminIndex);
        Assert.True(meta.IsIndexed);
    }

    [Fact]
    public void StringIndex_EmptyRows_LeavesMetaUnindexed()
    {
        var (_, meta) = new StringIndexStage().Apply(new List<ExportRow>(), new ExportMeta());

        Assert.Null(meta.TimezoneIndex);
        Assert.False(meta.IsIndexed);
    }
}
