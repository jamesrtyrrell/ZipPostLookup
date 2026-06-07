using Spectre.Console;
using ZipPostLookup.CountryDataTools.Commands;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class ValidateDashboard
{
    public static async Task<int> RunAsync()
    {
        while (true)
        {
            DashboardRenderer.RenderHeader("Validate");
            AnsiConsole.MarkupLine("  Validates a candidate CSV and guides you through fix/extract steps.");
            AnsiConsole.WriteLine();

            var file = AnsiConsole.Prompt(
                new TextPrompt<string>("  Candidate CSV path [grey](or blank to go back)[/]:")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(file)) break;

            var country = MenuPrompt.Show(
                ["US", "CA", "MX", "← Cancel"],
                s => s == "← Cancel" ? "[grey]← Cancel[/]" : $"[bold cyan]{s}[/]",
                escapeReturns: "← Cancel",
                title: "Country:");

            if (country == "← Cancel") continue;

            var noPrompts = AnsiConsole.Confirm("  --no-prompts (apply fix + extract automatically)?", false);

            var args = new List<string> { file, "--country", country };
            if (noPrompts) args.Add("--no-prompts");

            DashboardRenderer.RenderHeader("Validate");

            var exitCode = await ValidateCommand.RunAsync([.. args]);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(exitCode == 0
                ? "[green]  ✓ Done[/]"
                : $"[red]  ✗ Exited with code {exitCode}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
            Console.ReadKey(intercept: true);
        }

        return 0;
    }
}
