using Xunit;
using ZipPostLookup.CountryDataTools.CountryRules;
using ZipPostLookup.CountryDataTools.CountryRules.Ca;
using ZipPostLookup.CountryDataTools.CountryRules.Mx;
using ZipPostLookup.CountryDataTools.CountryRules.Us;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// Unit tests for the per-country domain rules (US/CA/MX) behind <c>ICountryRules</c> —
/// admin1 derivation, special-code/name detection, deprecated-timezone canonicalisation,
/// API-code shaping, and the factory. Pure logic, no database.
/// </summary>
public class CountryRulesTests
{
    // ── Factory ──────────────────────────────────────────────────────────────

    [Fact] public void Factory_Us() => Assert.IsType<UsCountryRules>(CountryRulesFactory.For("US"));
    [Fact] public void Factory_Us_LowerCase() => Assert.IsType<UsCountryRules>(CountryRulesFactory.For("us"));
    [Fact] public void Factory_Ca() => Assert.IsType<CaCountryRules>(CountryRulesFactory.For("CA"));
    [Fact] public void Factory_Mx() => Assert.IsType<MxCountryRules>(CountryRulesFactory.For("MX"));

    [Fact]
    public void Factory_UnknownCountry_ReturnsNonDerivingRules()
    {
        var rules = CountryRulesFactory.For("ZZ");
        Assert.False(rules.SupportsAdmin1Derivation);
        Assert.Null(rules.ResolveAdmin1("12345"));
        Assert.False(rules.IsKnownSpecialCode("12345"));
    }

    // ── United States ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    [InlineData(99950, false)]
    [InlineData(99951, true)]
    public void Us_IsOutOfBoundsUs(int zip, bool expected) =>
        Assert.Equal(expected, UsCountryRules.IsOutOfBoundsUs(zip));

    [Theory]
    [InlineData("09001", true)]    // APO/FPO Europe
    [InlineData("34050", true)]    // APO/FPO Americas
    [InlineData("96500", true)]    // APO/FPO Pacific
    [InlineData("00601", true)]    // Puerto Rico
    [InlineData("56999", true)]    // Parcel Return Service
    [InlineData("90210", false)]   // ordinary ZIP
    [InlineData("", false)]
    [InlineData("ABCDE", false)]
    public void Us_IsKnownSpecialCode(string code, bool expected) =>
        Assert.Equal(expected, new UsCountryRules().IsKnownSpecialCode(code));

    [Theory]
    [InlineData("09001", true)]    // military → APIs can't resolve
    [InlineData("56999", true)]    // PRS → skip
    [InlineData("00601", false)]   // territory (PR) has real addresses → enrichable
    public void Us_IsEnrichmentSkipped(string code, bool expected) =>
        Assert.Equal(expected, new UsCountryRules().IsEnrichmentSkipped(code));

    [Theory]
    [InlineData("APO AE", true)]
    [InlineData("Apo", true)]
    [InlineData("Springfield", false)]
    public void Us_IsKnownSpecialName(string name, bool expected) =>
        Assert.Equal(expected, new UsCountryRules().IsKnownSpecialName(name));

    [Fact]
    public void Us_CanonicalizeTimezone()
    {
        // CanonicalizeTimezone is a default interface method — call via ICountryRules.
        ICountryRules rules = new UsCountryRules();
        Assert.Equal("America/Denver", rules.CanonicalizeTimezone("America/Shiprock")); // retired → canonical
        Assert.Equal("America/Denver", rules.CanonicalizeTimezone("America/Denver"));   // already canonical
        Assert.Null(rules.CanonicalizeTimezone(null));
    }

    [Fact] public void Us_SupportsAdmin1Derivation() => Assert.True(new UsCountryRules().SupportsAdmin1Derivation);

    // ── Canada ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("A1A1A1", "NL")]   // first letter → province
    [InlineData("M5V3L9", "ON")]
    [InlineData("T2P1B8", "AB")]
    [InlineData("X0A0A0", "NU")]   // Nunavut carve-out within the X block
    [InlineData("X1A0A0", "NT")]   // every other X = Northwest Territories
    public void Ca_ResolveAdmin1(string code, string expectedAdmin) =>
        Assert.Equal(expectedAdmin, new CaCountryRules().ResolveAdmin1(code)?.Code);

    [Fact]
    public void Ca_ResolveAdmin1_Unknown_ReturnsNull() =>
        Assert.Null(new CaCountryRules().ResolveAdmin1("Z9Z9Z9"));

    [Fact]
    public void Ca_GetApiLookupCode_TruncatesToFsa() =>
        Assert.Equal("M5V", new CaCountryRules().GetApiLookupCode("M5V3L9"));

    [Fact]
    public void Ca_CanonicalizeTimezone_MapsRetiredZone() =>
        Assert.Equal("America/Toronto",
            ((ICountryRules)new CaCountryRules()).CanonicalizeTimezone("America/Nipigon"));

    [Fact]
    public void Ca_PlaceNameLanguages_IncludeEnglishAndFrench()
    {
        var langs = new CaCountryRules().PlaceNameLanguages;
        Assert.Contains("English", langs);
        Assert.Contains("French", langs);
    }

    // ── Mexico ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("01000", "CMX")]   // Ciudad de México
    [InlineData("20000", "AGS")]   // Aguascalientes
    [InlineData("44100", "JAL")]   // Jalisco
    [InlineData("99999", "ZAC")]   // Zacatecas
    public void Mx_ResolveAdmin1(string code, string expectedAdmin) =>
        Assert.Equal(expectedAdmin, new MxCountryRules().ResolveAdmin1(code)?.Code);

    [Fact]
    public void Mx_ResolveAdmin1_NonNumeric_ReturnsNull() =>
        Assert.Null(new MxCountryRules().ResolveAdmin1("ABCDE"));
}
