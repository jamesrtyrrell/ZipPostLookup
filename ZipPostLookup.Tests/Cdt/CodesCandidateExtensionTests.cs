using Xunit;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// Unit tests for <see cref="CodesCandidateExtension"/> — fanning the flat Admin1..Admin5
/// fields into AdminCandidateList (<c>BuildAdminLevels</c>) and projecting a column-mapped
/// delimited row into a candidate (<c>BuildCandidate</c>). Pure logic, no database.
/// </summary>
public class CodesCandidateExtensionTests
{
    [Fact]
    public void BuildAdminLevels_SingleLevel_AddsOneEntryKeyedByLevelNumber()
    {
        var c = new CodesCandidate { Admin1 = "Ontario", Admin1Code = "ON" }.BuildAdminLevels();

        var admin = Assert.Single(c.AdminCandidateList);
        Assert.Equal(1, admin.AdminLevelId);   // level number (FK resolved later at import)
        Assert.Equal("Ontario", admin.Value);
        Assert.Equal("ON", admin.Code);
    }

    [Fact]
    public void BuildAdminLevels_MultiLevel_AddsEntryPerPopulatedLevel()
    {
        var c = new CodesCandidate
        {
            Admin1 = "Jalisco",     Admin1Code = "JAL",
            Admin2 = "Guadalajara", Admin2Code = "GDL",
        }.BuildAdminLevels();

        Assert.Equal(2, c.AdminCandidateList.Count);
        Assert.Contains(c.AdminCandidateList, a => a.AdminLevelId == 1 && a.Code == "JAL");
        Assert.Contains(c.AdminCandidateList, a => a.AdminLevelId == 2 && a.Code == "GDL");
    }

    [Fact]
    public void BuildAdminLevels_RequiresBothValueAndCode()
    {
        var c = new CodesCandidate { Admin1 = "Ontario", Admin1Code = "" }.BuildAdminLevels();
        Assert.Empty(c.AdminCandidateList);
    }

    [Fact]
    public void BuildAdminLevels_IsIdempotent()
    {
        var c = new CodesCandidate { Admin1 = "Ontario", Admin1Code = "ON" };
        c.BuildAdminLevels();
        c.BuildAdminLevels();
        Assert.Single(c.AdminCandidateList);
    }

    [Fact]
    public void BuildCandidate_MapsColumns_DefaultsUnmappedToPlaceholder()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["ZpCode"]     = 0,
            ["PlaceName"]  = 1,
            ["Admin1"]     = 2,
            ["Admin1Code"] = 3,
        };
        var row = new[] { "44100", "Guadalajara", "Jalisco", "JAL" };

        var c = CodesCandidateExtension.BuildCandidate("mx", map, row);

        Assert.Equal("MX", c.CountryId);
        Assert.Equal("44100", c.ZpCode);
        Assert.Equal("Guadalajara", c.PlaceName);
        Assert.Equal("---", c.Timezone);    // not mapped → placeholder
        Assert.Equal("---", c.Lat);
        Assert.Equal("---", c.Lng);
        Assert.False(c.IsDefault);          // not mapped → default false
        Assert.Equal("JAL", Assert.Single(c.AdminCandidateList).Code);
    }

    [Fact]
    public void BuildCandidate_ParsesIsDefault_AndMappedCoords()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["ZpCode"]    = 0,
            ["PlaceName"] = 1,
            ["IsDefault"] = 2,
            ["Lat"]       = 3,
            ["Lng"]       = 4,
        };
        var row = new[] { "44100", "Centro", "true", "20.67", "-103.35" };

        var c = CodesCandidateExtension.BuildCandidate("MX", map, row);

        Assert.True(c.IsDefault);
        Assert.Equal("20.67", c.Lat);
        Assert.Equal("-103.35", c.Lng);
    }

    [Fact]
    public void BuildCandidate_OutOfRangeOrMissingColumn_IsBlank()
    {
        var map = new Dictionary<string, int> { ["ZpCode"] = 0, ["PlaceName"] = 9 };
        var row = new[] { "12345" };   // only one column; PlaceName index is out of range

        var c = CodesCandidateExtension.BuildCandidate("us", map, row);

        Assert.Equal("12345", c.ZpCode);
        Assert.Equal("", c.PlaceName);
    }
}
