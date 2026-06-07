using Spectre.Console;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class MenuPrompt
{
    /// <summary>
    /// Keyboard-driven selection menu with Escape support.
    /// Pressing Escape returns <paramref name="escapeReturns"/> — the same value
    /// that existing "← Back" sentinel checks already handle.
    /// </summary>
    public static T Show<T>(
        IReadOnlyList<T> choices,
        Func<T, string> converter,
        T escapeReturns,
        string? title = null)
    {
        if (!string.IsNullOrEmpty(title))
        {
            AnsiConsole.MarkupLine($"  [grey]{title}[/]");
            AnsiConsole.WriteLine();
        }

        var index    = 0;
        var startRow = Console.CursorTop;

        Render(choices, converter, index);

        while (true)
        {
            var key = Console.ReadKey(intercept: true).Key;

            switch (key)
            {
                case ConsoleKey.UpArrow:
                    index = (index - 1 + choices.Count) % choices.Count;
                    Console.SetCursorPosition(0, startRow);
                    Render(choices, converter, index);
                    break;

                case ConsoleKey.DownArrow:
                    index = (index + 1) % choices.Count;
                    Console.SetCursorPosition(0, startRow);
                    Render(choices, converter, index);
                    break;

                case ConsoleKey.Enter:
                    return choices[index];

                case ConsoleKey.Escape:
                    return escapeReturns;
            }
        }
    }

    private static void Render<T>(IReadOnlyList<T> choices, Func<T, string> converter, int selectedIndex)
    {
        for (var i = 0; i < choices.Count; i++)
        {
            var markup = converter(choices[i]);
            if (i == selectedIndex)
                AnsiConsole.MarkupLine($"  [bold green]❯[/] {markup}");
            else
                AnsiConsole.MarkupLine($"    {markup}");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [grey]↑↓ move   Enter select   Esc back[/]");
    }
}
