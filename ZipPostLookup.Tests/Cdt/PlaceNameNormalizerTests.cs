using Xunit;
using ZipPostLookup.CountryDataTools.Validation;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// Unit tests for <see cref="PlaceNameNormalizer"/> — abbreviation expansion and
/// equivalence (e.g. "St. Martin" ≡ "Saint Martin"), driven by the embedded
/// LanguageAbbreviations.json.
/// </summary>
public class PlaceNameNormalizerTests
{
    private static readonly string[] English = { "English" };

    [Fact]
    public void AreEquivalent_SaintAbbreviation() =>
        Assert.True(PlaceNameNormalizer.AreEquivalent("St. Martin", "Saint Martin", English));

    [Fact]
    public void AreEquivalent_HyphenAndDotInsensitive() =>
        Assert.True(PlaceNameNormalizer.AreEquivalent("St-Jean", "Saint Jean", English));

    [Fact]
    public void AreEquivalent_DistinctNames_False() =>
        Assert.False(PlaceNameNormalizer.AreEquivalent("Springfield", "Shelbyville", English));

    [Fact]
    public void AreEquivalent_BlankInput_False() =>
        Assert.False(PlaceNameNormalizer.AreEquivalent("", "Saint Martin", English));

    [Fact]
    public void Normalize_ExpandsAbbreviation() =>
        Assert.Equal("Saint Martin", PlaceNameNormalizer.Normalize("St. Martin", English));

    [Fact]
    public void Normalize_NoLanguages_ReturnsInput() =>
        Assert.Equal("St. Martin", PlaceNameNormalizer.Normalize("St. Martin", System.Array.Empty<string>()));
}
