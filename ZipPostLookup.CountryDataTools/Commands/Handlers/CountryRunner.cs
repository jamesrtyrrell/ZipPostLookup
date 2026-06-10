using Spectre.Console;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// Shared helpers for the curated country set (US/CA/MX) used by <c>--all</c> command paths.
/// </summary>
internal static class CountryRunner
{
    /// <summary>The curated countries, in canonical order.</summary>
    public static readonly string[] All = ["US", "CA", "MX"];

    /// <summary>
    /// Runs <paramref name="perCountry"/> for each of US/CA/MX, printing a blank line +
    /// left-justified Rule header before each. Returns the last non-zero exit code (0 if all
    /// returned 0) — matching the existing <c>--all</c> accumulation behaviour.
    /// </summary>
    public static async Task<int> ForEachWithRuleAsync(Func<string, Task<int>> perCountry)
    {
        var exitCode = 0;
        foreach (var cc in All)
        {
            Console.WriteLine();
            AnsiConsole.Write(new Rule($"[bold]{cc}[/]").LeftJustified());
            var result = await perCountry(cc);
            if (result != 0) exitCode = result;
        }
        return exitCode;
    }
}
