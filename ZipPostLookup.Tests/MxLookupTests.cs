using Xunit;
using ZipPostLookup.Core;

namespace ZipPostLookup.Tests;

public sealed class MxLookupTests : IClassFixture<MxRegistryFixture>
{
    private readonly ZipPostRegistry _postRegistry;

    public MxLookupTests(MxRegistryFixture fixture)
    {
        _postRegistry = fixture.PostRegistry;
    }

    // -------------------------------------------------------------------------
    // GetByZip
    // MX data is colonia-level — Name is the colonia, Admin1 is the estado.
    // Note: MX timezone data has not been fully enriched — most entries use
    // America/Mexico_City as a placeholder. Timezone tests are limited to
    // states with confirmed distinct timezones.
    // -------------------------------------------------------------------------

    [Fact]
    public void GetByZip_Aguascalientes_ReturnsCorrectEntry()
    {
        // 20000 is Zona Centro colonia in Aguascalientes state
        var entry = _postRegistry.GetByZip("20000");

        Assert.NotNull(entry);
        Assert.Equal("20000", entry.ZpCode);
        Assert.Equal("Zona Centro", entry.PlaceName);
        Assert.Equal("AGS", entry.Admin1Code);
        Assert.Equal("Aguascalientes", entry.Admin1);
    }

    [Fact]
    public void GetByZip_Monterrey_ReturnsAnEntryInNuevoLeon()
    {
        var entry = _postRegistry.GetByZip("64000");

        Assert.NotNull(entry);
        Assert.Equal("NLE", entry.Admin1Code);
        Assert.Equal("Nuevo León", entry.Admin1);
    }

    [Fact]
    public void GetByZip_Tijuana_ReturnsAnEntryInBajaCalifornia()
    {
        var entry = _postRegistry.GetByZip("22000");

        Assert.NotNull(entry);
        Assert.Equal("BC", entry.Admin1Code);
        Assert.Equal("Baja California", entry.Admin1);
    }

    [Fact]
    public void GetByZip_UnknownCode_ReturnsNull()
    {
        Assert.Null(_postRegistry.GetByZip("00000"));
    }

    // -------------------------------------------------------------------------
    // GetByState
    // -------------------------------------------------------------------------

    [Fact]
    public void GetByState_Jalisco_ReturnsEntries()
    {
        var entries = _postRegistry.GetByState("JAL");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("JAL", e.Admin1Code));
    }

    [Fact]
    public void GetByState_BajaCalifornia_ReturnsEntries()
    {
        var entries = _postRegistry.GetByState("BC");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("BC", e.Admin1Code));
    }

    // -------------------------------------------------------------------------
    // GetByStateName
    // -------------------------------------------------------------------------

    [Fact]
    public void GetByStateName_NuevoLeon_ReturnsEntries()
    {
        var entries = _postRegistry.GetByStateName("Nuevo León");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("NLE", e.Admin1Code));
    }

    [Fact]
    public void GetByStateName_CaseInsensitive_ReturnsResults()
    {
        var normal = _postRegistry.GetByStateName("Jalisco");
        var lower = _postRegistry.GetByStateName("jalisco");

        Assert.Equal(normal.Count, lower.Count);
    }

    // -------------------------------------------------------------------------
    // GetByTimeZone
    // -------------------------------------------------------------------------

    [Fact]
    public void GetByTimeZone_MexicoCity_ReturnsEntries()
    {
        var entries = _postRegistry.GetByTimeZone("America/Mexico_City");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("America/Mexico_City", e.Timezone));
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
    public void GetAll_AllCodesAre5Digits()
    {
        var all = _postRegistry.GetAll();

        Assert.All(all, e =>
        {
            Assert.Equal(5, e.ZpCode.Length);
            Assert.True(e.ZpCode.All(char.IsDigit), $"Expected 5-digit code but got '{e.ZpCode}'");
        });
    }
}