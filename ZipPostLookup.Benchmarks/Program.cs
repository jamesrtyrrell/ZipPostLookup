
using BenchmarkDotNet.Running;
using ZipPostLookup.Benchmarks.History;

#if DEBUG

// BenchmarkDotNet requires a Release build to produce meaningful numbers.
// JIT optimisations are disabled in Debug, making results up to 10× slower
// and unrepresentative of real-world performance.
Console.ForegroundColor = ConsoleColor.Yellow;
Console.Error.WriteLine();
Console.Error.WriteLine("  ⚠  Benchmarks must be run in Release mode.");
Console.Error.WriteLine();
Console.Error.WriteLine("     BenchmarkDotNet refuses to produce numbers in Debug — JIT optimisations");
Console.Error.WriteLine("     are disabled, making results up to 10× slower and unrepresentative");
Console.Error.WriteLine("     of real-world performance.");
Console.Error.WriteLine();
Console.Error.WriteLine("     Run with:");
Console.Error.WriteLine("       dotnet run -c Release --project ZipPostLookup.Benchmarks");
Console.Error.WriteLine();
Console.Error.WriteLine("     To run a specific class:");
Console.Error.WriteLine("       dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter \"*LookupBenchmarks*\"");
Console.Error.WriteLine("       dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter \"*ConstructionBenchmarks*\"");
Console.Error.WriteLine("       dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter \"*InternationalLookup*\"");
Console.Error.WriteLine("       dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter \"*CaRange*\"");
Console.Error.WriteLine("       dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter \"*Batch*\"");
Console.Error.WriteLine();
Console.Error.WriteLine("     To run the full CSV vs ZP-image comparison (all countries):");
Console.Error.WriteLine("       dotnet run -c Release --project ZipPostLookup.Benchmarks -- --filter \"*ZpImageBenchmarks*\" \"*ClosestImageBenchmarks*\" \"*ConstructionBenchmarks*\"");
Console.Error.WriteLine();
Console.ResetColor();

#else

var runStarted = DateTime.UtcNow;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

// ── History archiving ────────────────────────────────────────────────────────

// Resolve paths: bin/Release/net8.0 → up 3 levels → project folder → History/
var binDir     = new DirectoryInfo(AppContext.BaseDirectory);
var projectDir = binDir.Parent?.Parent?.Parent?.FullName ?? Directory.GetCurrentDirectory();
var historyDir = Path.Combine(projectDir, "History");

// BenchmarkDotNet writes CSVs relative to the working directory (typically solution root).
var artifactsDir = Path.Combine(Directory.GetCurrentDirectory(),
                                "BenchmarkDotNet.Artifacts", "results");
if (!Directory.Exists(artifactsDir))
    artifactsDir = Path.Combine(projectDir, "BenchmarkDotNet.Artifacts", "results");

var freshReports = HistoryArchiver.FindFreshReports(artifactsDir, runStarted);

if (freshReports.Count == 0)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine();
    Console.WriteLine("  (No fresh benchmark reports found — history archiving skipped.)");
    Console.ResetColor();
}
else
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"  {freshReports.Count} benchmark report(s) produced this run:");
    Console.ResetColor();

    foreach (var (className, _) in freshReports)
        Console.WriteLine($"    • {className}");

    Console.WriteLine();
    Console.Write("  Add results to History archive? [y/N] ");
    var answer = Console.ReadLine()?.Trim().ToUpperInvariant();

    if (answer == "Y")
    {
        Console.WriteLine();
        Directory.CreateDirectory(historyDir);

        var archived = 0;
        var skipped  = 0;

        foreach (var (className, csvPath) in freshReports)
        {
            Console.Write($"  Archiving {className}... ");
            try
            {
                var added = HistoryArchiver.AppendToArchive(historyDir, className, csvPath);
                if (added)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✓  → History/{className}.tar.gz");
                    Console.ResetColor();
                    archived++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗  {ex.Message}");
                Console.ResetColor();
            }
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Done — {archived} archived, {skipped} skipped.");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Skipped.");
        Console.ResetColor();
    }
}

Console.WriteLine();

#endif
