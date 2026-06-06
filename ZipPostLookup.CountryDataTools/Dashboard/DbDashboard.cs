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
        new("clear",           "clear",            "Clear pipeline working data for a country", Destructive: true),
        new("reset",           "reset",            "Full wipe — removes reference data too",   Destructive: true),
    ];

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[bold cyan]DB[/]").LeftJustified());
            AnsiConsole.WriteLine();

            var back = new Sub("back", "← Back", "");

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<Sub>()
                    .Title("Select subcommand:")
                    .UseConverter(s => s == back
                        ? "[grey]← Back[/]"
                        : s.Destructive
                            ? $"[bold red]{s.Label,-18}[/]  [grey]{s.Description}[/]"
                            : $"[bold cyan]{s.Label,-18}[/]  [grey]{s.Description}[/]")
                    .AddChoices([.. Subs, back]));

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
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[bold cyan]db {sub}[/]").LeftJustified());
        AnsiConsole.WriteLine();

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
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold cyan]db init[/]").LeftJustified());
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  Creates [grey]workdb.json[/] in the current directory.");
        AnsiConsole.MarkupLine("  [grey]Tip: add workdb.json to .gitignore — it contains your connection string.[/]");
        AnsiConsole.WriteLine();

        var country = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("  Country:")
                .AddChoices("US", "CA", "MX"));

        var connection = AnsiConsole.Prompt(
            new TextPrompt<string>("  Connection string:")
                .Validate(s => !string.IsNullOrWhiteSpace(s)
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Connection string is required[/]")));

        var provider = AnsiConsole.Prompt(
            new TextPrompt<string>("  Provider [grey](default: sqlserver)[/]:")
                .DefaultValue("sqlserver")
                .AllowEmpty());

        AnsiConsole.WriteLine();
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold cyan]db init — Running[/]").LeftJustified());
        AnsiConsole.WriteLine();

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
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold cyan]db newrun[/]").LeftJustified());
        AnsiConsole.WriteLine();

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
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold cyan]db normalize-names[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("  Country:")
                .UseConverter(s => s == "All (US + CA + MX)"
                    ? $"[bold cyan]{"All",-10}[/]  [grey]US + CA + MX in sequence[/]"
                    : $"[bold cyan]{s}[/]")
                .AddChoices("US", "CA", "MX", "All (US + CA + MX)"));

        AnsiConsole.WriteLine();

        string[] extra = choice.StartsWith("All")
            ? ["--all"]
            : ["--country", choice];

        return await RunSimpleAsync("normalize-names", extra);
    }

    // ── clear / reset: country picker, then let handler do the confirmation ───

    private static async Task<int> RunWithCountryAsync(string sub, bool needsConfirm)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[bold red]db {sub}[/]").LeftJustified());
        AnsiConsole.WriteLine();

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

        var country = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("  Country:")
                .AddChoices("US", "CA", "MX"));

        AnsiConsole.WriteLine();

        // The handler itself contains AnsiConsole.Confirm (clear) or
        // TextPrompt type-to-confirm (reset) — pass through directly.
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[bold red]db {sub} — {country}[/]").LeftJustified());
        AnsiConsole.WriteLine();

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
