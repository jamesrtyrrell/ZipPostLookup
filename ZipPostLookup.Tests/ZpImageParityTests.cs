using Xunit;
using ZipPostLookup.Core;
using ZipPostLookup.ZPImage;

namespace ZipPostLookup.Tests;

/// <summary>
/// Verifies the ZP frozen-image reader returns the same results as the CSV-built registry
/// for the embedded US data — guards against any drift between the writer and reader
/// (format, hash, or packing).
/// </summary>
public sealed class ZpImageParityTests
{
    private static readonly ZipPostRegistry Registry = new(CountryCode.US);
    private static readonly ZpImageLookup Image = ZpImageLookup.FromBuiltIn(CountryCode.US);

    [Fact]
    public void EveryCode_MatchesRegistry_DefaultEntry()
    {
        var all = Registry.GetAll();
        var distinctCodes = all.Select(e => e.ZpCode).Distinct(StringComparer.OrdinalIgnoreCase);

        var verified = 0;
        foreach (var code in distinctCodes)
        {
            var expected = Registry.GetByCode(code);
            var actual   = Image.GetByCode(code);

            Assert.NotNull(actual);
            Assert.Equal(expected!.ZpCode, actual!.ZpCode);
            Assert.Equal(expected.PlaceName, actual.PlaceName);
            Assert.Equal(expected.Timezone, actual.Timezone);
            Assert.Equal(expected.IsDefault, actual.IsDefault);
            Assert.Equal(expected.Admin1, actual.Admin1);
            Assert.Equal(expected.Admin1Code, actual.Admin1Code);
            verified++;
        }

        Assert.True(verified > 40_000, $"Expected >40k codes, verified {verified}.");
    }

    [Fact]
    public void Miss_ReturnsNull()
    {
        Assert.Null(Image.GetByCode("ZZZZZ"));
        Assert.Null(Image.GetByCode("00000"));
    }

    [Fact]
    public void MultiName_ReturnsAllEntries()
    {
        // 00603 — Aguadilla + Ramey, Puerto Rico (multi-name)
        var expected = Registry.GetAllByCode("00603");
        var actual   = Image.GetAllByCode("00603");

        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(
            expected.Select(e => e.PlaceName).OrderBy(n => n),
            actual.Select(e => e.PlaceName).OrderBy(n => n));
    }

    [Fact]
    public void Reverse_State_Timezone_Match()
    {
        Assert.Equal(Registry.GetByState("NY").Count, Image.GetByState("NY").Count);
        Assert.Equal(
            Registry.GetByTimeZone("America/New_York").Count,
            Image.GetByTimeZone("America/New_York").Count);
    }

    [Fact]
    public void Closest_Matches()
    {
        var expected = Registry.GetClosest("00001");
        var actual   = Image.GetClosest("00001");
        Assert.Equal(expected?.ZpCode, actual?.ZpCode);
    }
}

/// <summary>
/// CA-specific parity: the embedded Canadian image is the only built-in with a populated range
/// table (FSA compression), so it is the one that exercises the range-fallback path and the
/// 20-byte <c>RangeTable</c> entry layout. A stale CA image (built before the range entry grew a
/// <c>recordId</c> field) is exactly the drift that previously slipped past the US-only tests, so
/// this guards the range read/write contract directly.
///
/// <para>Deliberately uses only exact + range code lookups (each O(1) / small). It avoids the
/// reverse-lookup APIs (GetByState/GetByTimeZone/GetByName) for CA: those return tens of thousands
/// of records and the reader currently materialises each via an O(exactCount) scan, which is far
/// too slow for a unit test over 322k codes. Reverse parity is covered for US above.</para>
/// </summary>
public sealed class CaZpImageParityTests
{
    private static readonly ZipPostRegistry Registry = new(CountryCode.CA);
    private static readonly ZpImageLookup Image = ZpImageLookup.FromBuiltIn(CountryCode.CA);

    [Fact]
    public void ExactCode_MatchesRegistry()
    {
        // A plain exact LDU code on the MPHF fast path — derived from the exported CSV.
        var expected = Registry.GetByCode(TestCodes.CA.ExactCode);
        var actual   = Image.GetByCode(TestCodes.CA.ExactCode);

        Assert.NotNull(actual);
        Assert.Equal(expected!.ZpCode, actual!.ZpCode);
        Assert.Equal(expected.PlaceName, actual.PlaceName);
        Assert.Equal(expected.Timezone, actual.Timezone);
        Assert.Equal(expected.Admin1Code, actual.Admin1Code);
    }

    [Fact]
    public void RangeCode_ResolvesThroughRangeTable()
    {
        // TestCodes.CA.RangeProbe falls inside a compressed range row (TestCodes.CA.RangeZpCode) — it has
        // no exact entry, so a correct result proves the range table decoded with the right stride.
        var expected = Registry.GetByCode(TestCodes.CA.RangeProbe);
        var actual   = Image.GetByCode(TestCodes.CA.RangeProbe);

        Assert.NotNull(actual);
        Assert.Equal(expected!.ZpCode, actual!.ZpCode);
        Assert.Equal(expected.PlaceName, actual.PlaceName);
        Assert.Equal(expected.Timezone, actual.Timezone);
        Assert.Equal(expected.Admin1Code, actual.Admin1Code);
        Assert.Contains(":", actual.ZpCode); // confirms it came back as the range notation
    }

    [Fact]
    public void Miss_ReturnsNull()
    {
        Assert.Null(Image.GetByCode("Z9Z9Z9"));
    }

    [Fact]
    public void ReverseLookups_MatchRegistry()
    {
        // CA reverse lookups return very large record sets (e.g. the Ontario / America/Toronto
        // postings run into the tens of thousands). These exercise the reader's record → code
        // materialisation, which is backed by a one-time record → directory-slot map; a count
        // match confirms both correctness and that the map covers the full posting span.
        Assert.Equal(Registry.GetByState("ON").Count, Image.GetByState("ON").Count);
        Assert.Equal(Registry.GetByStateName("Ontario").Count, Image.GetByStateName("Ontario").Count);
        Assert.Equal(
            Registry.GetByTimeZone("America/Toronto").Count,
            Image.GetByTimeZone("America/Toronto").Count);

        // Spot-check a materialised entry from a reverse lookup resolves to a real Ontario record.
        var sample = Image.GetByState("ON")[0];
        Assert.Equal("ON", sample.Admin1Code);
        Assert.False(string.IsNullOrEmpty(sample.ZpCode));
    }
}
