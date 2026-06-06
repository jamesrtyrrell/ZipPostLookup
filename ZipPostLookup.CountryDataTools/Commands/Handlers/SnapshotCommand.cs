using Spectre.Console;
using ZipPostLookup.CountryDataTools.Database.WorkDb;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools snapshot [--yes]
///
/// Orchestrates a full end-to-end export pipeline for all pipeline countries (US, CA, MX).
///
/// Steps run in sequence:
///   1. export --target ref --all --curated-only
///      Back up the source-of-truth reference CSVs to CountryDataTools/Data/{cc}/.
///   2. export --all --curated-only
///      Export optimised main CSV + ZPI binary to ZipPostLookup/Data/{cc}/.
///
/// --yes   Skip the interactive confirmation prompt (for scripted / CI use).
///
/// If any step fails the snapshot stops immediately and prints recovery guidance.
/// On success, prints recommended next steps (integrity check, build, test, commit).
/// </summary>
public static class SnapshotCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        var yes = args.Any(a => a is "--yes" or "-y");

        Console.WriteLine();
        Console.WriteLine("  Snapshot will run the following steps:");
        Console.WriteLine("    1. export --target ref --all --curated-only");
        Console.WriteLine("       Back up source-of-truth reference CSVs for US, CA, MX.");
        Console.WriteLine("    2. export --all --curated-only");
        Console.WriteLine("       Export main CSV + ZPI to ZipPostLookup/Data/ for US, CA, MX.");
        Console.WriteLine();

        if (!yes && !AnsiConsole.Confirm("Proceed?"))
        {
            Console.WriteLine("  Snapshot cancelled.");
            return 0;
        }

        Console.WriteLine();

        WorkDbContext db;
        try
        {
            db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [red]✗ DB connection failed: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }

        // ── Step 1: back up source-of-truth reference CSVs ───────────────────
        PrintStepHeader(1, 2, "export --target ref --all --curated-only");

        var refResult = await ExportReferenceCommand.RunAllRefExportAsync(db, curatedOnly: true);

        if (refResult != 0)
        {
            return PrintFailure(refResult,
                "export --target ref --all failed.",
                [
                    "  · Verify the DB is running : countrydatatools db status",
                    "  · Re-run per country       : countrydatatools export --target ref --country XX --curated-only",
                ]);
        }

        // ── Step 2: export main CSV + ZPI ────────────────────────────────────
        Console.WriteLine();
        PrintStepHeader(2, 2, "export --all --curated-only");

        var exportResult = await ExportReferenceCommand.RunAllMainZpiExportAsync(db, curatedOnly: true);

        if (exportResult != 0)
        {
            return PrintFailure(exportResult,
                "export --all failed.",
                [
                    "  · Verify the DB is running : countrydatatools db status",
                    "  · Re-run per country       : countrydatatools export --country XX --curated-only",
                ]);
        }

        // ── All steps passed ─────────────────────────────────────────────────
        Console.WriteLine();
        AnsiConsole.MarkupLine("  [green]✓ Snapshot complete — US, CA, MX.[/]");
        Console.WriteLine();
        Console.WriteLine("  Updated:");
        Console.WriteLine("    CountryDataTools/Data/{us|ca|mx}/  — reference backups");
        Console.WriteLine("    ZipPostLookup/Data/{us|ca|mx}/     — main CSV + ZPI");
        Console.WriteLine();
        Console.WriteLine("  Recommended next steps:");
        Console.WriteLine("    countrydatatools integrity --all --tests 10000");
        Console.WriteLine("    dotnet build ZipPostLookup/ZipPostLookup.csproj -c Release");
        Console.WriteLine("    dotnet test  ZipPostLookup.Tests/ZipPostLookup.Tests.csproj");
        Console.WriteLine("    git add + commit when all checks pass");
        Console.WriteLine();
        return 0;
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static void PrintUsage() =>
        Console.WriteLine("""
            Usage: countrydatatools snapshot [--yes]

              Runs the full export pipeline for all countries (US, CA, MX) in sequence:
                1. export --target ref --all --curated-only   (source-of-truth backup)
                2. export --all --curated-only               (main CSV + ZPI images)

              --yes   Skip the interactive confirmation prompt (for scripted / CI use).
            """);

    private static void PrintStepHeader(int step, int total, string description)
    {
        AnsiConsole.Write(new Rule($"[bold]Step {step} of {total}[/] — {Markup.Escape(description)}").LeftJustified());
        Console.WriteLine();
    }

    private static int PrintFailure(int exitCode, string message, string[] hints)
    {
        Console.WriteLine();
        AnsiConsole.MarkupLine($"  [red]✗ Snapshot failed: {Markup.Escape(message)}[/]");
        Console.WriteLine();
        Console.WriteLine("  Suggested next steps:");
        foreach (var hint in hints)
        {
            Console.WriteLine(hint);
        }
        Console.WriteLine();
        return exitCode;
    }

}
