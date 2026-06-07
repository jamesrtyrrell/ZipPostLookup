using Dapper;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal sealed class CountryStatsRow
{
    public string  CountryId            { get; set; } = "";
    public int     TotalTimezoneChecked { get; set; }
    public int     TotalNameChecked     { get; set; }
    public int     TotalCurated         { get; set; }
    public int     Total                { get; set; }
    public int     Remaining            { get; set; }
    public decimal PctComplete          { get; set; }
}

internal static class DashboardStats
{
    /// <summary>
    /// Best-effort load of all-country curation stats. Returns empty list on any failure.
    /// </summary>
    public static async Task<List<CountryStatsRow>> TryLoadAllAsync()
    {
        try
        {
            var db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
            using var conn = db.GetFactory().CreateConnection();
            return (await conn.QueryAsync<CountryStatsRow>(
                CommonQueries.GetAllCountryCurationStats)).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Builds the all-country curation stats table with progress bars, icons, and a totals row.
    /// </summary>
    public static Table BuildTable(List<CountryStatsRow> stats)
    {
        var table = new Table()
            .AddColumn(new TableColumn("[grey]Country[/]"))
            .AddColumn(new TableColumn("[grey]Tz. Checked[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Name Checked[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Curated[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Total[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Remaining[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Progress[/]"));

        foreach (var s in stats)
        {
            var color = s.Remaining == 0 ? "green"
                      : s.PctComplete >= 99m ? "yellow"
                      : "red";

            var remaining = s.Remaining == 0
                ? "[green]✓ 0[/]"
                : $"[yellow]⚠ {s.Remaining:N0}[/]";

            table.AddRow(
                $"[bold {color}]{s.CountryId}[/]",
                $"[{color}]{s.TotalTimezoneChecked:N0}[/]",
                $"[{color}]{s.TotalNameChecked:N0}[/]",
                $"[{color}]{s.TotalCurated:N0}[/]",
                $"[{color}]{s.Total:N0}[/]",
                remaining,
                $"{ProgressBar(s.PctComplete, 10, color)} [bold]{s.PctComplete:F2}[/]"
            );
        }

        if (stats.Count > 0)
        {
            var totalTz  = stats.Sum(s => s.TotalTimezoneChecked);
            var totalNm  = stats.Sum(s => s.TotalNameChecked);
            var totalCur = stats.Sum(s => s.TotalCurated);
            var totalAll = stats.Sum(s => s.Total);
            var totalRem = stats.Sum(s => s.Remaining);

            table.AddRow(
                $"[grey]{stats.Count} countries[/]",
                $"[grey]{totalTz:N0}[/]",
                $"[grey]{totalNm:N0}[/]",
                $"[grey]{totalCur:N0}[/]",
                $"[grey]{totalAll:N0}[/]",
                totalRem == 0
                    ? "[green]✓ 0[/]"
                    : $"[yellow]{totalRem:N0} left[/]",
                $"[grey]{totalCur:N0} / {totalAll:N0} done[/]"
            );
        }

        return table;
    }

    private static string ProgressBar(decimal pct, int width, string color)
    {
        var filled = Math.Clamp((int)Math.Round(pct / 100.0m * width), 0, width);
        return $"[{color}]{new string('█', filled)}{new string('░', width - filled)}[/]";
    }
}
