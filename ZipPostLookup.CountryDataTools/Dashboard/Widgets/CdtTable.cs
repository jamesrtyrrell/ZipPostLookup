using Spectre.Console;

namespace ZipPostLookup.CountryDataTools.Dashboard.Widgets;

internal static class CdtTable
{
    // Square border — standard for data display tables
    public static Table Square() => new Table().Border(TableBorder.Square);

    // No border, hidden headers — for layout/menu tables
    public static Table Borderless() => new Table().NoBorder().HideHeaders();

    // Grey-labelled column header
    public static TableColumn Col(string label) => new TableColumn($"[grey]{label}[/]");

    public static TableColumn ColRight(string label) =>
        new TableColumn($"[grey]{label}[/]").RightAligned();

    public static TableColumn ColCenter(string label) =>
        new TableColumn($"[grey]{label}[/]").Centered();
}
