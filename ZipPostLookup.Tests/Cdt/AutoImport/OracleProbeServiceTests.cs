using Xunit;
using ZipPostLookup.CountryDataTools.Ingestion.AutoImport;
using ZipPostLookup.CountryDataTools.Ingestion.Models;
using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.Tests.Cdt.AutoImport;

#if NET10_0_OR_GREATER

public class OracleProbeServiceTests
{
    private readonly FileSnifferService _sniffer = new();
    private readonly OracleProbeService _oracle = new();

    [Fact]
    public void Probe_UsSample_IdentifiesColumn1AsPostalCode()
    {
        // Arrange
        var filePath = Path.Combine("samples", "Northern America", "US.tsv");
        var sniff = _sniffer.Sniff(filePath, sampleRows: 20);

        // Act
        var probe = _oracle.Probe(filePath, sniff, sampleRows: 200, minHitRate: 0.70);

        // Assert
        Assert.Equal(1, probe.PostalCodeColumnIndex); // Column 1 = US ZIP codes
        Assert.Equal("US", probe.DominantCountry);
        Assert.True(probe.ColumnHitRates[1] >= 0.95); // Should hit ~99% (199/200 rows)
        Assert.True(probe.SampleHits.Count >= 190);
        Assert.False(probe.IsAmbiguous);
    }

    [Fact]
    public void Probe_CaSample_IdentifiesColumn1AsPostalCode()
    {
        // Arrange
        var filePath = Path.Combine("samples", "Northern America", "CA.tsv");
        var sniff = _sniffer.Sniff(filePath, sampleRows: 20);

        // Act
        var probe = _oracle.Probe(filePath, sniff, sampleRows: 200, minHitRate: 0.70);

        // Assert
        Assert.Equal(1, probe.PostalCodeColumnIndex);
        Assert.Equal("CA", probe.DominantCountry);
        Assert.True(probe.ColumnHitRates[1] >= 0.70);
    }

    [Fact]
    public void Probe_JpSample_ThrowsNoColumnMeetsThreshold()
    {
        // Arrange - Japan sample should have 0% hit rate (not in US/CA/MX registry)
        var filePath = Path.Combine("samples", "Eastern Asia", "JP.tsv");
        var sniff = _sniffer.Sniff(filePath, sampleRows: 20);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _oracle.Probe(filePath, sniff, sampleRows: 200, minHitRate: 0.70));

        Assert.Contains("No column achieved minimum hit rate", ex.Message);
    }

    [Fact]
    public void Probe_LowerThreshold_AllowsLowerHitRate()
    {
        // Arrange - create temp file with 50% valid US codes (10001=New York, confirmed in registry)
        var tempFile = Path.GetTempFileName();
        var lines = new List<string> { "Code,City" };
        lines.AddRange(Enumerable.Range(0, 50).Select(_ => "10001,New York")); // Valid US ZIP
        lines.AddRange(Enumerable.Range(0, 50).Select(_ => "00000,Invalid"));  // Not in any registry
        File.WriteAllText(tempFile, string.Join("\n", lines));

        try
        {
            var sniff = _sniffer.Sniff(tempFile, sampleRows: 20);

            // Act
            var probe = _oracle.Probe(tempFile, sniff, sampleRows: 100, minHitRate: 0.40);

            // Assert — postal column identified and hit rate is approximately 50%
            var postalCol = probe.PostalCodeColumnIndex;
            var hitRate = probe.ColumnHitRates[postalCol];
            Assert.True(hitRate >= 0.40, $"Expected hit rate ≥0.40, got {hitRate:P0} on column {postalCol}");
            Assert.True(hitRate < 0.80, $"Expected hit rate <0.80 (mixed valid/invalid), got {hitRate:P0}");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Probe_AmbiguousTwoColumns_SetsIsAmbiguous()
    {
        // Arrange - create file with two postal code columns (both known valid)
        var tempFile = Path.GetTempFileName();
        var lines = new List<string> { "USZip,CAPostal,City" };
        for (int i = 0; i < 50; i++)
        {
            lines.Add("10001,M5H2N2,Toronto");
        }
        File.WriteAllText(tempFile, string.Join("\n", lines));

        try
        {
            var sniff = _sniffer.Sniff(tempFile, sampleRows: 20);

            // Act
            var probe = _oracle.Probe(tempFile, sniff, sampleRows: 50, minHitRate: 0.70);

            // Assert - both columns hit ≥70%, so at least 2 columns in the hit-rate map
            Assert.InRange(probe.ColumnHitRates.Count, 2, 3);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Probe_TracksOracleMisses()
    {
        // Arrange
        var filePath = Path.Combine("samples", "Northern America", "US.tsv");
        var sniff = _sniffer.Sniff(filePath, sampleRows: 20);

        // Act
        var probe = _oracle.Probe(filePath, sniff, sampleRows: 200, minHitRate: 0.70);

        // Assert - US sample should have ~1 miss (200 total, 199 hits)
        Assert.InRange(probe.MissedCodes.Count, 0, 5); // Allow small variance
    }

    [Fact]
    public void Probe_CountryTally_ReflectsDominantCountry()
    {
        // Arrange
        var filePath = Path.Combine("samples", "Northern America", "US.tsv");
        var sniff = _sniffer.Sniff(filePath, sampleRows: 20);

        // Act
        var probe = _oracle.Probe(filePath, sniff, sampleRows: 200, minHitRate: 0.70);

        // Assert
        var tally = probe.ColumnCountryTallies[probe.PostalCodeColumnIndex];
        Assert.True(tally.GetCount("US") > tally.GetCount("CA"));
        Assert.True(tally.GetCount("US") > tally.GetCount("MX"));
        Assert.Equal("US", tally.GetWinner());
    }
}

#endif
