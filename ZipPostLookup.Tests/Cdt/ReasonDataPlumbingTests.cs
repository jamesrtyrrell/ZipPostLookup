using System.Reflection;
using Xunit;
using ZipPostLookup.Core;
using ZipPostLookup.CountryDataTools.Export;
using ZipPostLookup.CountryDataTools.Export.Stages;
using ZipPostLookup.CountryDataTools.Export.ZpImage;
using ZipPostLookup.CountryDataTools.Models.Enums;
using ZipPostLookup.ZPImage;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// End-to-end verification that a per-row <see cref="DataFlagReasonType"/> set by the export
/// pipeline actually reaches the shipped library as <see cref="CodeReason"/> — through both
/// artifacts the pipeline produces. Exercises the real CDT writers
/// (<see cref="ZpImageBuilder"/>, <see cref="CsvExporter"/>) against the real library readers
/// (<see cref="ZpImageLookup"/>, and the private CSV parser behind <c>BuiltInDataSource</c>).
/// </summary>
public sealed class ReasonDataPlumbingTests
{
    private static List<ExportRow> BuildRows() => new()
    {
        Row("99901", DataFlagReasonType.Valid),
        Row("99902", DataFlagReasonType.Flagged),
        Row("99903", DataFlagReasonType.CommonFake),
        Row("99904", DataFlagReasonType.Obsolete),
    };

    private static ExportRow Row(string code, DataFlagReasonType reason) => new()
    {
        ZpCode = code, PlaceName = "Test Town", Timezone = "America/Chicago",
        IsDefault = true, Admin1 = "Illinois", Admin1Code = "IL", Reason = reason,
    };

    // ── ZP binary image ─────────────────────────────────────────────────────────

    [Fact]
    public void ZpImage_RoundTrips_ReasonByte_ForEveryCode()
    {
        var build = ZpImageBuilder.Build("US", BuildRows());
        var image = ZpImageLookup.FromImage(build.Bytes);

        Assert.Equal(CodeReason.None,       image.GetByCode("99901")!.Reason);
        Assert.Equal(CodeReason.Flagged,    image.GetByCode("99902")!.Reason);
        Assert.Equal(CodeReason.CommonFake, image.GetByCode("99903")!.Reason);
        Assert.Equal(CodeReason.Obsolete,   image.GetByCode("99904")!.Reason);
    }

    [Fact]
    public void ZpImage_ThrowEnabled_ThrowsOnlyForObsoleteAndCommonFake()
    {
        var build = ZpImageBuilder.Build("US", BuildRows());
        var image = ZpImageLookup.FromImage(build.Bytes);
        image.ThrowReasonExceptions(true);

        Assert.NotNull(image.GetByCode("99901")); // Valid/None
        Assert.NotNull(image.GetByCode("99902")); // Flagged — generic, does not throw
        Assert.Throws<CommonFakeDataException>(() => image.GetByCode("99903"));
        Assert.Throws<ObsoleteCodeException>(() => image.GetByCode("99904"));
    }

    // ── CSV (the format read by BuiltInDataSource) ──────────────────────────────

    [Fact]
    public async Task Csv_RoundTrips_ReasonColumn_ThroughTheRealParser()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zpl-reason-{Guid.NewGuid():N}.csv");
        try
        {
            await CsvExporter.WriteAsync(BuildRows(), new ExportMeta { IncludeCoords = false }, path);
            var lines = File.ReadAllLines(path);

            // Line 0 = header ("Code,Name,Timezone,IsDefault,Admin1,Admin1Code,Reason").
            var parsed = lines.Skip(1).Select(ParseViaBuiltInDataSource).ToList();

            Assert.Equal(CodeReason.None,       parsed[0]!.Reason);
            Assert.Equal(CodeReason.Flagged,    parsed[1]!.Reason);
            Assert.Equal(CodeReason.CommonFake, parsed[2]!.Reason);
            Assert.Equal(CodeReason.Obsolete,   parsed[3]!.Reason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Invokes the actual private parser behind the shipped library's <c>BuiltInDataSource</c>
    /// via reflection — there is no public entry point for parsing an arbitrary CSV file (only
    /// embedded resources), so this is the only way to prove the real parsing code, not a
    /// reimplementation of it, understands the CSV the export pipeline writes.
    /// </summary>
    private static CodeEntry? ParseViaBuiltInDataSource(string line)
    {
        var type = typeof(CodeEntry).Assembly.GetType("ZipPostLookup.Sources.BuiltInDataSource")!;
        var method = type.GetMethod("ParseLine", BindingFlags.NonPublic | BindingFlags.Static)!;
        var levelNames = new[] { "State" };
        return (CodeEntry?)method.Invoke(null, new object?[] { line, levelNames, null, null });
    }

    // ── RangeCompressStage carries Reason through a merged range row ────────────

    [Fact]
    public void RangeCompress_HomogeneousExceptReason_NotCompressed()
    {
        var rows = new List<ExportRow>
        {
            Row("A1A1A1", DataFlagReasonType.Valid),
            Row("A1A1B2", DataFlagReasonType.Obsolete),
        };

        var (result, _) = new RangeCompressStage("CA").Apply(rows, new ExportMeta());

        Assert.Equal(2, result.Count); // differing Reason blocks the merge
        Assert.DoesNotContain(result, r => r.ZpCode.Contains(':'));
    }

    [Fact]
    public void RangeCompress_HomogeneousReason_CompressedRowCarriesIt()
    {
        var rows = new List<ExportRow>
        {
            Row("A1A1A1", DataFlagReasonType.Obsolete),
            Row("A1A1B2", DataFlagReasonType.Obsolete),
            Row("A1A9Z9", DataFlagReasonType.Obsolete),
        };

        var (result, _) = new RangeCompressStage("CA").Apply(rows, new ExportMeta());

        var r = Assert.Single(result);
        Assert.Equal(DataFlagReasonType.Obsolete, r.Reason);
    }
}
