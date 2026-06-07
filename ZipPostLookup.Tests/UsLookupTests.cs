using Xunit;
using ZipPostLookup.Core;

namespace ZipPostLookup.Tests;

public sealed class UsLookupTests : IClassFixture<UsRegistryFixture>
{
    private readonly ZipPostRegistry _postRegistry;

    public UsLookupTests(UsRegistryFixture fixture)
    {
        _postRegistry = fixture.PostRegistry;
    }

    // --- GetByZip ---

    [Fact]
    public void GetByZip_KnownCode_ReturnsCorrectEntry()
    {
        var entry = _postRegistry.GetByZip("10001");

        Assert.NotNull(entry);
        Assert.Equal("10001", entry.ZpCode);
        Assert.Equal("New York", entry.PlaceName);
        Assert.Equal("NY", entry.Admin1Code);
        Assert.Equal("New York", entry.Admin1);
        Assert.Equal("America/New_York", entry.Timezone);
    }

    [Fact]
    public void GetByZip_BeverlyHills_ReturnsCorrectEntry()
    {
        var entry = _postRegistry.GetByZip("90210");

        Assert.NotNull(entry);
        Assert.Equal("90210", entry.ZpCode);
        Assert.Equal("Beverly Hills", entry.PlaceName);
        Assert.Equal("CA", entry.Admin1Code);
        Assert.Equal("California", entry.Admin1);
        Assert.Equal("America/Los_Angeles", entry.Timezone);
    }

    [Fact]
    public void GetByZip_LeadingZeroCode_ReturnsEntry()
    {
        var entry = _postRegistry.GetByZip("00501");

        Assert.NotNull(entry);
        Assert.Equal("00501", entry.ZpCode);
        Assert.Equal("Holtsville", entry.PlaceName);
        Assert.Equal("NY", entry.Admin1Code);
    }

    [Fact]
    public void GetByZip_UnknownCode_ReturnsNull()
    {
        var entry = _postRegistry.GetByZip("00000");
        Assert.Null(entry);
    }

    [Fact]
    public void GetByZip_NullCode_ReturnsNull()
    {
        Assert.Null(_postRegistry.GetByZip(null!));
    }

    // --- TryGetByZip ---

    [Fact]
    public void TryGetByZip_KnownCode_ReturnsTrueAndPopulatesEntry()
    {
        var found = _postRegistry.TryGetByZip("10001", out var entry);

        Assert.True(found);
        Assert.NotNull(entry);
        Assert.Equal("10001", entry.ZpCode);
        Assert.Equal("New York", entry.PlaceName);
        Assert.Equal("NY", entry.Admin1Code);
    }

    [Fact]
    public void TryGetByZip_UnknownCode_ReturnsFalseAndNullEntry()
    {
        var found = _postRegistry.TryGetByZip("00000", out var entry);

        Assert.False(found);
        Assert.Null(entry);
    }

    [Fact]
    public void TryGetByZip_CaseInsensitive_ReturnsTrue()
    {
        var found = _postRegistry.TryGetByZip("90210", out var entry);
        var foundLower = _postRegistry.TryGetByZip("90210", out var entryLower);

        Assert.True(found);
        Assert.True(foundLower);
        Assert.Equal(entry!.ZpCode, entryLower!.ZpCode);
    }

    [Fact]
    public void TryGetByZip_NullCode_ReturnsFalse()
    {
        var found = _postRegistry.TryGetByZip(null!, out var entry);
        Assert.False(found);
        Assert.Null(entry);
    }

    // --- PostalCode aliases ---

    [Fact]
    public void GetByPostalCode_KnownCode_ReturnsSameAsGetByZip()
    {
        var byZip = _postRegistry.GetByZip("10001");
        var byPostalCode = _postRegistry.GetByCode("10001");

        Assert.Equal(byZip, byPostalCode);
    }

    [Fact]
    public void TryGetByPostalCode_KnownCode_ReturnsTrueAndPopulatesEntry()
    {
        var found = _postRegistry.TryGetByCode("10001", out var entry);

        Assert.True(found);
        Assert.NotNull(entry);
        Assert.Equal("10001", entry.ZpCode);
    }

    [Fact]
    public void TryGetByPostalCode_UnknownCode_ReturnsFalse()
    {
        var found = _postRegistry.TryGetByCode("00000", out var entry);

        Assert.False(found);
        Assert.Null(entry);
    }

    [Fact]
    public void GetAllByPostalCode_KnownCode_ReturnsSameAsGetAllByZip()
    {
        var byZip = _postRegistry.GetAllByZip("00603");
        var byPostalCode = _postRegistry.GetAllByCode("00603");

        Assert.Equal(byZip.Count, byPostalCode.Count);
        Assert.Equal(byZip[0].PlaceName, byPostalCode[0].PlaceName);
    }

    [Fact]
    public void GetAllByZip_MultiCityCode_ReturnsAllEntries()
    {
        // 00603 has two entries: AGUADILLA (default) and RAMEY
        var entries = _postRegistry.GetAllByZip("00603");

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void GetAllByZip_DefaultEntryIsFirst()
    {
        var entries = _postRegistry.GetAllByZip("00603");

        Assert.True(entries[0].IsDefault);
        Assert.Equal("Aguadilla", entries[0].PlaceName);
    }

    [Fact]
    public void GetAllByZip_UnknownCode_ReturnsEmpty()
    {
        var entries = _postRegistry.GetAllByZip("00000");
        Assert.Empty(entries);
    }

    // --- GetByCity ---

    [Fact]
    public void GetByCity_KnownCity_ReturnsCityEntries()
    {
        var entries = _postRegistry.GetByCity("New York");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("New York", e.PlaceName));
    }

    [Fact]
    public void GetByCity_CaseInsensitive_ReturnsResults()
    {
        var upper = _postRegistry.GetByCity("New York");
        var lower = _postRegistry.GetByCity("new york");

        Assert.Equal(upper.Count, lower.Count);
    }

    [Fact]
    public void GetByCity_UnknownCity_ReturnsEmpty()
    {
        Assert.Empty(_postRegistry.GetByCity("R'LYEH"));
    }

    [Fact]
    public void GetByCity_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _postRegistry.GetByCity(null!));
    }

    // --- GetByState ---

    [Fact]
    public void GetByState_KnownAbbreviation_ReturnsEntries()
    {
        var entries = _postRegistry.GetByState("NY");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("NY", e.Admin1Code));
    }

    [Fact]
    public void GetByState_CaseInsensitive_ReturnsResults()
    {
        var upper = _postRegistry.GetByState("NY");
        var lower = _postRegistry.GetByState("ny");

        Assert.Equal(upper.Count, lower.Count);
    }

    [Fact]
    public void GetByState_UnknownAbbreviation_ReturnsEmpty()
    {
        Assert.Empty(_postRegistry.GetByState("XX"));
    }

    // --- GetByStateName ---

    [Fact]
    public void GetByStateName_KnownName_ReturnsEntries()
    {
        var entries = _postRegistry.GetByStateName("New York");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("New York", e.Admin1));
    }

    [Fact]
    public void GetByStateName_CaseInsensitive_ReturnsResults()
    {
        var normal = _postRegistry.GetByStateName("New York");
        var lower = _postRegistry.GetByStateName("new york");

        Assert.Equal(normal.Count, lower.Count);
    }

    [Fact]
    public void GetByStateName_UnknownName_ReturnsEmpty()
    {
        Assert.Empty(_postRegistry.GetByStateName("Narnia"));
    }

    // --- GetByTimeZone ---

    [Fact]
    public void GetByTimeZone_KnownZone_ReturnsEntries()
    {
        var entries = _postRegistry.GetByTimeZone("America/New_York");

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("America/New_York", e.Timezone));
    }

    [Fact]
    public void GetByTimeZone_UnknownZone_ReturnsEmpty()
    {
        Assert.Empty(_postRegistry.GetByTimeZone("Mars/Olympus"));
    }

    // --- GetAll ---

    [Fact]
    public void GetAll_ReturnsNonEmptyList()
    {
        var all = _postRegistry.GetAll();
        Assert.NotEmpty(all);
    }

    [Fact]
    public void GetAll_IsSortedByZip()
    {
        var all = _postRegistry.GetAll();
        for (int i = 1; i < all.Count; i++)
            Assert.True(
                string.Compare(all[i - 1].ZpCode, all[i].ZpCode, StringComparison.Ordinal) <= 0,
                $"Expected sorted order but '{all[i - 1].ZpCode}' came before '{all[i].ZpCode}'");
    }

    // --- GetRandom ---

    [Fact]
    public void GetRandom_ReturnsValidEntry()
    {
        var entry = _postRegistry.GetRandom();
        Assert.NotNull(entry);
        Assert.NotEmpty(entry.ZpCode);
    }

    [Fact]
    public void GetRandom_WithSeed_IsReproducible()
    {
        var entry1 = _postRegistry.GetRandom(new Random(42));
        var entry2 = _postRegistry.GetRandom(new Random(42));
        Assert.Equal(entry1.ZpCode, entry2.ZpCode);
    }
}