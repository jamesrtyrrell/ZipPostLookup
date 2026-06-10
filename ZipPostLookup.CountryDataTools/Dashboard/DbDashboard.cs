using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class DbDashboard
{
    private sealed record Sub(string Key, string Label, string Description, bool Destructive = false);

    private static readonly Sub[] Subs =
    [
        new("status",  "status",  "Show config, connection status, and recent runs"),
        new("test",    "test",    "Verify the connection and schema only"),
        new("init",    "init",    "Create or replace workdb.json"),
        new("newrun",  "newrun",  "Create a new pipeline run and set activeRunId"),
        new("clear",   "clear",   "Clear pipeline working data for a country", Destructive: true),
        new("reset",   "reset",   "Full wipe — removes reference data too",   Destructive: true),
    ];

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("DB");

            var back = new Sub("back", "← Back", "");

            var selected = CdtSelectMenu.Show(
                [.. Subs, back],
                s => s == back
                    ? "[grey]← Back[/]"
                    : s.Destructive
                        ? $"[bold red]{s.Label,-18}[/]  [grey]{s.Description}[/]"
                        : $"[bold cyan]{s.Label,-18}[/]  [grey]{s.Description}[/]",
                escapeReturns: back,
                title: "Select subcommand:");

            if (selected == back)
                break;

            var exitCode = selected.Key switch
            {
                "status"  => await RunSimpleAsync("status", []),
                "test"    => await RunSimpleAsync("test",   []),
                "init"    => await RunInitAsync(),
                "newrun"  => await RunNewRunAsync(),
                "clear"   => await RunWithCountryAsync("clear", needsConfirm: false),
                "reset"   => await RunWithCountryAsync("reset", needsConfirm: false),
                _         => 0,
            };

            _ = exitCode; // caller already shows ✓/✗ in each branch above
        }

        return 0;
    }

    // ── Individual entry points (used by CdtNestedMenu DB Maintenance group) ─

    internal static Task<int> RunStatusAsync() => RunSimpleAsync("status", []);
    internal static Task<int> RunTestAsync()   => RunSimpleAsync("test",   []);
    internal static Task<int> RunClearAsync()  => RunWithCountryAsync("clear", needsConfirm: false);
    internal static Task<int> RunResetAsync()  => RunWithCountryAsync("reset", needsConfirm: false);

    // ── Simple: no args needed ────────────────────────────────────────────────

    private static async Task<int> RunSimpleAsync(string sub, string[] extra)
    {
        HeaderBar.Render($"DB › {sub}");

        var exitCode = await DbCommand.RunAsync([sub, .. extra]);

        FooterBar.ShowResultAndPause(exitCode);
        return exitCode;
    }

    // ── init: country + connection string ─────────────────────────────────────

    internal static async Task<int> RunInitAsync()
    {
        HeaderBar.Render("DB › init");
        AnsiConsole.MarkupLine("  Creates [grey]workdb.json[/] in the current directory.");
        AnsiConsole.MarkupLine("  [grey]Tip: add workdb.json to .gitignore — it contains your connection string.[/]");
        AnsiConsole.WriteLine();

        var country = CountryPicker.Show(
            title: "Country:",
            cancelLabel: "← Cancel");

        if (country == "← Cancel") return 0;

        var connection = AnsiConsole.Prompt(
            new TextPrompt<string>("  Connection string:")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Connection string is required[/]")));

        var provider = AnsiConsole.Prompt(
            new TextPrompt<string>("  Provider [grey](default: sqlserver)[/]:")
                .DefaultValue("sqlserver")
                .AllowEmpty());

        HeaderBar.Render("DB › init");

        var args = new List<string> { "init", "--country", country, "--connection", connection };
        if (!string.IsNullOrWhiteSpace(provider) && provider != "sqlserver")
            args.AddRange(["--provider", provider]);

        var exitCode = await DbCommand.RunAsync([.. args]);

        FooterBar.ShowResultAndPause(exitCode, "workdb.json written", $"Init failed (exit {exitCode})");
        return exitCode;
    }

    // ── newrun: source file path ───────────────────────────────────────────────

    internal static async Task<int> RunNewRunAsync()
    {
        HeaderBar.Render("DB › newrun");

        var source = AnsiConsole.Prompt(
            new TextPrompt<string>("  Source file path [grey](candidate CSV)[/]:")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Path is required[/]")));

        AnsiConsole.WriteLine();

        var exitCode = await DbCommand.RunAsync(["newrun", "--source", source]);

        FooterBar.ShowResultAndPause(exitCode, "Run created", $"Failed (exit {exitCode})");
        return exitCode;
    }

    // ── clear / reset: country picker, then let handler do the confirmation ───

    private static async Task<int> RunWithCountryAsync(string sub, bool needsConfirm)
    {
        HeaderBar.Render($"DB › {sub}");

        if (sub == "reset")
        {
            AnsiConsole.MarkupLine("  [bold red]⚠  WARNING[/]  This will delete all data for the selected country,");
            AnsiConsole.MarkupLine("  including [red]data.reference[/] (the curated rows). This cannot be undone.");
        }
        else
        {
            AnsiConsole.MarkupLine("  Clears pipeline working data for the selected country.");
            AnsiConsole.MarkupLine("  [grey]data.reference is not affected.[/]");
        }

        AnsiConsole.WriteLine();

        var country = CountryPicker.Show(
            title: "Country:",
            cancelLabel: "← Cancel");

        if (country == "← Cancel") return 0;

        // The handler itself contains AnsiConsole.Confirm (clear) or
        // TextPrompt type-to-confirm (reset) — pass through directly.
        HeaderBar.Render($"DB › {sub} › {country}");

        var exitCode = await DbCommand.RunAsync([sub, "--country", country]);

        FooterBar.ShowResultAndPause(exitCode);
        return exitCode;
    }
}
