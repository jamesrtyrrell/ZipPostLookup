using Xunit;
using ZipPostLookup.CountryDataTools.CountryRules;
using ZipPostLookup.CountryDataTools.Ingestion.AutoImport;
using ZipPostLookup.CountryDataTools.Ingestion.Models;

namespace ZipPostLookup.Tests.Cdt.AutoImport;

/// <summary>
/// Dry-run tests for <see cref="IngestionService"/>'s format gate — codes must be normalized
/// to the country's canonical form (CA: strip spaces, uppercase) and structurally invalid
/// codes rejected before any candidate is built. Regression cover for the 2026-06-16 incident
/// where a test TSV with spaced CA codes ("T0A 0A4") and a numeric header/index row was
/// ingested verbatim into data.Reference as bogus distinct codes.
/// </summary>
public sealed class IngestionServiceTests
{
    private static FileSniffResult TsvSniff(bool hasHeader) => new()
    {
        Format = FileFormat.Tsv,
        Delimiter = '\t',
        HasHeaderRow = hasHeader,
        ColumnCount = 3,
    };

    private static MappingProposal Mapping() => new()
    {
        Mappings =
        [
            new FieldMapping { FieldName = "ZpCode",    ColumnIndex = 0, Confidence = 1.0 },
            new FieldMapping { FieldName = "PlaceName", ColumnIndex = 1, Confidence = 1.0 },
            new FieldMapping { FieldName = "Timezone",  ColumnIndex = 2, Confidence = 1.0 },
        ],
    };

    private static async Task<IngestionResult> DryRunAsync(string country, string[] lines, bool hasHeader = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"zpl-ingest-{Guid.NewGuid():N}.tsv");
        try
        {
            File.WriteAllLines(path, lines);
            var service = new IngestionService(CountryRulesFactory.For(country), country);
            return await service.IngestAsync(path, TsvSniff(hasHeader), new ProbeResult(), Mapping(), dryRun: true);
        }
        finally
        {
            File.Delete(path);
            var rejectedSidecar = Path.ChangeExtension(path, ".rejected.csv");
            if (File.Exists(rejectedSidecar)) { File.Delete(rejectedSidecar); }
        }
    }

    [Fact]
    public async Task Ca_SpacedCode_IsAcceptedAfterNormalization()
    {
        // "T0A 0A4" normalizes to T0A0A4 (valid) — accepted, not rejected as a new bogus code.
        var result = await DryRunAsync("CA", ["T0A 0A4\tLindbergh\tAmerica/Edmonton"]);

        Assert.Equal(1, result.CandidatesGenerated);
        Assert.Equal(0, result.RejectedRows);
    }

    [Fact]
    public async Task Ca_NumericIndexRow_IsRejected()
    {
        // A numeric column-index row ("0	1	2") that a sniffer failed to flag as a header
        // must be rejected by format validation, not ingested as ZpCode='0'.
        var result = await DryRunAsync("CA", ["0\t1\t2", "T0A0A4\tLindbergh\tAmerica/Edmonton"]);

        Assert.Equal(1, result.CandidatesGenerated);
        Assert.Equal(1, result.RejectedRows);
    }

    [Fact]
    public async Task Us_MalformedCode_IsRejected()
    {
        var result = await DryRunAsync("US", ["NOTAZIP\tSomewhere\tAmerica/Chicago"]);

        Assert.Equal(0, result.CandidatesGenerated);
        Assert.Equal(1, result.RejectedRows);
    }

    [Fact]
    public async Task HeaderRow_IsSkipped_WhenSniffSaysSo()
    {
        var result = await DryRunAsync("US",
            ["zip\tcity\ttz", "10001\tNew York\tAmerica/New_York"], hasHeader: true);

        Assert.Equal(1, result.CandidatesGenerated);
        Assert.Equal(0, result.RejectedRows);
    }
}
