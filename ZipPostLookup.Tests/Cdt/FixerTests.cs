using Xunit;
using ZipPostLookup.CountryDataTools.Dsv;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// Unit tests for the CSV auto-<see cref="Fixer"/> — the per-row normalisation passes (trim,
/// boolean tokens, zip normalisation, casing, lat/lng placeholder, timezone-from-coordinates)
/// and the cross-row passes (duplicate removal, one-default-per-zip). Pure, in-memory.
/// </summary>
public class FixerTests
{
    private static CsvRow Row(int n, string zip, string name, string isDefault = "true",
        string tz = "America/Los_Angeles", string? admin1 = null, string? admin1Code = null,
        string lat = "", string lng = "") => new()
    {
        RecordNumber = n, ZpCode = zip, PlaceName = name, Timezone = tz,
        IsDefault = isDefault, Admin1 = admin1, Admin1Code = admin1Code, Lat = lat, Lng = lng,
    };

    [Fact]
    public void Fix_NormalisesMessyRow()
    {
        var (rows, _) = Fixer.Fix(
            new[] { Row(1, "  90210 ", " Beverly Hills ", isDefault: "1", admin1: "beverly hills", admin1Code: "ca") },
            "US");

        var r = Assert.Single(rows);
        Assert.Equal("90210", r.ZpCode);            // trimmed + normalised
        Assert.Equal("Beverly Hills", r.PlaceName); // trimmed
        Assert.Equal("true", r.IsDefault);          // "1" → "true"
        Assert.Equal("---", r.Lat);                 // blank → placeholder
        Assert.Equal("---", r.Lng);
        Assert.Equal("CA", r.Admin1Code);           // upper-cased
        Assert.Equal("Beverly Hills", r.Admin1);    // title-cased
    }

    [Fact]
    public void Fix_NormalisesIsDefaultTokens()
    {
        var (rows, _) = Fixer.Fix(new[]
        {
            Row(1, "90210", "Bravo", isDefault: "yes"),
            Row(2, "90210", "Alpha", isDefault: "0"),
        }, "US");

        Assert.Equal("true",  rows.Single(r => r.PlaceName == "Bravo").IsDefault);   // yes → true
        Assert.Equal("false", rows.Single(r => r.PlaceName == "Alpha").IsDefault);   // 0 → false
    }

    [Fact]
    public void Fix_RemovesDuplicateZipNamePairs()
    {
        var (rows, _) = Fixer.Fix(new[]
        {
            Row(1, "90210", "Beverly Hills"),
            Row(2, "90210", "Beverly Hills"),   // exact duplicate (zip, name)
        }, "US");

        Assert.Single(rows);
    }

    [Fact]
    public void Fix_OneDefaultPerZip_KeepsAlphabeticallyLast()
    {
        var (rows, _) = Fixer.Fix(new[]
        {
            Row(1, "90210", "Alpha", isDefault: "true"),
            Row(2, "90210", "Bravo", isDefault: "true"),   // both default → keep last name
        }, "US");

        Assert.Equal("false", rows.Single(r => r.PlaceName == "Alpha").IsDefault);
        Assert.Equal("true",  rows.Single(r => r.PlaceName == "Bravo").IsDefault);
    }

    [Fact]
    public void Fix_DerivesTimezoneFromCoordinatesWhenBlank()
    {
        // Blank timezone + real coords → timezone resolved from the coordinates (NYC).
        var (rows, _) = Fixer.Fix(new[]
        {
            Row(1, "10001", "New York", tz: "", lat: "40.7128", lng: "-74.0060"),
        }, "US");

        Assert.Equal("America/New_York", rows.Single().Timezone);
    }

    [Fact]
    public void Fix_NoDefault_PromotesAlphabeticallyFirst()
    {
        var (rows, _) = Fixer.Fix(new[]
        {
            Row(1, "10001", "Zeta",  isDefault: "false", tz: "America/New_York"),
            Row(2, "10001", "Alpha", isDefault: "false", tz: "America/New_York"),
        }, "US");

        Assert.Equal("true",  rows.Single(r => r.PlaceName == "Alpha").IsDefault);
        Assert.Equal("false", rows.Single(r => r.PlaceName == "Zeta").IsDefault);
    }
}
