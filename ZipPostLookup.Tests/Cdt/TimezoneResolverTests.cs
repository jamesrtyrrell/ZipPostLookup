using Xunit;
using ZipPostLookup.CountryDataTools.Utilities;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// Unit tests for <see cref="TimezoneResolver"/> — deriving a country code from a data
/// filename, and resolving an IANA timezone from coordinates (via GeoTimeZone, no DB).
/// </summary>
public class TimezoneResolverTests
{
    [Theory]
    [InlineData("us.csv", "US")]
    [InlineData("ca-geonames.tsv", "CA")]      // stem split on '-', first segment
    [InlineData("MX.csv", "MX")]               // upper-cased
    [InlineData("countries.json", null)]       // 9-letter stem → not a 2-letter code
    [InlineData("data-2024.csv", null)]        // "data" stem → null
    public void CountryCodeFromFileName(string file, string? expected) =>
        Assert.Equal(expected, TimezoneResolver.CountryCodeFromFileName(file));

    [Fact]
    public void TryResolveWithCoordinates_ReturnsIana()
    {
        // New York City → America/New_York
        Assert.Equal("America/New_York", TimezoneResolver.TryResolveWithCoordinates("40.7128", "-74.0060"));
    }

    [Fact]
    public void TryResolveWithCoordinates_Unparseable_ReturnsNull() =>
        Assert.Null(TimezoneResolver.TryResolveWithCoordinates("abc", "xyz"));
}
