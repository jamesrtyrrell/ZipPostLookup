using Xunit;
using ZipPostLookup.Core;
using ZipPostLookup.Normalizers;

namespace ZipPostLookup.Tests;

// -------------------------------------------------------------------------
// UsCountryCodeRules
// -------------------------------------------------------------------------

public sealed class UsCountryCodeRulesTests
{
    private readonly ICountryCodeRules _rules = new UsCountryCodeRules();

    // --- CountryCode ---

    [Fact]
    public void CountryCode_IsUS()
    {
        Assert.Equal(CountryCode.US, _rules.CountryCode);
    }

    // --- Normalize ---

    [Fact]
    public void Normalize_FiveDigit_ReturnsUnchanged()
    {
        Assert.Equal("10001", _rules.Normalize("10001"));
    }

    [Fact]
    public void Normalize_ZipPlusFourDash_StripsToFiveDigits()
    {
        Assert.Equal("12345", _rules.Normalize("12345-6789"));
    }

    [Fact]
    public void Normalize_ZipPlusFourSpace_StripsToFiveDigits()
    {
        Assert.Equal("12345", _rules.Normalize("12345 6789"));
    }

    [Fact]
    public void Normalize_NineDigitNoSeparator_StripsToFiveDigits()
    {
        Assert.Equal("12345", _rules.Normalize("123456789"));
    }

    [Fact]
    public void Normalize_LeadingAndTrailingWhitespace_IsTrimmed()
    {
        Assert.Equal("10001", _rules.Normalize("  10001  "));
    }

    [Fact]
    public void Normalize_ZipPlusFourWithWhitespace_TrimsAndStrips()
    {
        Assert.Equal("12345", _rules.Normalize("  12345-6789  "));
    }

    [Fact]
    public void Normalize_NullInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _rules.Normalize(null!));
    }

    // --- Validate ---

    [Fact]
    public void Validate_FiveDigits_ReturnsTrue()
    {
        Assert.True(_rules.Validate("10001"));
    }

    [Fact]
    public void Validate_FiveZeros_ReturnsFalse()
    {
        // 00000 is below the USPS minimum 00501 — structurally 5 digits but out of range
        Assert.False(_rules.Validate("00000"));
    }

    [Fact]
    public void Validate_BelowMinimum_ReturnsFalse()
    {
        Assert.False(_rules.Validate("00500")); // one below 00501
    }

    [Fact]
    public void Validate_Minimum_ReturnsTrue()
    {
        Assert.True(_rules.Validate("00501")); // IRS Holtsville NY — lowest assigned ZIP
    }

    [Fact]
    public void Validate_Maximum_ReturnsTrue()
    {
        Assert.True(_rules.Validate("99950")); // Ketchikan AK — highest assigned ZIP
    }

    [Fact]
    public void Validate_AboveMaximum_ReturnsFalse()
    {
        Assert.False(_rules.Validate("99951")); // one above 99950
    }

    [Fact]
    public void Validate_FourDigits_ReturnsFalse()
    {
        Assert.False(_rules.Validate("1234"));
    }

    [Fact]
    public void Validate_SixDigits_ReturnsFalse()
    {
        Assert.False(_rules.Validate("123456"));
    }

    [Fact]
    public void Validate_NonNumeric_ReturnsFalse()
    {
        Assert.False(_rules.Validate("1234A"));
    }

    [Fact]
    public void Validate_Empty_ReturnsFalse()
    {
        Assert.False(_rules.Validate(string.Empty));
    }

    // --- Round-trip: Normalize then Validate ---

    [Theory]
    [InlineData("10001")]
    [InlineData("10001-2345")]
    [InlineData("10001 2345")]
    [InlineData("100012345")]
    [InlineData("  10001  ")]
    public void NormalizeThenValidate_ValidInputVariants_PassValidation(string input)
    {
        var normalized = _rules.Normalize(input);
        Assert.True(_rules.Validate(normalized),
            $"Expected '{input}' to pass validation after normalisation but got '{normalized}'");
    }

    [Theory]
    [InlineData("ABCDE")]
    [InlineData("")]
    public void NormalizeThenValidate_InvalidInputVariants_FailValidation(string input)
    {
        var normalized = _rules.Normalize(input);
        Assert.False(_rules.Validate(normalized),
            $"Expected '{input}' to fail validation after normalisation but got '{normalized}'");
    }
}

// -------------------------------------------------------------------------
// CaCountryCodeRules
// -------------------------------------------------------------------------

public sealed class CaCountryCodeRulesTests
{
    private readonly ICountryCodeRules _rules = new CaCountryCodeRules();

    // --- CountryCode ---

    [Fact]
    public void CountryCode_IsCA()
    {
        Assert.Equal(CountryCode.CA, _rules.CountryCode);
    }

    // --- Normalize ---

    [Fact]
    public void Normalize_UppercaseSixChar_ReturnsUnchanged()
    {
        Assert.Equal("M5V3L9", _rules.Normalize("M5V3L9"));
    }

    [Fact]
    public void Normalize_Lowercase_ConvertsToUppercase()
    {
        Assert.Equal("M5V3L9", _rules.Normalize("m5v3l9"));
    }

    [Fact]
    public void Normalize_MixedCase_ConvertsToUppercase()
    {
        Assert.Equal("M5V3L9", _rules.Normalize("M5v3L9"));
    }

    [Fact]
    public void Normalize_WithSpaceSeparator_RemovesSpace()
    {
        Assert.Equal("M5V3L9", _rules.Normalize("M5V 3L9"));
    }

    [Fact]
    public void Normalize_WithHyphenSeparator_RemovesHyphen()
    {
        Assert.Equal("M5V3L9", _rules.Normalize("M5V-3L9"));
    }

    [Fact]
    public void Normalize_LowercaseWithSpace_UppercasesAndRemovesSpace()
    {
        Assert.Equal("M5V3L9", _rules.Normalize("m5v 3l9"));
    }

    [Fact]
    public void Normalize_LeadingAndTrailingWhitespace_IsTrimmed()
    {
        Assert.Equal("M5V3L9", _rules.Normalize("  M5V3L9  "));
    }

    [Fact]
    public void Normalize_NullInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _rules.Normalize(null!));
    }

    // --- Validate ---

    [Fact]
    public void Validate_ValidPattern_ReturnsTrue()
    {
        Assert.True(_rules.Validate("M5V3L9"));
    }

    [Fact]
    public void Validate_AllLetters_ReturnsFalse()
    {
        Assert.False(_rules.Validate("MMMMMM"));
    }

    [Fact]
    public void Validate_AllDigits_ReturnsFalse()
    {
        Assert.False(_rules.Validate("123456"));
    }

    [Fact]
    public void Validate_WrongPattern_DigitFirst_ReturnsFalse()
    {
        // Canadian pattern must start with a letter
        Assert.False(_rules.Validate("5M3V9L"));
    }

    [Fact]
    public void Validate_FiveChars_ReturnsFalse()
    {
        Assert.False(_rules.Validate("M5V3L"));
    }

    [Fact]
    public void Validate_SevenChars_ReturnsFalse()
    {
        Assert.False(_rules.Validate("M5V3L9X"));
    }

    [Fact]
    public void Validate_Empty_ReturnsFalse()
    {
        Assert.False(_rules.Validate(string.Empty));
    }

    [Fact]
    public void Validate_WithSpace_ReturnsFalse()
    {
        // Validate expects already-normalised input — space should have been removed by Normalize
        Assert.False(_rules.Validate("M5V 3L9"));
    }

    // --- Round-trip: Normalize then Validate ---

    [Theory]
    [InlineData("M5V3L9")]
    [InlineData("m5v3l9")]
    [InlineData("M5V 3L9")]
    [InlineData("M5V-3L9")]
    [InlineData("m5v 3l9")]
    [InlineData("  M5V3L9  ")]
    public void NormalizeThenValidate_ValidInputVariants_PassValidation(string input)
    {
        var normalized = _rules.Normalize(input);
        Assert.True(_rules.Validate(normalized),
            $"Expected '{input}' to pass validation after normalisation but got '{normalized}'");
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("MMMMMM")]
    [InlineData("")]
    public void NormalizeThenValidate_InvalidInputVariants_FailValidation(string input)
    {
        var normalized = _rules.Normalize(input);
        Assert.False(_rules.Validate(normalized),
            $"Expected '{input}' to fail validation after normalisation but got '{normalized}'");
    }
}

// -------------------------------------------------------------------------
// MxCountryCodeRules
// -------------------------------------------------------------------------

public sealed class MxCountryCodeRulesTests
{
    private readonly ICountryCodeRules _rules = new MxCountryCodeRules();

    // --- CountryCode ---

    [Fact]
    public void CountryCode_IsMX()
    {
        Assert.Equal(CountryCode.MX, _rules.CountryCode);
    }

    // --- Normalize ---

    [Fact]
    public void Normalize_FiveDigit_ReturnsUnchanged()
    {
        Assert.Equal("06600", _rules.Normalize("06600"));
    }

    [Fact]
    public void Normalize_FourDigitMissingLeadingZero_PadsToFiveDigits()
    {
        Assert.Equal("01000", _rules.Normalize("1000"));
    }

    [Fact]
    public void Normalize_LeadingAndTrailingWhitespace_IsTrimmed()
    {
        Assert.Equal("06600", _rules.Normalize("  06600  "));
    }

    [Fact]
    public void Normalize_FourDigitWithWhitespace_TrimsAndPads()
    {
        Assert.Equal("01000", _rules.Normalize("  1000  "));
    }

    [Fact]
    public void Normalize_NullInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _rules.Normalize(null!));
    }

    // --- Validate ---

    [Fact]
    public void Validate_ValidCode_ReturnsTrue()
    {
        Assert.True(_rules.Validate("06600"));
    }

    [Fact]
    public void Validate_AllZeros_ReturnsFalse()
    {
        // 00000 is not a valid Mexican postal code
        Assert.False(_rules.Validate("00000"));
    }

    [Fact]
    public void Validate_FourDigits_ReturnsFalse()
    {
        Assert.False(_rules.Validate("1234"));
    }

    [Fact]
    public void Validate_SixDigits_ReturnsFalse()
    {
        Assert.False(_rules.Validate("123456"));
    }

    [Fact]
    public void Validate_NonNumeric_ReturnsFalse()
    {
        Assert.False(_rules.Validate("0660A"));
    }

    [Fact]
    public void Validate_Empty_ReturnsFalse()
    {
        Assert.False(_rules.Validate(string.Empty));
    }

    // --- Round-trip: Normalize then Validate ---

    [Theory]
    [InlineData("06600")]
    [InlineData("20000")]
    [InlineData("1000")]       // 4-digit input — gets zero-padded to 01000
    [InlineData("  06600  ")]
    public void NormalizeThenValidate_ValidInputVariants_PassValidation(string input)
    {
        var normalized = _rules.Normalize(input);
        Assert.True(_rules.Validate(normalized),
            $"Expected '{input}' to pass validation after normalisation but got '{normalized}'");
    }

    [Theory]
    [InlineData("00000")]
    [InlineData("ABCDE")]
    [InlineData("")]
    public void NormalizeThenValidate_InvalidInputVariants_FailValidation(string input)
    {
        var normalized = _rules.Normalize(input);
        Assert.False(_rules.Validate(normalized),
            $"Expected '{input}' to fail validation after normalisation but got '{normalized}'");
    }
}