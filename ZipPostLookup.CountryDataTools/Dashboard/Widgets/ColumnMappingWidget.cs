using System.Globalization;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;

namespace ZipPostLookup.CountryDataTools.Dashboard.Widgets;

internal enum ColumnMappingOutcome { Accept, Cancel }

internal sealed record ColumnMappingResult(ColumnMappingOutcome Outcome, ColumnMapping Mapping);

/// <summary>
/// Two-pane, keyboard-driven column-mapping screen. The left pane shows the universal template
/// (<see cref="ColumnMapping"/>); the right pane shows the incoming file's columns with a sample
/// value. The user binds each starred field to a column, optionally previews the result over the
/// first few rows (V), and accepts (A) once every mandatory field is mapped.
///
/// Keys: ↑↓ move (editable fields only) · Enter / &gt; bind column · V toggle preview · A accept · Esc cancel.
///
/// The widget is pure UI: it consumes already-parsed <paramref name="sampleRows"/> and returns the
/// completed mapping. Reading/splitting the file (delimiter sniff) is the caller's job.
/// </summary>
internal static class ColumnMappingWidget
{
    private const int PreviewRowCount = 5;

    private static readonly IReadOnlyDictionary<string, string> NoDerivedValues =
        new Dictionary<string, string>(0);

    /// <param name="derivedValues">
    /// Optional provider of read-only values for fields not mapped from a column but populated
    /// downstream — admin levels resolved at the country level, timezone created from coordinates,
    /// IsDefault's default. Invoked per row (current mapping + that row) so it reacts to the bound
    /// key/coordinate columns. Returns field-name → value; shown greyed + "(auto)" on the left
    /// pane and as extra "auto" columns in the bottom validation table, never editable.
    /// </param>
    public static ColumnMappingResult Show(
        string pageTitle,
        ColumnMapping mapping,
        IReadOnlyList<string[]> sampleRows,
        bool showValidation = false,
        Func<ColumnMapping, string[], IReadOnlyDictionary<string, string>>? derivedValues = null)
    {
        var editable = mapping.EditableFields;
        var columnCount = sampleRows.Count > 0 ? sampleRows.Max(r => r.Length) : 0;

        var selected = 0;          // index into editable fields
        var showPreview = showValidation;   // start with the validation table visible when asked
        string? message = null;

        while (true)
        {
            Render(pageTitle, mapping, sampleRows, columnCount, editable, selected, showPreview, message, derivedValues);
            message = null;

            var keyInfo = Console.ReadKey(intercept: true);
            var ch = char.ToUpperInvariant(keyInfo.KeyChar);

            switch (keyInfo.Key)
            {
                case ConsoleKey.UpArrow:
                    if (editable.Count > 0)
                        selected = (selected - 1 + editable.Count) % editable.Count;
                    break;

                case ConsoleKey.DownArrow:
                    if (editable.Count > 0)
                        selected = (selected + 1) % editable.Count;
                    break;

                case ConsoleKey.Enter:
                    BindColumn(pageTitle, editable, selected, sampleRows, columnCount);
                    break;

                case ConsoleKey.Escape:
                    AnsiConsole.Clear();
                    return new ColumnMappingResult(ColumnMappingOutcome.Cancel, mapping);

                default:
                    // '>' binds (mirrors the on-screen cursor), V previews, A accepts.
                    if (keyInfo.KeyChar == '>')
                    {
                        BindColumn(pageTitle, editable, selected, sampleRows, columnCount);
                    }
                    else if (ch == 'V')
                    {
                        showPreview = !showPreview;
                    }
                    else if (ch == 'A')
                    {
                        if (mapping.AllMandatoryMapped)
                        {
                            AnsiConsole.Clear();
                            return new ColumnMappingResult(ColumnMappingOutcome.Accept, mapping);
                        }

                        var missing = string.Join(", ",
                            mapping.Fields.Where(f => f.Mandatory && !f.IsMapped).Select(f => f.Name));
                        message = $"[red]Cannot accept — map all required (*) fields first: {Markup.Escape(missing)}[/]";
                    }
                    break;
            }
        }
    }

    // ── Column chooser (Enter / >) ────────────────────────────────────────────

    private static void BindColumn(
        string pageTitle,
        IReadOnlyList<ColumnMappingField> editable,
        int selected,
        IReadOnlyList<string[]> sampleRows,
        int columnCount)
    {
        if (editable.Count == 0) { return; }

        var field = editable[selected];

        HeaderBar.Render(pageTitle);
        AnsiConsole.MarkupLine($"  Map [bold cyan]{Markup.Escape(field.Name)}[/] to which incoming column?");
        AnsiConsole.WriteLine();

        // -1 = leave unmapped; int.MinValue = Esc/cancel (no change).
        var choices = new List<int> { -1 };
        choices.AddRange(Enumerable.Range(0, columnCount));

        var chosen = CdtSelectMenu.Show(
            choices,
            c => c < 0
                ? "[grey](leave unmapped)[/]"
                : $"[cyan]Column {c}[/]  [grey]{Markup.Escape(Sample(sampleRows, c))}[/]",
            escapeReturns: int.MinValue,
            title: null);

        if (chosen == int.MinValue) { return; }       // cancelled
        field.ColumnIndex = chosen < 0 ? null : chosen;
    }

    // ── Rendering ──────────────────────────────────────────────────────────────

    private static void Render(
        string pageTitle,
        ColumnMapping mapping,
        IReadOnlyList<string[]> sampleRows,
        int columnCount,
        IReadOnlyList<ColumnMappingField> editable,
        int selected,
        bool showPreview,
        string? message,
        Func<ColumnMapping, string[], IReadOnlyDictionary<string, string>>? derivedValues)
    {
        HeaderBar.Render(pageTitle);

        var firstRow     = sampleRows.Count > 0 ? sampleRows[0] : Array.Empty<string>();
        var derivedFirst = derivedValues?.Invoke(mapping, firstRow) ?? NoDerivedValues;

        var template = BuildTemplateTable(mapping, sampleRows, editable, selected, derivedFirst);
        var incoming = BuildIncomingTable(sampleRows, columnCount);
        AnsiConsole.Write(new Columns(template, incoming).Collapse());
        AnsiConsole.WriteLine();

        if (showPreview)
        {
            AnsiConsole.Write(new Rule("[grey]Validation — first rows through current mapping (auto = populated downstream)[/]").LeftJustified());
            AnsiConsole.Write(BuildPreviewTable(mapping, sampleRows, derivedValues));
            AnsiConsole.WriteLine();
        }

        if (message != null)
        {
            AnsiConsole.MarkupLine($"  {message}");
            AnsiConsole.WriteLine();
        }

        var ready = mapping.AllMandatoryMapped ? "[green]A accept[/]" : "[grey]A accept[/]";
        CdtCommandMenu.Render($"  [grey]↑↓ move   Enter/> map column   V preview   Esc cancel[/]   {ready}");
    }

    private static Table BuildTemplateTable(
        ColumnMapping mapping,
        IReadOnlyList<string[]> sampleRows,
        IReadOnlyList<ColumnMappingField> editable,
        int selected,
        IReadOnlyDictionary<string, string> derived)
    {
        var table = new Table()
            .Title("[bold]ZipPostLookup Data[/]")
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("").Width(2))
            .AddColumn(new TableColumn("[grey]*[/]").Width(1))
            .AddColumn(new TableColumn("[grey]Field[/]"))
            .AddColumn(new TableColumn("[grey]Mapped value[/]"));

        var selectedField = editable.Count > 0 ? editable[selected] : null;

        foreach (var field in mapping.Fields)
        {
            var isSelected = ReferenceEquals(field, selectedField);
            var cursor     = isSelected ? "[bold green]❯[/]" : " ";
            var star       = field.Mandatory ? "[yellow]*[/]" : " ";

            string value;
            string colTag;
            if (field.IsMapped)
            {
                value  = Markup.Escape(Sample(sampleRows, field.ColumnIndex!.Value));
                colTag = $" [grey](col {field.ColumnIndex})[/]";
            }
            else if (derived.TryGetValue(field.Name, out var dv) && !string.IsNullOrEmpty(dv))
            {
                // Not mapped from a column, but populated downstream (e.g. admin from the
                // country, timezone from coords, IsDefault default). Show it, never editable.
                value  = Markup.Escape(dv);
                colTag = " [grey](auto)[/]";
            }
            else
            {
                value  = "—";
                colTag = "";
            }

            string name, val;
            if (!field.Mandatory)
            {
                // Locked field — greyed, non-interactive.
                name = $"[grey]{Markup.Escape(field.Name)}[/]";
                val  = $"[grey]{value}[/]{colTag}";
            }
            else if (isSelected)
            {
                name = $"[bold white]{Markup.Escape(field.Name)}[/]";
                val  = $"[bold white]{value}[/]{colTag}";
            }
            else
            {
                name = $"[cyan]{Markup.Escape(field.Name)}[/]";
                val  = $"{value}{colTag}";
            }

            table.AddRow(cursor, star, name, val);
        }

        return table;
    }

    private static Table BuildIncomingTable(IReadOnlyList<string[]> sampleRows, int columnCount)
    {
        var table = new Table()
            .Title("[bold]Incoming Data[/]")
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[grey]Col[/]").Width(3))
            .AddColumn(new TableColumn("[grey]Sample value[/]"));

        for (var c = 0; c < columnCount; c++)
            table.AddRow($"[cyan]{c}[/]", $"[grey]{Markup.Escape(Sample(sampleRows, c))}[/]");

        if (columnCount == 0)
            table.AddRow("", "[red]no columns[/]");

        return table;
    }

    private static Table BuildPreviewTable(
        ColumnMapping mapping,
        IReadOnlyList<string[]> sampleRows,
        Func<ColumnMapping, string[], IReadOnlyDictionary<string, string>>? derivedValues)
    {
        var firstRow     = sampleRows.Count > 0 ? sampleRows[0] : Array.Empty<string>();
        var derivedFirst = derivedValues?.Invoke(mapping, firstRow) ?? NoDerivedValues;

        // Full picture: every field that will be populated — mapped from a column, or
        // auto-populated downstream (admin from the country, timezone from coords, IsDefault).
        var columns = mapping.Fields
            .Where(f => f.IsMapped || derivedFirst.ContainsKey(f.Name))
            .ToList();

        var table = new Table().Border(TableBorder.Minimal);

        if (columns.Count == 0)
        {
            table.AddColumn("[grey](map a column to preview)[/]");
            return table;
        }

        foreach (var f in columns)
        {
            table.AddColumn(f.IsMapped
                ? $"[bold]{Markup.Escape(f.Name)}[/]"
                : $"[bold]{Markup.Escape(f.Name)}[/] [grey]auto[/]");
        }

        foreach (var row in sampleRows.Take(PreviewRowCount))
        {
            var derived = derivedValues?.Invoke(mapping, row) ?? NoDerivedValues;
            var cells = columns.Select(f =>
            {
                if (f.IsMapped)
                {
                    var raw = Cell(row, f.ColumnIndex!.Value);

                    // Lat/Lng must parse as doubles — flag bad mappings in red.
                    var isCoord = f.Name is "Lat" or "Lng";
                    var bad = isCoord && !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
                    var text = Markup.Escape(string.IsNullOrEmpty(raw) ? "—" : raw);
                    return bad ? $"[red]{text}[/]" : text;
                }

                // Auto-populated value for this row (derived / default).
                return derived.TryGetValue(f.Name, out var dv) && !string.IsNullOrEmpty(dv)
                    ? $"[grey]{Markup.Escape(dv)}[/]"
                    : "[grey]—[/]";
            });

            table.AddRow(cells.ToArray());
        }

        return table;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Sample value for a column, taken from the first incoming row.</summary>
    private static string Sample(IReadOnlyList<string[]> sampleRows, int column) =>
        sampleRows.Count > 0 ? Cell(sampleRows[0], column) : "";

    private static string Cell(string[] row, int column) =>
        column >= 0 && column < row.Length ? row[column].Trim() : "";
}
