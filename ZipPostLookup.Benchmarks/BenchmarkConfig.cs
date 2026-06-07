using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Loggers;

namespace ZipPostLookup.Benchmarks;

public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        // Output to DataAnalysis/ alongside integrity and analysis reports.
        ArtifactsPath = Path.Combine(Directory.GetCurrentDirectory(), "DataAnalysis");

        AddExporter(MarkdownExporter.GitHub);  // *-report-github.md
        AddExporter(CsvExporter.Default);      // *-report.csv
        AddLogger(ConsoleLogger.Default);
        AddColumnProvider(DefaultColumnProviders.Instance);
    }
}