using Xunit;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// Unit tests for the lat/lng pairing rule on <see cref="DataReference"/> — coordinates are
/// only ever stored as a complete pair; a half-populated pair is blanked to "---". Guards
/// against the fault that once left ~735k CA rows with a latitude but no longitude.
/// </summary>
public class DataReferenceTests
{
    [Fact]
    public void NormalizeCoordinatePair_CompletePair_IsUnchanged()
    {
        var r = new DataReference { Lat = "19.43", Lng = "-99.13" };

        Assert.False(r.HasIncompleteCoordinates());
        Assert.False(r.NormalizeCoordinatePair());   // nothing to do
        Assert.Equal("19.43", r.Lat);
        Assert.Equal("-99.13", r.Lng);
    }

    [Theory]
    [InlineData("19.43", "---")]
    [InlineData("---", "-99.13")]
    [InlineData("19.43", "")]
    [InlineData("", "-99.13")]
    [InlineData("notanumber", "1.0")]
    public void NormalizeCoordinatePair_HalfOrInvalidPair_BlanksBoth(string lat, string lng)
    {
        var r = new DataReference { Lat = lat, Lng = lng };

        Assert.True(r.HasIncompleteCoordinates());
        Assert.True(r.NormalizeCoordinatePair());    // changed the row
        Assert.Equal("---", r.Lat);
        Assert.Equal("---", r.Lng);
    }

    [Fact]
    public void NormalizeCoordinatePair_BothPlaceholder_NoChange()
    {
        var r = new DataReference { Lat = "---", Lng = "---" };

        Assert.True(r.HasIncompleteCoordinates());   // placeholders are "incomplete"
        Assert.False(r.NormalizeCoordinatePair());   // but already normalised → no change
        Assert.Equal("---", r.Lat);
        Assert.Equal("---", r.Lng);
    }
}
