using Xunit;
using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Models.Enums;
using ZipPostLookup.CountryDataTools.Validation.Csv;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// Unit tests for the CSV <see cref="ValidationRules"/> engine — structural (header),
/// per-row (required fields, code format, coordinate pairing/range, admin pairing), over a
/// parsed <see cref="CsvRow"/> list. Pure, in-memory.
/// </summary>
public class ValidationRulesTests
{
    private static IReadOnlyList<ValidationError> Validate(params CsvRow[] rows) =>
        ValidationRules.Validate(rows, headerOk: true, missingColumns: System.Array.Empty<string>(), countryCode: "US");

    private static CsvRow ValidUsRow(int n = 1) => new()
    {
        RecordNumber = n,
        ZpCode = "90210", PlaceName = "Beverly Hills", Timezone = "America/Los_Angeles",
        IsDefault = "true", Lat = "34.09", Lng = "-118.41", Admin1 = "California", Admin1Code = "CA",
    };

    [Fact]
    public void MissingColumns_ReportedAsHeaderErrors()
    {
        var errors = ValidationRules.Validate(
            System.Array.Empty<CsvRow>(), headerOk: true, missingColumns: new[] { "ZpCode" }, countryCode: "US");
        Assert.Contains(errors, e => e.ErrorType == ErrorType.MissingColumn && e.Value == "ZpCode");
    }

    [Fact]
    public void BrokenHeader_ShortCircuitsRowChecks()
    {
        var errors = ValidationRules.Validate(
            new[] { ValidUsRow() }, headerOk: false, missingColumns: new[] { "ZpCode" }, countryCode: "US");
        Assert.Single(errors);
        Assert.Equal(ErrorType.MissingColumn, errors[0].ErrorType);
    }

    [Fact]
    public void CleanRow_HasNoErrors() => Assert.Empty(Validate(ValidUsRow()));

    [Fact]
    public void MissingRequiredFields_Reported()
    {
        var row = ValidUsRow();
        row.PlaceName = "";
        row.Timezone = "";
        var errors = Validate(row);
        Assert.Contains(errors, e => e.Field == "PlaceName" && e.ErrorType == ErrorType.MissingValue);
        Assert.Contains(errors, e => e.Field == "Timezone" && e.ErrorType == ErrorType.MissingValue);
    }

    [Fact]
    public void BadZipFormat_Reported()
    {
        var row = ValidUsRow();
        row.ZpCode = "ABCDE";
        Assert.Contains(Validate(row), e => e.Field == "Code" && e.ErrorType == ErrorType.InvalidZipFormat);
    }

    [Fact]
    public void HalfCoordinatePair_Reported()
    {
        var row = ValidUsRow();
        row.Lng = "---";   // lat present, lng blank
        Assert.Contains(Validate(row),
            e => e.ErrorType == ErrorType.MissingValue && e.Field is "Lat" or "Lng");
    }

    [Fact]
    public void OutOfRangeLatitude_Reported()
    {
        var row = ValidUsRow();
        row.Lat = "999";
        Assert.Contains(Validate(row), e => e.Field == "Lat" && e.ErrorType == ErrorType.InvalidZipFormat);
    }

    [Fact]
    public void AdminCodeWithoutAdminValue_Reported()
    {
        var row = ValidUsRow();
        row.Admin1 = "";   // code present, value missing
        Assert.Contains(Validate(row), e => e.Field == "Admin1" && e.ErrorType == ErrorType.MissingValue);
    }
}
