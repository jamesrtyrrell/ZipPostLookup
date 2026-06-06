using Xunit;
using ZipPostLookup.Core;

namespace ZipPostLookup.Tests;

public sealed class CaLookupTests : IClassFixture<CaRegistryFixture>
{
    private readonly ZipPostRegistry _postRegistry;

    public CaLookupTests(CaRegistryFixture fixture)
    {
        _postRegistry = fixture.PostRegistry;
    }

    // -------------------------------------------------------------------------
    // GetByZip — individual LDU codes (heterogeneous FSAs kept as exact rows)
    // -------------------------------------------------------------------------

    [Fact]
    public void GetByZip_Alberta_ReturnsCorrectEntry()
    {
        // T0A is a heterogeneous FSA (rural AB, many different towns per LDU)
        // — kept as individual rows, not compressed to a range
        var entry = _postRegistry.GetByZip("T0A0A0");

        Assert.NotNull(entry);
        Assert.Equal("Abee", entry.PlaceName);
        Assert.Equal("AB", entry.Admin1Code);
        Assert.Equal("Alberta", entry.Admin1);
        Assert.Equal("America/Edmonton", entry.Timezone);
    }

    [Fact]
    public void GetByZip_UnknownCode_ReturnsNull()
    {
        Assert.Null(_postRegistry.GetByZip("Z9Z9Z9"));
    }

    // -------------------------------------------------------------------------
    // GetByZip — range lookups (homogeneous FSAs compressed to a single range row)
    // Any specific LDU lookup within a compressed FSA resolves via the range index.
    // -------------------------------------------------------------------------

    [Fact]
    public void GetByZip_CompressedFSA_Toronto_ResolvesViaRange()
    {
        // M5V is a homogeneous FSA (all Toronto, ON) — stored as a range row
        var entry = _postRegistry.GetByZip("M5V3L9");

        Assert.NotNull(entry);
        Assert.Equal("Toronto", entry.PlaceName);
        Assert.Equal("ON", entry.Admin1Code);
        Assert.Equal("Ontario", entry.Admin1);
        Assert.Equal("America/Toronto", entry.Timezone);
    }

    [Fact]
    public void GetByZip_CompressedFSA_Vancouver_ResolvesViaRange()
    {
        // V6B is a homogeneous FSA (all Vancouver, BC) — stored as a range row
        var entry = _postRegistry.GetByZip("V6B1A1");

        Assert.NotNull(entry);
        Assert.Equal("Vancouver", entry.PlaceName);
        Assert.Equal("BC", entry.Admin1Code);
        Assert.Equal("British Columbia", entry.Admin1);
        Assert.Equal("America/Vancouver", entry.Timezone);
    }

    [Fact]
    public void GetByZip_CaseInsensitive_ReturnsEntry()
    {
        var upper = _postRegistry.GetByZip("T0A0A0");
        var lower = _postRegistry.GetByZip("t0a0a0");

        Assert.NotNull(upper);
        Assert.NotNull(lower);
        Assert.Equal(upper.PlaceName, lower.PlaceName);
    }

    // -------------------------------------------------------------------------
    // GetByState (province abbreviation)
    // -------------------------------------------------------------------------

    [Fact]
    public void GetByState_Ontario_ReturnsEntries()
    {
        var entries = _postRegistry.GetByState("ON");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("ON", e.Admin1Code));
    }

    [Fact]
    public void GetByState_BritishColumbia_ReturnsEntries()
    {
        var entries = _postRegistry.GetByState("BC");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("BC", e.Admin1Code));
    }

    [Fact]
    public void GetByState_Saskatchewan_ReturnsEntries()
    {
        var entries = _postRegistry.GetByState("SK");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("SK", e.Admin1Code));
    }

    [Fact]
    public void GetByState_NewfoundlandAndLabrador_ReturnsEntries()
    {
        var entries = _postRegistry.GetByState("NL");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("NL", e.Admin1Code));
    }

    // -------------------------------------------------------------------------
    // GetByStateName (province full name)
    // -------------------------------------------------------------------------

    [Fact]
    public void GetByStateName_Alberta_ReturnsEntries()
    {
        var entries = _postRegistry.GetByStateName("Alberta");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("Alberta", e.Admin1));
    }

    [Fact]
    public void GetByStateName_CaseInsensitive_ReturnsResults()
    {
        var normal = _postRegistry.GetByStateName("British Columbia");
        var lower  = _postRegistry.GetByStateName("british columbia");

        Assert.Equal(normal.Count, lower.Count);
    }

    // -------------------------------------------------------------------------
    // GetByTimeZone (IANA)
    // -------------------------------------------------------------------------

    [Fact]
    public void GetByTimeZone_Edmonton_ReturnsEntries()
    {
        var entries = _postRegistry.GetByTimeZone("America/Edmonton");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("America/Edmonton", e.Timezone));
    }

    [Fact]
    public void GetByTimeZone_Regina_ReturnsEntries()
    {
        // Saskatchewan — no DST
        var entries = _postRegistry.GetByTimeZone("America/Regina");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("SK", e.Admin1Code));
    }

    [Fact]
    public void GetByTimeZone_StJohns_ReturnsEntries()
    {
        // Newfoundland — UTC-3:30
        var entries = _postRegistry.GetByTimeZone("America/St_Johns");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("NL", e.Admin1Code));
    }

    // -------------------------------------------------------------------------
    // GetByCity
    // -------------------------------------------------------------------------

    [Fact]
    public void GetByCity_Toronto_ReturnsEntries()
    {
        var entries = _postRegistry.GetByCity("Toronto");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("Toronto", e.PlaceName));
    }

    // -------------------------------------------------------------------------
    // GetAll
    // -------------------------------------------------------------------------

    [Fact]
    public void GetAll_ReturnsNonEmptyList()
    {
        Assert.NotEmpty(_postRegistry.GetAll());
    }

    [Fact]
    public void GetAll_ContainsNoUsZipCodes()
    {
        // All CA entries (individual LDUs and range-expanded codes) should have
        // codes starting with a letter — no 5-digit US zip codes
        var all = _postRegistry.GetAll();

        Assert.All(all, e =>
        {
            Assert.False(
                e.ZpCode.Length == 5 && e.ZpCode.All(char.IsDigit),
                $"Found US-style zip code in CA registry: '{e.ZpCode}'");
        });
    }

    // -------------------------------------------------------------------------
    // GetRandom
    // -------------------------------------------------------------------------

    [Fact]
    public void GetRandom_ReturnsValidEntry()
    {
        var entry = _postRegistry.GetRandom();

        Assert.NotNull(entry);
        Assert.NotEmpty(entry.PlaceName);
        Assert.NotEmpty(entry.Admin1Code!);
    }
}