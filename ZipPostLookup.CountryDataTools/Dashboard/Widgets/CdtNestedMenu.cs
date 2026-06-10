using Spectre.Console;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;

namespace ZipPostLookup.CountryDataTools.Dashboard.Widgets;

internal sealed record MenuGroup(string Name, string Description, IReadOnlyList<MenuItem> Items);
internal sealed record MenuItem(string Name, string Description, Func<Task<int>>? Action, bool IsFuture = false);

/// <summary>
/// Accordion-style keyboard-driven menu. Five primary groups expand inline on Enter;
/// only one group is open at a time. Future items are greyed and non-interactive.
/// Navigation is level-locked: ↑↓ moves within the active level (primary or submenu).
/// Esc collapses the open submenu; Esc at primary exits the dashboard.
/// </summary>
internal static class CdtNestedMenu
{
    public static async Task RunAsync(IReadOnlyList<MenuGroup> groups, List<CountryStatsRow> stats)
    {
        var primaryIndex   = 0;
        int? expandedGroup = null;
        var submenuIndex   = 0;

        while (true)
        {
            Render(groups, primaryIndex, expandedGroup, submenuIndex, stats);
            var key = Console.ReadKey(intercept: true).Key;

            switch (key)
            {
                case ConsoleKey.UpArrow:
                    if (expandedGroup.HasValue)
                    {
                        var count = groups[expandedGroup.Value].Items.Count;
                        submenuIndex = (submenuIndex - 1 + count) % count;
                    }
                    else
                    {
                        primaryIndex = (primaryIndex - 1 + groups.Count) % groups.Count;
                    }
                    break;

                case ConsoleKey.DownArrow:
                    if (expandedGroup.HasValue)
                    {
                        var count = groups[expandedGroup.Value].Items.Count;
                        submenuIndex = (submenuIndex + 1) % count;
                    }
                    else
                    {
                        primaryIndex = (primaryIndex + 1) % groups.Count;
                    }
                    break;

                case ConsoleKey.Enter:
                    if (!expandedGroup.HasValue)
                    {
                        expandedGroup = primaryIndex;
                        submenuIndex  = 0;
                    }
                    else
                    {
                        var item = groups[expandedGroup.Value].Items[submenuIndex];
                        if (!item.IsFuture && item.Action != null)
                        {
                            await item.Action();
                            stats = await DashboardStats.TryLoadAllAsync();
                        }
                    }
                    break;

                case ConsoleKey.Escape:
                    if (expandedGroup.HasValue)
                    {
                        expandedGroup = null;
                    }
                    else
                    {
                        AnsiConsole.Clear();
                        return;
                    }
                    break;
            }
        }
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private static void Render(
        IReadOnlyList<MenuGroup> groups,
        int primaryIndex,
        int? expandedGroup,
        int submenuIndex,
        List<CountryStatsRow> stats)
    {
        HeaderBar.Render("Dashboard");

        var table = BuildMenuTable(groups, primaryIndex, expandedGroup, submenuIndex);

        if (stats.Count > 0)
            AnsiConsole.Write(new Columns(table, DashboardStats.BuildTable(stats)));
        else
            AnsiConsole.Write(table);

        AnsiConsole.WriteLine();

        var hints = expandedGroup.HasValue
            ? "[grey]↑↓ move   Enter select   Esc back[/]"
            : "[grey]↑↓ move   Enter expand   Esc exit[/]";
        CdtCommandMenu.Render($"  {hints}");
    }

    private static Table BuildMenuTable(
        IReadOnlyList<MenuGroup> groups,
        int primaryIndex,
        int? expandedGroup,
        int submenuIndex)
    {
        var table = new Table()
            .NoBorder()
            .HideHeaders()
            .AddColumn(new TableColumn("").Width(2))
            .AddColumn(new TableColumn("").Width(22))
            .AddColumn(new TableColumn(""));

        for (var i = 0; i < groups.Count; i++)
        {
            var group      = groups[i];
            var isExpanded = expandedGroup == i;
            var isCurrent  = !expandedGroup.HasValue && i == primaryIndex;
            var cursor     = isCurrent ? "[bold green]❯[/]" : " ";

            string nameMarkup, descMarkup;

            if (isCurrent)
            {
                nameMarkup = $"[bold white]{Markup.Escape(group.Name),-20}[/]";
                descMarkup = $"[bold white]{Markup.Escape(group.Description)}[/]";
            }
            else if (isExpanded)
            {
                nameMarkup = $"[bold cyan]{Markup.Escape(group.Name),-20}[/]";
                descMarkup = $"[grey]{Markup.Escape(group.Description)}[/]";
            }
            else
            {
                nameMarkup = $"[cyan]{Markup.Escape(group.Name),-20}[/]";
                descMarkup = $"[grey]{Markup.Escape(group.Description)}[/]";
            }

            table.AddRow(cursor, nameMarkup, descMarkup);

            if (!isExpanded) { continue; }

            for (var j = 0; j < group.Items.Count; j++)
            {
                var item       = group.Items[j];
                var isSelected = j == submenuIndex;
                var subCursor  = isSelected ? "[bold green]❯[/]" : " ";

                string subName, subDesc;

                if (item.IsFuture)
                {
                    subName = $"  [grey]{Markup.Escape(item.Name),-18}[/]";
                    subDesc = $"[grey]{Markup.Escape(item.Description)}[/]";
                }
                else if (isSelected)
                {
                    subName = $"  [bold white]{Markup.Escape(item.Name),-18}[/]";
                    subDesc = $"[bold white]{Markup.Escape(item.Description)}[/]";
                }
                else
                {
                    subName = $"  [cyan]{Markup.Escape(item.Name),-18}[/]";
                    subDesc = $"[grey]{Markup.Escape(item.Description)}[/]";
                }

                table.AddRow(subCursor, subName, subDesc);
            }
        }

        return table;
    }
}
