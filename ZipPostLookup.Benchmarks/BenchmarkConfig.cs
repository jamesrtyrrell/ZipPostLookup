using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Loggers;

namespace ZipPostLookup.Benchmarks;

public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        // AddExporter(MarkdownExporter.GitHub);  // GitHub.md only — no default.md
        AddExporter(CsvExporter.Default);      // keep .csv for archive
        AddLogger(ConsoleLogger.Default);
        AddColumnProvider(DefaultColumnProviders.Instance);
        // No HtmlExporter — no .html
    }
}