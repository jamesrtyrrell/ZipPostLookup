using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;
using ZipPostLookup.CountryDataTools.Models.Enums;
using ZipPostLookup.CountryDataTools.Utilities;

namespace ZipPostLookup.CountryDataTools.Dashboard;

/// <summary>
/// Interactive TUI wrapper for `import auto`.
/// Accessible via: Data Operations › Auto-Import
/// </summary>
internal static class AutoImportDashboard
{
    public static async Task<int> RunAsync()
    {
        HeaderBar.Render("Auto-Import");

        // ── File path ────────────────────────────────────────────────────────
        var source = AnsiConsole.Prompt(
            new TextPrompt<string>("  File path (CSV/TSV):")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Path is required[/]")));

        var filePath = FileTools.StripPathQuotes(source);
        if (!File.Exists(filePath))
        {
            AnsiConsole.MarkupLine("[red]  File not found.[/]");
            FooterBar.ShowResultAndPause(2);
            return 2;
        }

        AnsiConsole.WriteLine();

        // ── Country override (optional) ──────────────────────────────────────
        var forceCountry = AnsiConsole.Confirm("  Force country (skip oracle detection)?", false);
        string? country = null;
        if (forceCountry)
        {
            var choice = CountryPicker.Show(title: "Country:", cancelLabel: "← Cancel");
            if (choice == "← Cancel") return 0;
            country = choice.ToUpperInvariant();
        }

        AnsiConsole.WriteLine();

        // ── Run options ──────────────────────────────────────────────────────
        var dryRun = AnsiConsole.Confirm("  Dry run (preview only, no DB writes)?", false);
        var noUi   = AnsiConsole.Confirm("  Skip interactive column confirmation?", false);

        AnsiConsole.WriteLine();

        // ── LLM options ──────────────────────────────────────────────────────
        var noLlm      = !AnsiConsole.Confirm("  Enable LLM disambiguation (requires ANTHROPIC_API_KEY)?", true);
        var llmSummary = !noLlm && AnsiConsole.Confirm("  Generate LLM summary after import?", false);

        AnsiConsole.WriteLine();

        // ── Advanced options ─────────────────────────────────────────────────
        var advanced = AnsiConsole.Confirm("  Configure advanced options (sample rows, hit rate threshold)?", false);
        int sampleRows   = 200;
        double minHitRate = 0.70;

        if (advanced)
        {
            sampleRows = AnsiConsole.Prompt(
                new TextPrompt<int>("    Sample rows [200]:")
                    .DefaultValue(200)
                    .Validate(n => n is >= 10 and <= 10_000
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Must be between 10 and 10000[/]")));

            minHitRate = AnsiConsole.Prompt(
                new TextPrompt<double>("    Min hit rate 0.0-1.0 [0.70]:")
                    .DefaultValue(0.70)
                    .Validate(r => r is >= 0.1 and <= 1.0
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Must be between 0.10 and 1.00[/]")));
        }

        AnsiConsole.WriteLine();

        // ── Run ──────────────────────────────────────────────────────────────
        HeaderBar.Render("Auto-Import › running");

        var opts = new AutoImportCommand.Options(
            FilePath:   filePath,
            SampleRows: sampleRows,
            MinHitRate: minHitRate,
            Country:    country,
            DryRun:     dryRun,
            NoLlm:      noLlm,
            LlmSummary: llmSummary,
            NoUi:       noUi);

        var exitCode = await AutoImportCommand.RunAsync(opts);

        FooterBar.ShowResultAndPause(exitCode);
        return exitCode;
    }
}
