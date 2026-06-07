using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class DbDashboard
{
    private sealed record Sub(string Key, string Label, string Description, bool Destructive = false);

    private static readonly Sub[] Subs =
    [
        new("status",          "status",          "Show config, connection status, and recent runs"),
        new("test",            "test",             "Verify the connection and schema only"),
        new("init",            "init",             "Create or replace workdb.json"),
        new("newrun",          "newrun",           "Create a new pipeline run and set activeRunId"),
        new("normalize-tz",    "normalize-tz",     "Normalise timezone aliases and resolve from coordinates"),
        new("normalize-names", "normalize-names",  "Detect and link place-name abbreviation alternates"),
        new("purge-oob-us",    "purge-oob-us",     "Remove US ZIP codes outside 00501–99950",  Destructive: true),
        new("clear",           "clear",            "Clear pipeline working data for a country", Destructive: true),
        new("reset",           "reset",            "Full wipe — removes reference data too",   Destructive: true),
    ];

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            DashboardRenderer.RenderHeader("DB");

            var back = new Sub("back", "← Back", "");

            var selected = MenuPrompt.Show(
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
                "status"          => await RunSimpleAsync("status",         []),
                "test"            => await RunSimpleAsync("test",           []),
                "normalize-tz"    => await RunSimpleAsync("normalize-tz",   []),
                "init"            => await RunInitAsync(),
                "newrun"          => await RunNewRunAsync(),
                "normalize-names" => await RunNormalizeNamesAsync(),
                "purge-oob-us"    => await RunSimpleAsync("purge-oob-us",   []),
                "clear"           => await RunWithCountryAsync("clear",     needsConfirm: false),
                "reset"           => await RunWithCountryAsync("reset",     needsConfirm: false),
                _                 => 0,
            };

            _ = exitCode; // caller already shows ✓/✗ in each branch above
        }

        return 0;
    }

    // ── Simple: no args needed ────────────────────────────────────────────────

    private static async Task<int> RunSimpleAsync(string sub, string[] extra)
    {
        DashboardRenderer.RenderHeader($"DB › {sub}");

        var exitCode = await DbCommand.RunAsync([sub, .. extra]);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(exitCode == 0
            ? "[green]  ✓ Done[/]"
            : $"[red]  ✗ Exited with code {exitCode}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
        Console.ReadKey(intercept: true);
        return exitCode;
    }

    // ── init: country + connection string ─────────────────────────────────────

    private static async Task<int> RunInitAsync()
    {
        DashboardRenderer.RenderHeader("DB › init");
        AnsiConsole.MarkupLine("  Creates [grey]workdb.json[/] in the current directory.");
        AnsiConsole.MarkupLine("  [grey]Tip: add workdb.json to .gitignore — it contains your connection string.[/]");
        AnsiConsole.WriteLine();

        var country = MenuPrompt.Show(
            ["US", "CA", "MX", "← Cancel"],
            s => s == "← Cancel" ? "[grey]← Cancel[/]" : $"[bold cyan]{s}[/]",
            escapeReturns: "← Cancel",
            title: "Country:");

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

        DashboardRenderer.RenderHeader("DB › init");

        var args = new List<string> { "init", "--country", country, "--connection", connection };
        if (!string.IsNullOrWhiteSpace(provider) && provider != "sqlserver")
            args.AddRange(["--provider", provider]);

        var exitCode = await DbCommand.RunAsync([.. args]);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(exitCode == 0
            ? "[green]  ✓ workdb.json written[/]"
            : $"[red]  ✗ Init failed (exit {exitCode})[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
        Console.ReadKey(intercept: true);
        return exitCode;
    }

    // ── newrun: source file path ───────────────────────────────────────────────

    private static async Task<int> RunNewRunAsync()
    {
        DashboardRenderer.RenderHeader("DB › newrun");

        var source = AnsiConsole.Prompt(
            new TextPrompt<string>("  Source file path [grey](candidate CSV)[/]:")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Path is required[/]")));

        AnsiConsole.WriteLine();

        var exitCode = await DbCommand.RunAsync(["newrun", "--source", source]);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(exitCode == 0
            ? "[green]  ✓ Run created[/]"
            : $"[red]  ✗ Failed (exit {exitCode})[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
        Console.ReadKey(intercept: true);
        return exitCode;
    }

    // ── normalize-names: country or all ───────────────────────────────────────

    private static async Task<int> RunNormalizeNamesAsync()
    {
        DashboardRenderer.RenderHeader("DB › normalize-names");

        var choice = MenuPrompt.Show(
            ["US", "CA", "MX", "All (US + CA + MX)", "← Cancel"],
            s => s switch
            {
                "← Cancel"           => "[grey]← Cancel[/]",
                "All (US + CA + MX)" => $"[bold cyan]{"All",-10}[/]  [grey]US + CA + MX in sequence[/]",
                _                    => $"[bold cyan]{s}[/]",
            },
            escapeReturns: "← Cancel",
            title: "Country:");

        if (choice == "← Cancel") return 0;

        string[] extra = choice.StartsWith("All")
            ? ["--all"]
            : ["--country", choice];

        return await RunSimpleAsync("normalize-names", extra);
    }

    // ── clear / reset: country picker, then let handler do the confirmation ───

    private static async Task<int> RunWithCountryAsync(string sub, bool needsConfirm)
    {
        DashboardRenderer.RenderHeader($"DB › {sub}");

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

        var country = MenuPrompt.Show(
            ["US", "CA", "MX", "← Cancel"],
            s => s == "← Cancel" ? "[grey]← Cancel[/]" : $"[bold cyan]{s}[/]",
            escapeReturns: "← Cancel",
            title: "Country:");

        if (country == "← Cancel") return 0;

        // The handler itself contains AnsiConsole.Confirm (clear) or
        // TextPrompt type-to-confirm (reset) — pass through directly.
        DashboardRenderer.RenderHeader($"DB › {sub} › {country}");

        var exitCode = await DbCommand.RunAsync([sub, "--country", country]);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(exitCode == 0
            ? "[green]  ✓ Done[/]"
            : $"[red]  ✗ Exited with code {exitCode}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
        Console.ReadKey(intercept: true);
        return exitCode;
    }
}
