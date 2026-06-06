using Spectre.Console;

namespace ZipPostLookup.CountryDataTools.Commands.Display;

/// <summary>
/// Shared display helper for consistent 3-column tables used as job headers
/// and result summaries across all CountryDataTools commands.
///
/// Columns: Label | Value | Description
/// When no row carries a Description the third column is omitted automatically.
/// </summary>
public static class CommandDisplay
{
    /// <summary>
    /// Renders a labelled table with up to three columns: Label | Value | Description.
    /// The Description column is only rendered when at least one row supplies a non-empty value.
    /// Headers are hidden — the title carries all the structural context needed.
    /// </summary>
    public static void PrintTable(
        string title,
        params (string Label, string Value, string? Description)[] rows)
    {
        var hasDescriptions = rows.Any(r => !string.IsNullOrWhiteSpace(r.Description));

        var table = new Table()
            .Title($"[bold]{Markup.Escape(title)}[/]")
            .HideHeaders()
            .AddColumn(new TableColumn("label").NoWrap())
            .AddColumn(new TableColumn("value"));

        if (hasDescriptions)
            table.AddColumn(new TableColumn("description"));

        foreach (var (label, value, desc) in rows)
        {
            if (hasDescriptions)
                table.AddRow(Markup.Escape(label), value, desc ?? "");
            else
                table.AddRow(Markup.Escape(label), value);
        }

        AnsiConsole.Write(table);
    }

    /// <summary>Convenience overload for 2-column (Label | Value) job headers.</summary>
    public static void PrintTable(string title, params (string Label, string Value)[] rows) =>
        PrintTable(title, rows.Select(r => (r.Label, r.Value, (string?)null)).ToArray());
}
