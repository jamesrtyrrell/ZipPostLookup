using ZipPostLookup.CountryDataTools.Export.Stages;

namespace ZipPostLookup.CountryDataTools.Export;

/// <summary>
/// Orchestrates the export pipeline stages for a given country and writes
/// the optimised CSV to <paramref name="outputPath"/>.
///
/// Pipeline (applied in order):
///   1. <see cref="LatLngStripStage"/>   — strip lat/lng when coverage &lt; 20%
///   2. <see cref="RangeCompressStage"/> — collapse homogeneous prefix groups,
///                                          but only when the row reduction is
///                                          significant (otherwise a no-op so codes
///                                          stay on the exact-key lookup fast path)
///   3. <see cref="StringIndexStage"/>   — replace tz/admin strings with indices
///
/// Active for: CA, US, MX
/// To enable for additional countries: add the country code to
/// <see cref="Commands.ExportReferenceCommand._pipelineCountries"/>.
/// </summary>
internal static class ExportPipeline
{
    /// <summary>
    /// Applies the optimisation stages and returns the transformed rows and metadata,
    /// without writing anything. Shared by the CSV export (<see cref="RunAsync"/>) and the
    /// frozen-image export so both consume identically optimised data.
    /// </summary>
    public static (List<ExportRow> Rows, ExportMeta Meta) Transform(
        string countryCode,
        List<ExportRow> rows,
        string[]? adminLevelNames = null)
    {
        var cc   = countryCode.ToUpperInvariant();
        var meta = new ExportMeta { AdminLevelNames = adminLevelNames };

        var stages = new List<IExportStage>
        {
            new LatLngStripStage(),
            new RangeCompressStage(cc),
            new StringIndexStage(),
        };

        Console.WriteLine($"  Running export pipeline ({stages.Count} stages)…");

        foreach (var stage in stages)
        {
            Console.WriteLine($"  ▸ {stage.StageName}");
            (rows, meta) = stage.Apply(rows, meta);
        }

        meta = meta with { RowCount = rows.Count };
        return (rows, meta);
    }

    public static async Task<ExportMeta> RunAsync(
        string countryCode,
        List<ExportRow> rows,
        string outputPath,
        string[]? adminLevelNames = null)
    {
        var (transformed, meta) = Transform(countryCode, rows, adminLevelNames);

        Console.WriteLine($"  Writing {transformed.Count:N0} rows → {outputPath}");
        await CsvExporter.WriteAsync(transformed, meta, outputPath);

        return meta;
    }
}