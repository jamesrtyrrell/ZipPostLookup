using Xunit;
using ZipPostLookup.Core;

namespace ZipPostLookup.Tests;

public sealed class RegistryTests : IClassFixture<NorthAmericaRegistryFixture>
{
    private readonly ZipPostRegistry _northAmerica;

    public RegistryTests(NorthAmericaRegistryFixture fixture)
    {
        _northAmerica = fixture.PostRegistry;
    }

    // --- Multi-country registry ---

    [Fact]
    public void NorthAmericaRegistry_ContainsUsZip()
    {
        var entry = _northAmerica.GetByZip("10001");

        Assert.NotNull(entry);
        Assert.Equal("NY", entry.Admin1Code);
    }

    [Fact]
    public void NorthAmericaRegistry_ContainsCaPostalCode()
    {
        var entry = _northAmerica.GetByZip("M5V3L9");

        Assert.NotNull(entry);
        Assert.Equal("ON", entry.Admin1Code);
    }

    [Fact]
    public void NorthAmericaRegistry_GetAll_ContainsBothCountries()
    {
        var all = _northAmerica.GetAll();

        var hasUs = all.Any(e => e.ZpCode.Length == 5 && e.ZpCode.All(char.IsDigit));
        var hasCa = all.Any(e => e.ZpCode.Length == 6);

        Assert.True(hasUs, "Expected US zip codes in combined registry");
        Assert.True(hasCa, "Expected CA postal codes in combined registry");
    }

    [Fact]
    public void NorthAmericaRegistry_GetByTimeZone_SpansCountries()
    {
        // America/Toronto covers both ON (CA) and some US entries
        var entries = _northAmerica.GetByTimeZone("America/Toronto");
        Assert.NotEmpty(entries);
    }

    // --- Unsupported country ---

    [Fact]
    public void CreateRegistry_UnsupportedCountry_ThrowsNotSupportedException()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => new ZipPostRegistry(CountryCode.GB));

        Assert.Contains("GB", ex.Message);
        Assert.Contains("ICodeDataSource", ex.Message);
    }

    [Fact]
    public void CreateRegistry_UnsupportedCountryString_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(
            () => new ZipPostRegistry((CountryCode)"FR"));
    }

    // --- Default registry ---

    [Fact]
    public void Default_IsUsOnly()
    {
        var all = ZipPostRegistry.Default.GetAll();

        // All entries should be 5-digit numeric US zips
        Assert.All(all, entry =>
        {
            Assert.Equal(5, entry.ZpCode.Length);
            Assert.True(entry.ZpCode.All(char.IsDigit), $"Expected numeric zip but got '{entry.ZpCode}'");
        });
    }

    [Fact]
    public void Default_IsSingleton()
    {
        Assert.Same(ZipPostRegistry.Default, ZipPostRegistry.Default);
    }

    // --- CreateCustomRegistry ---

    [Fact]
    public void CreateCustomRegistry_AdditionalSource_MergesEntries()
    {
        var custom = ZipPostLookup.CreateCustomRegistry(new TestDataSource());
        var entry = custom.GetByZip("99999");

        Assert.NotNull(entry);
        Assert.Equal("Test City", entry.PlaceName);
    }

    // --- GetRandom ---

    [Fact]
    public void GetRandom_NullRandom_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ZipPostRegistry.Default.GetRandom(null!));
    }
}

/// <summary>
/// Minimal in-memory data source for testing CreateCustomRegistry.
/// </summary>
internal sealed class TestDataSource : ICodeDataSource
{
    public string SourceName => "test";

    public IEnumerable<CodeEntry> GetEntries()
    {
        yield return new CodeEntry(
            ZpCode: "99999",
            PlaceName: "Test City",
            Timezone: "America/Chicago",
            IsDefault: true,
            Admins: new[] { new AdminLevel("Texas", "TX", "State") });
    }
}