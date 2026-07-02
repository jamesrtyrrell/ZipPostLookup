using Xunit;
using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Ingestion.AutoImport;
using ZipPostLookup.CountryDataTools.Ingestion.Models;

namespace ZipPostLookup.Tests.Cdt.AutoImport;

#if NET10_0_OR_GREATER

public class ColumnCorrelationServiceTests
{
    private readonly FileSnifferService _sniffer = new();
    private readonly OracleProbeService _oracle = new();
    private readonly ColumnCorrelationService _correlation = new();

    /// <summary>
    /// Reads sample rows the same way AutoImportCommand and OracleProbeService do — skipping
    /// the header row when the sniffer flags one. The probe's OracleHit.RowIndex values are
    /// relative to the data rows (header excluded), so reading without the skip misaligns
    /// every hit by one row and collapses all similarity scores.
    /// </summary>
    private static string[][] ReadSampleRows(string filePath, FileSniffResult sniff, int maxRows)
    {
        var rows = DelimitedFile.ReadRows(filePath, sniff.Delimiter,
            maxRows: maxRows + (sniff.HasHeaderRow ? 1 : 0));
        if (sniff.HasHeaderRow && rows.Count > 0) rows.RemoveAt(0);
        return rows.ToArray();
    }

    [Fact]
    public void Correlate_UsSample_MapsPlaceNameAdmin1AndCoords()
    {
        // Arrange
        var filePath = Path.Combine("samples", "Northern America", "US.tsv");
        var sniff = _sniffer.Sniff(filePath, sampleRows: 20);
        var probe = _oracle.Probe(filePath, sniff, sampleRows: 200, minHitRate: 0.70);
        var sampleRows = ReadSampleRows(filePath, sniff, 200);

        // Act
        var proposal = _correlation.Correlate(sniff, probe, sampleRows);

        // Assert
        Assert.NotEmpty(proposal.Mappings);

        // ZpCode should be column 1
        var zpCodeMapping = proposal.Mappings.FirstOrDefault(m => m.FieldName == "ZpCode");
        Assert.NotNull(zpCodeMapping);
        Assert.Equal(1, zpCodeMapping.ColumnIndex);
        Assert.True(zpCodeMapping.Confidence >= 0.95);

        // PlaceName should be column 2
        var placeNameMapping = proposal.Mappings.FirstOrDefault(m => m.FieldName == "PlaceName");
        Assert.NotNull(placeNameMapping);
        Assert.Equal(2, placeNameMapping.ColumnIndex);
        Assert.True(placeNameMapping.Confidence >= 0.60);

        // Admin1 should be column 3 (Alaska, etc.)
        var admin1Mapping = proposal.Mappings.FirstOrDefault(m => m.FieldName == "Admin1");
        Assert.NotNull(admin1Mapping);
        Assert.Equal(3, admin1Mapping.ColumnIndex);

        // Admin1Code should be column 4 (AK, etc.)
        var admin1CodeMapping = proposal.Mappings.FirstOrDefault(m => m.FieldName == "Admin1Code");
        Assert.NotNull(admin1CodeMapping);
        Assert.Equal(4, admin1CodeMapping.ColumnIndex);

        // Latitude and Longitude should be detected in some numeric column
        var latMapping = proposal.Mappings.FirstOrDefault(m => m.FieldName == "Latitude");
        Assert.NotNull(latMapping);
        Assert.True(latMapping.ColumnIndex.HasValue);

        var lngMapping = proposal.Mappings.FirstOrDefault(m => m.FieldName == "Longitude");
        Assert.NotNull(lngMapping);
        Assert.True(lngMapping.ColumnIndex.HasValue);
    }

    [Fact]
    public void Correlate_HighConfidenceMappings_ZpCodeAndPlaceNameMapped()
    {
        // Arrange - create clear CSV with perfect column names and all valid US ZIPs
        var tempFile = Path.GetTempFileName();
        var lines = new List<string> { "ZipCode,PlaceName,Admin1,Admin1Code" };
        // Use ZIPs confirmed in the built-in registry
        lines.Add("10001,New York,New York,NY");
        lines.Add("90210,Beverly Hills,California,CA");
        lines.Add("33101,Miami,Florida,FL");
        lines.Add("60601,Chicago,Illinois,IL");
        lines.Add("94102,San Francisco,California,CA");
        File.WriteAllText(tempFile, string.Join("\n", lines));

        try
        {
            var sniff = _sniffer.Sniff(tempFile, sampleRows: 10);
            var probe = _oracle.Probe(tempFile, sniff, sampleRows: 10, minHitRate: 0.70);

            // Read sample rows the same way AutoImportCommand does (skip header if present)
            var rawRows = DelimitedFile.ReadRows(tempFile, sniff.Delimiter, maxRows: 10 + (sniff.HasHeaderRow ? 1 : 0));
            if (sniff.HasHeaderRow && rawRows.Count > 0) rawRows.RemoveAt(0);
            var sampleRows = rawRows.ToArray();

            // Act
            var proposal = _correlation.Correlate(sniff, probe, sampleRows);

            // Assert — ZpCode and PlaceName should be mapped with high confidence
            var zpCode = proposal.Mappings.FirstOrDefault(m => m.FieldName == "ZpCode");
            Assert.NotNull(zpCode);
            Assert.True(zpCode.Confidence >= 0.70);

            var placeName = proposal.Mappings.FirstOrDefault(m => m.FieldName == "PlaceName");
            Assert.NotNull(placeName);
            Assert.True(placeName.Confidence >= 0.30);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Correlate_CompetingCandidates_SetsRequireDisambiguation()
    {
        // Arrange - GeoNames has multiple numeric columns that look like coords
        var filePath = Path.Combine("samples", "Northern America", "US.tsv");
        var sniff = _sniffer.Sniff(filePath, sampleRows: 20);
        var probe = _oracle.Probe(filePath, sniff, sampleRows: 200, minHitRate: 0.70);
        var sampleRows = ReadSampleRows(filePath, sniff, 200);

        // Act
        var proposal = _correlation.Correlate(sniff, probe, sampleRows);

        // Assert - GeoNames has columns 9/10 (lat/lng) and 11 (accuracy flag, also numeric)
        // Should detect ambiguity between coordinate candidates
        Assert.True(proposal.RequireDisambiguation);
        Assert.Contains(proposal.AmbiguityReasons, r => r.Contains("Latitude") || r.Contains("Longitude"));
    }

    [Fact]
    public void Correlate_MissingPlaceName_SetsAmbiguity()
    {
        // Arrange - create file without clear PlaceName column (code + numeric only, no header)
        var tempFile = Path.GetTempFileName();
        var lines = new List<string>();
        lines.Add("10001,42.36");
        lines.Add("90210,40.75");
        File.WriteAllText(tempFile, string.Join("\n", lines));

        try
        {
            var sniff = _sniffer.Sniff(tempFile, sampleRows: 10);
            var probe = _oracle.Probe(tempFile, sniff, sampleRows: 2, minHitRate: 0.70);
            var sampleRows = DelimitedFile.ReadRows(tempFile, sniff.Delimiter, maxRows: 2).ToArray();

            // Act
            var proposal = _correlation.Correlate(sniff, probe, sampleRows);

            // Assert
            Assert.Contains(proposal.AmbiguityReasons, r => r.Contains("PlaceName"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Correlate_GreedyAssignment_NoDuplicateColumns()
    {
        // Arrange
        var filePath = Path.Combine("samples", "Northern America", "US.tsv");
        var sniff = _sniffer.Sniff(filePath, sampleRows: 20);
        var probe = _oracle.Probe(filePath, sniff, sampleRows: 200, minHitRate: 0.70);
        var sampleRows = ReadSampleRows(filePath, sniff, 200);

        // Act
        var proposal = _correlation.Correlate(sniff, probe, sampleRows);

        // Assert - no column should be assigned to multiple fields
        var assignedColumns = proposal.Mappings
            .Where(m => m.ColumnIndex.HasValue)
            .Select(m => m.ColumnIndex!.Value)
            .ToList();

        Assert.Equal(assignedColumns.Count, assignedColumns.Distinct().Count());
    }

    [Fact]
    public void Correlate_ConfidenceScores_ReflectCoverageAndSimilarity()
    {
        // Arrange
        var filePath = Path.Combine("samples", "Northern America", "US.tsv");
        var sniff = _sniffer.Sniff(filePath, sampleRows: 20);
        var probe = _oracle.Probe(filePath, sniff, sampleRows: 200, minHitRate: 0.70);
        var sampleRows = ReadSampleRows(filePath, sniff, 200);

        // Act
        var proposal = _correlation.Correlate(sniff, probe, sampleRows);

        // Assert — each mapping has a valid confidence and a non-empty reasoning.
        // ZpCode uses "Oracle hit rate" reasoning; other fields use "coverage × similarity".
        foreach (var mapping in proposal.Mappings)
        {
            Assert.InRange(mapping.Confidence, 0.0, 1.0);
            Assert.NotNull(mapping.Reasoning);
            Assert.NotEmpty(mapping.Reasoning);
        }

        // Non-ZpCode mappings should specifically mention coverage
        foreach (var mapping in proposal.Mappings.Where(m => m.FieldName != "ZpCode"))
        {
            Assert.Contains("coverage", mapping.Reasoning, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Correlate_TimezoneColumn_DetectedByIanaFormat()
    {
        // Arrange - create CSV with IANA timezone column using valid US ZIPs
        var tempFile = Path.GetTempFileName();
        var lines = new List<string>();
        lines.Add("10001,New York,NY,America/New_York");
        lines.Add("90210,Beverly Hills,CA,America/Los_Angeles");
        lines.Add("60601,Chicago,IL,America/Chicago");
        File.WriteAllText(tempFile, string.Join("\n", lines));

        try
        {
            var sniff = _sniffer.Sniff(tempFile, sampleRows: 10);
            var probe = _oracle.Probe(tempFile, sniff, sampleRows: 3, minHitRate: 0.70);
            var sampleRows = DelimitedFile.ReadRows(tempFile, sniff.Delimiter, maxRows: 3).ToArray();

            // Act
            var proposal = _correlation.Correlate(sniff, probe, sampleRows);

            // Assert
            var tzMapping = proposal.Mappings.FirstOrDefault(m => m.FieldName == "Timezone");
            Assert.NotNull(tzMapping);
            Assert.Equal(3, tzMapping.ColumnIndex);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}

#endif
