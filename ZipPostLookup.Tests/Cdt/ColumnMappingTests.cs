using Xunit;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// Unit tests for the column-mapping template (<c>ColumnMapping</c>) — the universal
/// CodesCandidate-derived field set, the per-page star sets, header prefill, and the
/// accept-time column map. Internal type, reached via InternalsVisibleTo.
/// </summary>
public class ColumnMappingTests
{
    [Fact]
    public void ForTemplate_AlwaysStarsZpCode()
    {
        var m = ColumnMapping.ForTemplate();   // no extra stars requested
        Assert.True(m.Fields.Single(f => f.Name == nameof(CodesCandidate.ZpCode)).Mandatory);
    }

    [Fact]
    public void ForTemplate_StarsRequestedFieldsOnly()
    {
        var m = ColumnMapping.ForTemplate(nameof(CodesCandidate.Lat), nameof(CodesCandidate.Lng));
        Assert.True(m.Fields.Single(f => f.Name == "Lat").Mandatory);
        Assert.True(m.Fields.Single(f => f.Name == "Lng").Mandatory);
        Assert.False(m.Fields.Single(f => f.Name == "PlaceName").Mandatory);
    }

    [Fact]
    public void ForIngestion_StarsOnlyPlaceNameAndZpCode()
    {
        var m = ColumnMapping.ForIngestion();
        var mandatory = m.Fields.Where(f => f.Mandatory).Select(f => f.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "PlaceName", "ZpCode" }, mandatory);

        // Country-derived / defaulted fields stay present but optional.
        Assert.False(m.Fields.Single(f => f.Name == "Admin1").Mandatory);
        Assert.False(m.Fields.Single(f => f.Name == "Timezone").Mandatory);
        Assert.False(m.Fields.Single(f => f.Name == "IsDefault").Mandatory);
    }

    [Fact]
    public void Template_ExcludesSystemFields_IncludesAllAdminLevels()
    {
        var names = ColumnMapping.ForTemplate().Fields.Select(f => f.Name).ToHashSet();
        Assert.DoesNotContain(nameof(CodesCandidate.CandidateId), names);
        Assert.DoesNotContain(nameof(CodesCandidate.RunId), names);
        Assert.DoesNotContain(nameof(CodesCandidate.Status), names);
        Assert.Contains(nameof(CodesCandidate.Admin1), names);
        Assert.Contains(nameof(CodesCandidate.Admin5), names);   // multi-level fields exposed
    }

    [Fact]
    public void BindByHeader_BindsByNameCaseInsensitive()
    {
        var m = ColumnMapping.ForIngestion();
        m.BindByHeader(new[] { "zpcode", "placename", "junk", "Admin1" });

        var map = m.ToColumnMap();
        Assert.Equal(0, map["ZpCode"]);
        Assert.Equal(1, map["PlaceName"]);
        Assert.Equal(3, map["Admin1"]);
        Assert.False(map.ContainsKey("Timezone"));   // no matching header column
    }

    [Fact]
    public void AllMandatoryMapped_FalseUntilStarsBound()
    {
        var m = ColumnMapping.ForIngestion();
        Assert.False(m.AllMandatoryMapped);

        m.BindByHeader(new[] { "ZpCode", "PlaceName" });
        Assert.True(m.AllMandatoryMapped);
    }

    [Fact]
    public void ToColumnMap_IncludesOnlyMappedFields()
    {
        var m = ColumnMapping.ForIngestion();
        m.BindByHeader(new[] { "ZpCode", "PlaceName" });
        Assert.Equal(2, m.ToColumnMap().Count);
    }
}
