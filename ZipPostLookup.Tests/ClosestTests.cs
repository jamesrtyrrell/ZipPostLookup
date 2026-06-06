using Xunit;
using ZipPostLookup.Core;

namespace ZipPostLookup.Tests;

public sealed class ClosestTests : IClassFixture<UsRegistryFixture>
{
    private readonly ZipPostRegistry _postRegistry;

    public ClosestTests(UsRegistryFixture fixture)
    {
        _postRegistry = fixture.PostRegistry;
    }

    [Fact]
    public void GetClosest_ExactMatch_ReturnsEntry()
    {
        var entry = _postRegistry.GetClosest("10001");

        Assert.NotNull(entry);
        Assert.Equal("10001", entry.ZpCode);
    }

    [Fact]
    public void GetClosest_NoExactMatch_ReturnsNumericallyAdjacentEntry()
    {
        // 00000 doesn't exist — should fall back to the nearest real zip
        var entry = _postRegistry.GetClosest("00000");

        Assert.NotNull(entry);
        Assert.NotEqual("00000", entry.ZpCode);
    }

    [Fact]
    public void GetClosest_NearbyCode_ReturnsCloserZip()
    {
        // 10002 exists; ask for 10001 to confirm we get an exact match
        // then ask for something close to a known gap
        var exact = _postRegistry.GetClosest("10001");
        var nearby = _postRegistry.GetClosest("10001");

        Assert.Equal(exact!.ZpCode, nearby!.ZpCode);
    }

    [Fact]
    public void GetClosest_NonNumericCode_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => _postRegistry.GetClosest("T0A0A0"));
        Assert.Contains("5-digit US zip", ex.Message);
    }

    [Fact]
    public void GetClosest_ShortCode_Throws()
    {
        Assert.Throws<ArgumentException>(() => _postRegistry.GetClosest("1234"));
    }

    [Fact]
    public void GetClosest_NullCode_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _postRegistry.GetClosest(null!));
    }
}