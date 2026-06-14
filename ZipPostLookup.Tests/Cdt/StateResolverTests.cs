using Xunit;
using ZipPostLookup.CountryDataTools.CountryRules.Us;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// Unit tests for <see cref="StateResolver"/> — canonicalising US state references from USPS
/// codes, full names, and ANSI/FIPS codes (loaded from the embedded us_info.json "Divisions").
/// Regression guard for the schema-mismatch bug that once made every lookup return null.
/// </summary>
public class StateResolverTests
{
    [Fact]
    public void Resolve_ByUspsCode()
    {
        var m = StateResolver.Resolve("KY");
        Assert.Equal("KY", m?.StateCode);
        Assert.Equal("Kentucky", m?.StateName);
    }

    [Fact]
    public void Resolve_ByFullName_CaseInsensitive()
    {
        Assert.Equal("CA", StateResolver.Resolve("California")?.StateCode);
        Assert.Equal("CA", StateResolver.Resolve("california")?.StateCode);
    }

    [Fact]
    public void Resolve_ByAnsiCode()
    {
        // ANSI / FIPS 6 = California
        Assert.Equal("CA", StateResolver.Resolve("6")?.StateCode);
    }

    [Fact]
    public void Resolve_Unknown_ReturnsNull() => Assert.Null(StateResolver.Resolve("ZZ"));

    [Fact]
    public void Resolve_NullOrBlank_ReturnsNull()
    {
        Assert.Null(StateResolver.Resolve(null));
        Assert.Null(StateResolver.Resolve("   "));
    }
}
