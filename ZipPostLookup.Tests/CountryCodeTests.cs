using Xunit;
using ZipPostLookup.Core;

namespace ZipPostLookup.Tests;

public sealed class CountryCodeTests
{
    [Fact]
    public void StaticConstant_HasCorrectValue()
    {
        Assert.Equal("US", (string)CountryCode.US);
        Assert.Equal("CA", (string)CountryCode.CA);
        Assert.Equal("GB", (string)CountryCode.GB);
    }

    [Fact]
    public void ImplicitFromString_NormalisesToUppercase()
    {
        CountryCode lower = "us";
        CountryCode mixed = "Ca";
        Assert.Equal(CountryCode.US, lower);
        Assert.Equal(CountryCode.CA, mixed);
    }

    [Fact]
    public void Equality_SameCode_IsEqual()
    {
        CountryCode a = "US";
        CountryCode b = "us";
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equality_DifferentCodes_NotEqual()
    {
        Assert.NotEqual(CountryCode.US, CountryCode.CA);
        Assert.True(CountryCode.US != CountryCode.CA);
    }

    [Fact]
    public void ToString_ReturnsUppercaseCode()
    {
        Assert.Equal("US", CountryCode.US.ToString());
        Assert.Equal("CA", CountryCode.CA.ToString());
    }

    [Fact]
    public void Constructor_EmptyString_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CountryCode(""));
        Assert.Throws<ArgumentException>(() => new CountryCode("   "));
    }

    [Fact]
    public void GetHashCode_EqualCodes_SameHash()
    {
        CountryCode a = "US";
        CountryCode b = "us";
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}