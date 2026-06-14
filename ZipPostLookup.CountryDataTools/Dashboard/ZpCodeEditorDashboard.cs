using Dapper;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;
using ZipPostLookup.CountryDataTools.Database;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class ZpCodeEditorDashboard
{
    private const int PageSize = 15;

    // ── DTOs ───────────────────────────────────────────────────────────────────

    private sealed class BrowseRow
    {
        public string  ZpCode          { get; set; } = "";
        public string  PlaceName       { get; set; } = "";
        public string  Timezone        { get; set; } = "";
        public bool    IsDefault       { get; set; }
        public string  Lat             { get; set; } = "---";
        public string  Lng             { get; set; } = "---";
        public string? AltNameOf       { get; set; }
        public string  Admin1          { get; set; } = "---";
        public string  Admin1Code      { get; set; } = "---";
        public bool    TimezoneChecked { get; set; }
        public bool    NameChecked     { get; set; }
        public DataFlagReasonType Flagged { get; set; }
    }

    private sealed class DetailRow
    {
        public long    ReferenceId     { get; set; }
        public string  ZpCode          { get; set; } = "";
        public string  PlaceName       { get; set; } = "";
        public string  Timezone        { get; set; } = "";
        public bool    IsDefault       { get; set; }
        public string  Lat             { get; set; } = "---";
        public string  Lng             { get; set; } = "---";
        public string? AltNameOf       { get; set; }
        public string  Admin1          { get; set; } = "---";
        public string  Admin1Code      { get; set; } = "---";
        public string  Admin2          { get; set; } = "---";
        public string  Admin2Code      { get; set; } = "---";
        public bool    TimezoneChecked { get; set; }
        public bool    NameChecked     { get; set; }
        public DataFlagReasonType Flagged { get; set; }
        public bool    IsGold          { get; set; }
    }

    private sealed class CurationStats
    {
        public int     TotalTimezoneChecked { get; set; }
        public int     TotalNameChecked     { get; set; }
        public int     TotalCurated         { get; set; }
        public int     Total                { get; set; }
        public int     Remaining            { get; set; }
        public decimal PctComplete          { get; set; }
        public int     OrphanAltNames       { get; set; }
        public int     GoldCount            { get; set; }
    }

    private sealed class CandidateBrowseRow
    {
        public string ZpCode     { get; set; } = "";
        public string PlaceName  { get; set; } = "";
        public string Timezone   { get; set; } = "";
        public bool   IsDefault  { get; set; }
        public string Status     { get; set; } = "";
        public string Admin1     { get; set; } = "---";
        public string Admin1Code { get; set; } = "---";
    }

    private sealed class CandidateStatusCount
    {
        public string Status { get; set; } = "";
        public int    Count  { get; set; }
    }

    // ── Entry point ────────────────────────────────────────────────────────────

    public static async Task<int> RunAsync()
    {
        while (true)
        {
            HeaderBar.Render("ZpCode Editor");

            var entryStats = await DashboardStats.TryLoadAllAsync();

            if (entryStats.Count > 0)
            {
                AnsiConsole.Write(DashboardStats.BuildTable(entryStats));
                AnsiConsole.WriteLine();
            }
            else
            {
                AnsiConsole.MarkupLine(
                    "  [grey]Browse and curate uncurated reference codes from the working DB.[/]");
                AnsiConsole.MarkupLine(
                    "  [grey](Could not reach DB — check workdb.json)[/]");
                AnsiConsole.WriteLine();
            }

            var country = CountryPicker.Show(
                title: "Country:",
                cancelLabel: "← Back");

            if (country == "← Back") break;

            HeaderBar.Render("ZpCode Editor");

            var mode = CdtSelectMenu.Show(
                ["Edit Uncurated", "Edit Flagged", "Candidate", "← Back"],
                s => s switch
                {
                    "← Back"       => "[grey]← Back[/]",
                    "Edit Flagged" => $"[bold red]{"Edit Flagged",-18}[/]  [grey]Browse and manage flagged (bad-actor) codes[/]",
                    "Candidate"    => $"[bold yellow]{"Candidate",-18}[/]  [grey]Browse pipeline candidate codes by status[/]",
                    _              => $"[bold cyan]{"Edit Uncurated",-18}[/]  [grey]Browse and curate uncurated reference codes[/]",
                },
                escapeReturns: "← Back",
                title: $"Mode — {country}:");

            if (mode == "← Back") continue;

            if (mode == "Candidate")
                await BrowseCandidatesAsync(country);
            else
                await BrowseCodesAsync(country, flaggedMode: mode == "Edit Flagged");
        }

        return 0;
    }

    // ── Browse (ZpCode Editor › {CC}) ─────────────────────────────────────────

    private static async Task BrowseCodesAsync(string country, bool flaggedMode = false)
    {
        var modeLabel = flaggedMode ? "Flagged" : country;
        var header    = flaggedMode ? $"ZpCode Editor › {country} › Flagged" : $"ZpCode Editor › {country}";

        IWorkDbConnectionFactory factory;
        try
        {
            var db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
            factory = db.GetFactory();
        }
        catch (Exception ex)
        {
            HeaderBar.Render(header);
            AnsiConsole.MarkupLine($"[red]  ✗ {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.WriteLine();
            FooterBar.PressAnyKey();
            Console.ReadKey(intercept: true);
            return;
        }

        CurationStats? stats = null;

        await PaginatedBrowser.RunAsync<BrowseRow>(
            PageSize,
            loadPage: async off =>
            {
                var (p, t, s) = await LoadBrowsePageAsync(factory, country, off, flaggedMode);
                stats = s;
                return (p, t);
            },
            render: (p, sel, off, total) =>
                RenderBrowsePage(country, p, sel, off, total, stats, flaggedMode),
            onEnter: row => ViewCodeAsync(factory, country, row.ZpCode),
            onEmpty: async () =>
            {
                HeaderBar.Render(header);
                AnsiConsole.Write(BuildCurrentCountryStatsTable(stats, country));
                AnsiConsole.WriteLine();

                if (!flaggedMode && stats?.OrphanAltNames > 0)
                {
                    AnsiConsole.MarkupLine(
                        $"  [yellow]⚠  {stats.OrphanAltNames:N0} alt-name row(s) still uncurated[/]  " +
                        "[grey](orphaned from canonical rows)[/]");
                    AnsiConsole.MarkupLine(
                        "  [grey]Press [/][bold]O[/][grey] to propagate curation, or any other key to return.[/]");
                    AnsiConsole.WriteLine();

                    if (Console.ReadKey(intercept: true).Key == ConsoleKey.O)
                    {
                        await RunOrphanFixAsync(factory, country);
                        return false;   // reload from 0 and continue browsing
                    }
                    return true;
                }

                AnsiConsole.MarkupLine(flaggedMode
                    ? "[green]  ✓ No flagged codes.[/]"
                    : "[green]  ✓ All codes are curated![/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
                Console.ReadKey(intercept: true);
                return true;
            },
            onKey: async key =>
            {
                if (key == ConsoleKey.O && !flaggedMode && stats?.OrphanAltNames > 0)
                {
                    await RunOrphanFixAsync(factory, country);
                    return true;   // data changed → reload current page
                }
                return false;
            });
    }

    // ── ZpCode detail (ZpCode Editor › {CC} › {ZpCode}) ──────────────────────

    private static async Task ViewCodeAsync(
        IWorkDbConnectionFactory factory, string country, string zpCode)
    {
        var rows = await LoadDetailRowsAsync(factory, country, zpCode);
        if (rows is null) return;

        var selectedIndex = 0;

        while (true)
        {
            var flagReason = rows.Count > 0 ? rows[selectedIndex].Flagged : DataFlagReasonType.Valid;
            HeaderBar.Render($"ZpCode Editor › {country} › {zpCode}");

            AnsiConsole.Write(BuildDetailTable(rows, selectedIndex));
            AnsiConsole.WriteLine();

            var flagHint = flagReason != DataFlagReasonType.Valid
                ? $"[red bold]F[/][grey] flag row ([/][red]{flagReason}[/][grey])[/]"
                : "[bold]F[/][grey] flag row[/]";
            CdtCommandMenu.Render(
                $"  [bold]C[/][grey] curate all   [/][bold]T[/][grey] TZ checked   [/]" +
                $"[bold]N[/][grey] names checked   [/][bold]E[/][grey] edit   {flagHint}   Esc back[/]");

            var key = Console.ReadKey(intercept: true).Key;

            switch (key)
            {
                case ConsoleKey.UpArrow:
                    if (selectedIndex > 0) selectedIndex--;
                    break;

                case ConsoleKey.DownArrow:
                    if (selectedIndex < rows.Count - 1) selectedIndex++;
                    break;

                case ConsoleKey.C:
                    await RunUpdateAsync("mark curated",
                        () => ExecAsync(factory, CommonQueries.MarkCodeAsCurated,
                            new { CountryId = country, ZpCode = zpCode }));
                    rows = await LoadDetailRowsAsync(factory, country, zpCode) ?? rows;
                    if (rows.All(r => r.TimezoneChecked && r.NameChecked))
                    {
                        await TryCertifyGoldAsync(factory, country, zpCode);
                        return;
                    }
                    break;

                case ConsoleKey.T:
                    await RunUpdateAsync("mark TZ checked",
                        () => ExecAsync(factory, CommonQueries.MarkCodeTimezoneChecked,
                            new { CountryId = country, ZpCode = zpCode }));
                    rows = await LoadDetailRowsAsync(factory, country, zpCode) ?? rows;
                    if (rows.All(r => r.TimezoneChecked && r.NameChecked))
                    {
                        await TryCertifyGoldAsync(factory, country, zpCode);
                        return;
                    }
                    break;

                case ConsoleKey.N:
                    await RunUpdateAsync("mark names checked",
                        () => ExecAsync(factory, CommonQueries.MarkCodeNameChecked,
                            new { CountryId = country, ZpCode = zpCode }));
                    rows = await LoadDetailRowsAsync(factory, country, zpCode) ?? rows;
                    if (rows.All(r => r.TimezoneChecked && r.NameChecked))
                    {
                        await TryCertifyGoldAsync(factory, country, zpCode);
                        return;
                    }
                    break;

                case ConsoleKey.E when rows.Count > 0:
                    var renamedTo = await EditRowAsync(factory, country, zpCode, rows[selectedIndex]);
                    if (renamedTo is not null) zpCode = renamedTo;
                    rows = await LoadDetailRowsAsync(factory, country, zpCode) ?? rows;
                    selectedIndex = Math.Min(selectedIndex, Math.Max(0, rows.Count - 1));
                    break;

                case ConsoleKey.F when rows.Count > 0:
                    var target = rows[selectedIndex];
                    var chosen = PromptFlagReason(target.Flagged, target.PlaceName);
                    if (chosen is { } reason && reason != target.Flagged)
                    {
                        await RunUpdateAsync(
                            $"set flag reason → {reason}",
                            () => ExecAsync(factory, CommonQueries.SetReferenceFlagReasonById,
                                new { target.ReferenceId, Flagged = (int)reason }));
                        rows = await LoadDetailRowsAsync(factory, country, zpCode) ?? rows;
                    }
                    break;

                case ConsoleKey.Escape:
                    return;
            }
        }
    }

    // ── Edit row (ZpCode Editor › {CC} › {ZpCode} › Edit) ────────────────────

    private static readonly string[] EditableFields =
        ["Code", "PlaceName", "Timezone", "Coordinates", "AltNameOf",
         "Admin1Code", "Admin1Name", "Admin2Code", "Admin2Name"];

    private static async Task<string?> EditRowAsync(
        IWorkDbConnectionFactory factory, string country, string zpCode, DetailRow row)
    {
        var selectedField = 0;
        string? renamedTo = null;

        while (true)
        {
            // Use row.ZpCode so the header reflects the current code after a rename.
            HeaderBar.Render($"ZpCode Editor › {country} › {row.ZpCode} › Edit");
            AnsiConsole.MarkupLine("  [grey]Select a field and press Enter to edit.[/]");
            AnsiConsole.WriteLine();

            AnsiConsole.Write(BuildEditTable(row, selectedField));
            AnsiConsole.WriteLine();
            CdtCommandMenu.Render("  [grey]↑↓ move   Enter edit   Esc back[/]");

            var key = Console.ReadKey(intercept: true).Key;

            switch (key)
            {
                case ConsoleKey.UpArrow:
                    if (selectedField > 0) selectedField--;
                    break;

                case ConsoleKey.DownArrow:
                    if (selectedField < EditableFields.Length - 1) selectedField++;
                    break;

                case ConsoleKey.Enter:
                    var newCode = await EditFieldAsync(factory, country, row.ZpCode, row, EditableFields[selectedField]);
                    if (newCode is not null) renamedTo = newCode;
                    var updated = await LoadSingleDetailRowAsync(factory, row.ReferenceId);
                    if (updated is not null) row = updated;
                    break;

                case ConsoleKey.Escape:
                    return renamedTo;
            }
        }
    }

    private static async Task<string?> EditFieldAsync(
        IWorkDbConnectionFactory factory, string country, string zpCode,
        DetailRow row, string fieldName)
    {
        HeaderBar.Render(
            $"ZpCode Editor › {country} › {zpCode} › Edit › {fieldName}");

        // Coordinates are edited as a pair on their own page — you cannot set Lat or Lng
        // independently (the lat/lng pairing rule would blank a half-populated pair).
        if (fieldName == "Coordinates")
        {
            await EditCoordinatesAsync(factory, row);
            return null;
        }

        var currentValue = GetFieldValue(row, fieldName);
        AnsiConsole.MarkupLine(
            $"  Current: [grey]{Markup.Escape(currentValue)}[/]  [grey](blank = cancel)[/]");
        AnsiConsole.WriteLine();

        var input = AnsiConsole.Prompt(
            new TextPrompt<string>($"  New {fieldName}:").AllowEmpty());

        if (string.IsNullOrWhiteSpace(input)) return null;
        var newValue = input.Trim();

        try
        {
            using var conn = factory.CreateConnection();

            switch (fieldName)
            {
                case "Code":
                    if (string.Equals(newValue, zpCode, StringComparison.OrdinalIgnoreCase))
                    {
                        AnsiConsole.MarkupLine("  [grey]Code unchanged.[/]");
                        return null;
                    }
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine(
                        $"  [yellow]⚠  Change code [bold]{Markup.Escape(zpCode)}[/] → [bold]{Markup.Escape(newValue)}[/]" +
                        $" for ReferenceId [bold]{row.ReferenceId}[/] ([grey]{Markup.Escape(row.PlaceName)}[/]) only.[/]");
                    AnsiConsole.MarkupLine("  [grey]Press [/][bold yellow]Y[/][grey] to confirm, any other key cancels.[/]");
                    if (Console.ReadKey(intercept: true).Key != ConsoleKey.Y)
                    {
                        AnsiConsole.MarkupLine("[grey]  Cancelled.[/]");
                        return null;
                    }
                    await ExecAsync(factory,
                        CommonQueries.RenameZpCode,
                        new { row.ReferenceId, NewCode = newValue });
                    return newValue;

                case "PlaceName":
                    await ExecAsync(factory, CommonQueries.UpdateReferencePlaceNameById,
                        new { ReferenceId = row.ReferenceId, PlaceName = newValue });
                    break;

                case "Timezone":
                    await ExecAsync(factory, CommonQueries.UpdateReferenceTimezoneById,
                        new { ReferenceId = row.ReferenceId, Timezone = newValue });
                    break;

                case "AltNameOf":
                    var altValue = newValue == "---" || newValue == "—" ? null : newValue;
                    await ExecAsync(factory, CommonQueries.UpdateReferenceAltNameOfById,
                        new { ReferenceId = row.ReferenceId, AltNameOf = altValue });
                    break;

                case "Admin1Code":
                case "Admin1Name":
                    var al1Id = await conn.ExecuteScalarAsync<int?>(
                        CommonQueries.GetAdminLevelIdForLevel,
                        new { CountryId = country, LevelNumber = 1 });
                    if (al1Id is null)
                    {
                        AnsiConsole.MarkupLine("[yellow]  No level-1 admin defined for this country.[/]");
                        AnsiConsole.MarkupLine("[grey]  Press any key...[/]");
                        Console.ReadKey(intercept: true);
                        break;
                    }
                    await ExecAsync(factory, CommonQueries.UpsertReferenceAdmin,
                        new
                        {
                            ReferenceId  = row.ReferenceId,
                            AdminLevelId = al1Id.Value,
                            Value        = fieldName == "Admin1Name" ? newValue : row.Admin1,
                            Code         = fieldName == "Admin1Code" ? newValue : row.Admin1Code,
                        });
                    break;

                case "Admin2Code":
                case "Admin2Name":
                    var al2Id = await conn.ExecuteScalarAsync<int?>(
                        CommonQueries.GetAdminLevelIdForLevel,
                        new { CountryId = country, LevelNumber = 2 });
                    if (al2Id is null)
                    {
                        AnsiConsole.MarkupLine("[yellow]  No level-2 admin defined for this country.[/]");
                        AnsiConsole.MarkupLine("[grey]  Press any key...[/]");
                        Console.ReadKey(intercept: true);
                        break;
                    }
                    await ExecAsync(factory, CommonQueries.UpsertReferenceAdmin,
                        new
                        {
                            ReferenceId  = row.ReferenceId,
                            AdminLevelId = al2Id.Value,
                            Value        = fieldName == "Admin2Name" ? newValue : row.Admin2,
                            Code         = fieldName == "Admin2Code" ? newValue : row.Admin2Code,
                        });
                    break;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]  ✗ Update failed: {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.MarkupLine("[grey]  Press any key...[/]");
            Console.ReadKey(intercept: true);
        }

        return null;
    }

    /// <summary>
    /// Dedicated page to edit a row's Lat + Lng together. Each prompt defaults to the current value
    /// so either can be kept by pressing Enter. The pair is run through
    /// <see cref="Models.Dbo.DataReference.NormalizeCoordinatePair"/> before writing, so an incomplete
    /// pair is stored as ('---','---') rather than a stranded coordinate. Writes both via one UPDATE.
    /// </summary>
    private static async Task EditCoordinatesAsync(IWorkDbConnectionFactory factory, DetailRow row)
    {
        AnsiConsole.MarkupLine(
            $"  [grey]Current: Lat [/][white]{Markup.Escape(row.Lat)}[/][grey]  Lng [/][white]{Markup.Escape(row.Lng)}[/]");
        AnsiConsole.MarkupLine(
            "  [grey]Edit both — Enter keeps the current value. Set both to '---' to clear coordinates.[/]");
        AnsiConsole.WriteLine();

        var latInput = AnsiConsole.Prompt(
            new TextPrompt<string>("  Lat:").DefaultValue(row.Lat).AllowEmpty());
        var lngInput = AnsiConsole.Prompt(
            new TextPrompt<string>("  Lng:").DefaultValue(row.Lng).AllowEmpty());

        var coord = new Models.Dbo.DataReference
        {
            Lat = string.IsNullOrWhiteSpace(latInput) ? "---" : latInput.Trim(),
            Lng = string.IsNullOrWhiteSpace(lngInput) ? "---" : lngInput.Trim(),
        };
        var blanked = coord.NormalizeCoordinatePair();

        try
        {
            await ExecAsync(factory, CommonQueries.UpdateReferenceCoordsById,
                new { row.ReferenceId, coord.Lat, coord.Lng });

            if (blanked)
                AnsiConsole.MarkupLine(
                    "  [yellow]⚠  Coordinates must be a complete pair — both cleared to '---' (one coordinate was missing or invalid).[/]");
            else
                AnsiConsole.MarkupLine(
                    $"  [green]✓ Coordinates set to Lat {Markup.Escape(coord.Lat)}, Lng {Markup.Escape(coord.Lng)}.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]  ✗ Update failed: {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.MarkupLine("[grey]  Press any key...[/]");
            Console.ReadKey(intercept: true);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string GetFieldValue(DetailRow row, string fieldName) => fieldName switch
    {
        "Code"        => row.ZpCode,
        "PlaceName"   => row.PlaceName,
        "Timezone"    => row.Timezone,
        "Coordinates" => $"{row.Lat}, {row.Lng}",
        "AltNameOf"   => row.AltNameOf ?? "—",
        "Admin1Code" => row.Admin1Code,
        "Admin1Name" => row.Admin1,
        "Admin2Code" => row.Admin2Code,
        "Admin2Name" => row.Admin2,
        _            => "",
    };

    private static async Task RunOrphanFixAsync(IWorkDbConnectionFactory factory, string country)
    {
        try
        {
            var affected = await ExecAsync(factory,
                CommonQueries.FixOrphanAltNames,
                new { CountryId = country });
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]  ✓ {affected:N0} alt-name row(s) marked curated[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]  ✗ Fix failed: {Markup.Escape(ex.Message)}[/]");
        }
        AnsiConsole.MarkupLine("[grey]  Press any key...[/]");
        Console.ReadKey(intercept: true);
    }

    /// <summary>
    /// Prompts for a flag reason (v/f/c/o → <see cref="DataFlagReasonType"/>) for a single row.
    /// Returns the chosen reason, or <c>null</c> if cancelled (Esc). The target place name and its
    /// current reason are shown for context.
    /// </summary>
    private static DataFlagReasonType? PromptFlagReason(DataFlagReasonType current, string placeName)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"  [grey]Flagging row [/][bold]{Markup.Escape(placeName)}[/][grey] — current: [/][bold]{current}[/]");
        AnsiConsole.MarkupLine(
            "  [grey]Set reason — [/]" +
            "[bold]V[/][grey]alid  [/][bold]F[/][grey]lagged  [/][bold]C[/][grey]ommonFake  [/]" +
            "[bold]O[/][grey]bsolete   Esc cancel[/]");

        while (true)
        {
            switch (Console.ReadKey(intercept: true).Key)
            {
                case ConsoleKey.V: return DataFlagReasonType.Valid;
                case ConsoleKey.F: return DataFlagReasonType.Flagged;
                case ConsoleKey.C: return DataFlagReasonType.CommonFake;
                case ConsoleKey.O: return DataFlagReasonType.Obsolete;
                case ConsoleKey.Escape: return null;
            }
        }
    }

    private static async Task RunUpdateAsync(string label, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]  ✗ {label} failed: {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.MarkupLine("[grey]  Press any key...[/]");
            Console.ReadKey(intercept: true);
        }
    }

    /// <summary>
    /// Runs a single write through the sanctioned <see cref="CommandServices"/> (Rule 1:
    /// writes go through services, never a raw connection). The editor holds only the
    /// connection factory, which is all the service needs. Reads stay inline (sanctioned).
    /// </summary>
    private static Task<int> ExecAsync(
        IWorkDbConnectionFactory factory, string commonQuery, object? parameters = null)
        => new CommandServices(factory).ExecuteAsync(commonQuery, parameters);

    private static async Task<List<DetailRow>?> LoadDetailRowsAsync(
        IWorkDbConnectionFactory factory, string country, string zpCode)
    {
        try
        {
            using var conn = factory.CreateConnection();
            return (await conn.QueryAsync<DetailRow>(
                CommonQueries.GetCodeDetailRows,
                new { CountryId = country, ZpCode = zpCode })).ToList();
        }
        catch (Exception ex)
        {
            HeaderBar.Render($"ZpCode Editor › {country} › {zpCode}");
            AnsiConsole.MarkupLine($"[red]  ✗ {Markup.Escape(ex.Message)}[/]");
            FooterBar.PressAnyKey();
            Console.ReadKey(intercept: true);
            return null;
        }
    }

    private static async Task<DetailRow?> LoadSingleDetailRowAsync(
        IWorkDbConnectionFactory factory, long referenceId)
    {
        try
        {
            using var conn = factory.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<DetailRow>(
                CommonQueries.GetCodeDetailRowById,
                new { ReferenceId = referenceId });
        }
        catch
        {
            return null;
        }
    }

    private static async Task TryCertifyGoldAsync(
        IWorkDbConnectionFactory factory, string country, string zpCode)
    {
        try
        {
            using var conn = factory.CreateConnection();
            var reason = await conn.ExecuteScalarAsync<string?>(
                CommonQueries.CheckGoldEligibility,
                new { CountryId = country, ZpCode = zpCode });
            if (reason is null)
            {
                await ExecAsync(factory,
                    CommonQueries.SetGoldCode,
                    new { CountryId = country, ZpCode = zpCode, ChecksVersion = 1 });
                AnsiConsole.MarkupLine("[bold yellow]  ⭐ Gold certified[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[grey]  (Not gold-eligible: {Markup.Escape(reason)})[/]");
            }
            AnsiConsole.MarkupLine("[grey]  Press any key…[/]");
            Console.ReadKey(intercept: true);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[grey]  (Gold check skipped: {Markup.Escape(ex.Message)})[/]");
            AnsiConsole.MarkupLine("[grey]  Press any key…[/]");
            Console.ReadKey(intercept: true);
        }
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private static void RenderBrowsePage(
        string country,
        List<BrowseRow> page,
        int selectedIndex,
        int offset,
        int totalCount,
        CurationStats? stats,
        bool flaggedMode = false)
    {
        var header = flaggedMode
            ? $"ZpCode Editor › {country} › Flagged"
            : $"ZpCode Editor › {country}";
        HeaderBar.Render(header);

        var browseTable  = BuildBrowseTable(page, selectedIndex, offset, totalCount, stats, flaggedMode);
        var countryPanel = BuildCurrentCountryStatsTable(stats, country);

        AnsiConsole.Write(new Columns(browseTable, countryPanel));

        var hint = (!flaggedMode && stats?.OrphanAltNames > 0)
            ? "  [grey]↑↓ move   PgUp/PgDn page   Enter view   [/][bold]O[/][grey] fix orphans   Esc back[/]"
            : "  [grey]↑↓ move   PgUp/PgDn page   Enter view   Esc back[/]";
        CdtCommandMenu.Render(hint);
    }

    /// <summary>
    /// Single-letter glyph for the flag-reason column: F=Flagged, C=CommonFake, O=Obsolete,
    /// blank for Valid. Lets the Flagged browse page distinguish a generic flag from a
    /// CommonFake / Obsolete one at a glance.
    /// </summary>
    private static string FlagReasonGlyph(DataFlagReasonType reason) => reason switch
    {
        DataFlagReasonType.Flagged    => "[red]F[/]",
        DataFlagReasonType.CommonFake => "[red]C[/]",
        DataFlagReasonType.Obsolete   => "[red]O[/]",
        _                             => " ",
    };

    private static Table BuildBrowseTable(
        List<BrowseRow> page,
        int selectedIndex,
        int offset,
        int totalCount,
        CurationStats? stats,
        bool flaggedMode = false)
    {
        var totalPages  = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)PageSize);
        var currentPage = offset / PageSize + 1;
        var orphanNote  = (!flaggedMode && stats?.OrphanAltNames > 0)
            ? $"  [yellow]orphans: {stats.OrphanAltNames:N0}[/]"
            : "";
        var caption = flaggedMode
            ? $"[grey]{totalCount:N0} flagged — page {currentPage}/{totalPages}[/]"
            : $"[grey]{totalCount:N0} uncurated — page {currentPage}/{totalPages}[/]{orphanNote}";

        var table = new Table()
            .Border(TableBorder.Square)
            .Caption(caption)
            .AddColumn(new TableColumn("").Padding(0, 0, 0, 0))
            .AddColumn(new TableColumn("[grey]Code[/]"))
            .AddColumn(new TableColumn("[grey]D[/]").Centered())
            .AddColumn(new TableColumn("[grey]TZ[/]").Centered())
            .AddColumn(new TableColumn("[grey]Nm[/]").Centered())
            .AddColumn(new TableColumn("[grey]⚑[/]").Centered())
            .AddColumn(new TableColumn("[grey]Place Name[/]"))
            .AddColumn(new TableColumn("[grey]Timezone[/]"))
            .AddColumn(new TableColumn("[grey]Lat[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Lng[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]AltNameOf[/]"))
            .AddColumn(new TableColumn("[grey]Admin[/]"));

        for (var i = 0; i < page.Count; i++)
        {
            var r   = page[i];
            var sel = i == selectedIndex;
            var ind = sel ? "[bold green]❯[/]" : " ";

            var def = r.IsDefault       ? "[cyan]✓[/]"  : "[grey]–[/]";
            var tz  = r.TimezoneChecked ? "[green]✓[/]" : "[red]✗[/]";
            var nm  = r.NameChecked     ? "[green]✓[/]" : "[red]✗[/]";
            var flg = FlagReasonGlyph(r.Flagged);

            var code = r.Flagged != DataFlagReasonType.Valid
                ? $"[red]{Markup.Escape(r.ZpCode)}[/]"
                : Markup.Escape(r.ZpCode);
            var name = Markup.Escape(r.PlaceName.Length > 20 ? r.PlaceName[..18] + "…" : r.PlaceName);
            var zone = Markup.Escape(r.Timezone.Length  > 18 ? r.Timezone[..16]  + "…" : r.Timezone);
            var lat  = r.Lat == "---" ? "[grey]—[/]" : Markup.Escape(r.Lat);
            var lng  = r.Lng == "---" ? "[grey]—[/]" : Markup.Escape(r.Lng);
            var alt  = r.AltNameOf is null
                ? "[grey]—[/]"
                : Markup.Escape(r.AltNameOf.Length > 14 ? r.AltNameOf[..12] + "…" : r.AltNameOf);
            var adm  = r.Admin1Code == "---"
                ? "[grey]—[/]"
                : Markup.Escape(r.Admin1Code.Length > 6 ? r.Admin1Code[..6] : r.Admin1Code);

            if (sel)
                table.AddRow(ind, $"[bold]{code}[/]", def, tz, nm, flg,
                    $"[bold]{name}[/]", zone, lat, lng, alt, adm);
            else
                table.AddRow(ind, code, def, tz, nm, flg, name, zone, lat, lng, alt, adm);
        }

        return table;
    }

    private static Table BuildDetailTable(List<DetailRow> rows, int selectedIndex)
    {
        var isGold = rows.Any(r => r.IsGold);
        var table = new Table()
            .Border(TableBorder.Square)
            .AddColumn(new TableColumn("").Padding(0, 0, 0, 0))
            .AddColumn(new TableColumn("[grey]★[/]").Centered())
            .AddColumn(new TableColumn("[grey]D[/]").Centered())
            .AddColumn(new TableColumn("[grey]Code[/]"))
            .AddColumn(new TableColumn("[grey]Place Name[/]"))
            .AddColumn(new TableColumn("[grey]TZ[/]").Centered())
            .AddColumn(new TableColumn("[grey]Nm[/]").Centered())
            .AddColumn(new TableColumn("[grey]⚑[/]").Centered())
            .AddColumn(new TableColumn("[grey]Timezone[/]"))
            .AddColumn(new TableColumn("[grey]Lat[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Lng[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]AltNameOf[/]"))
            .AddColumn(new TableColumn("[grey]Admin[/]"));

        if (isGold) table.Title("[bold yellow]⭐ Gold[/]");

        for (var i = 0; i < rows.Count; i++)
        {
            var r   = rows[i];
            var sel = i == selectedIndex;
            var ind = sel ? "[bold green]❯[/]" : " ";

            var gold = r.IsGold         ? "[bold yellow]★[/]" : " ";
            var def  = r.IsDefault       ? "[cyan]✓[/]"       : "[grey]–[/]";
            var tz   = r.TimezoneChecked ? "[green]✓[/]"      : "[red]✗[/]";
            var nm   = r.NameChecked     ? "[green]✓[/]"      : "[red]✗[/]";
            var flg  = FlagReasonGlyph(r.Flagged);

            var code = r.Flagged != DataFlagReasonType.Valid
                ? $"[red]{Markup.Escape(r.ZpCode)}[/]"
                : Markup.Escape(r.ZpCode);
            var name = Markup.Escape(r.PlaceName.Length > 22 ? r.PlaceName[..20] + "…" : r.PlaceName);
            var zone = Markup.Escape(r.Timezone.Length  > 18 ? r.Timezone[..16]  + "…" : r.Timezone);
            var lat  = r.Lat == "---" ? "[grey]—[/]" : Markup.Escape(r.Lat);
            var lng  = r.Lng == "---" ? "[grey]—[/]" : Markup.Escape(r.Lng);
            var alt  = r.AltNameOf is null
                ? "[grey]—[/]"
                : Markup.Escape(r.AltNameOf.Length > 16 ? r.AltNameOf[..14] + "…" : r.AltNameOf);

            var adminParts = new List<string>();
            if (r.Admin1Code != "---") adminParts.Add(r.Admin1Code);
            if (r.Admin1     != "---") adminParts.Add(r.Admin1);
            var adm = adminParts.Count > 0
                ? Markup.Escape(string.Join(" ", adminParts))
                : "[grey]—[/]";

            if (sel)
                table.AddRow(ind, gold, def, $"[bold]{code}[/]", $"[bold]{name}[/]",
                    tz, nm, flg, zone, lat, lng, alt, adm);
            else
                table.AddRow(ind, gold, def, code, name, tz, nm, flg, zone, lat, lng, alt, adm);
        }

        return table;
    }

    private static Table BuildEditTable(DetailRow row, int selectedField)
    {
        var table = new Table()
            .Border(TableBorder.Square)
            .HideHeaders()
            .AddColumn(new TableColumn("").Width(2).Padding(0, 0, 0, 0))
            .AddColumn(new TableColumn("").Width(14))
            .AddColumn(new TableColumn(""));

        for (var i = 0; i < EditableFields.Length; i++)
        {
            var f   = EditableFields[i];
            var sel = i == selectedField;
            var ind = sel ? "[bold green]❯[/]" : " ";
            var val = Markup.Escape(GetFieldValue(row, f));

            // Code rename is destructive — highlight in yellow when focused.
            if (f == "Code")
            {
                table.AddRow(
                    ind,
                    sel ? $"[bold yellow]{f}[/]" : $"[grey]{f}[/]",
                    sel ? $"[bold yellow]{val}[/]" : $"[grey]{val}[/]");
            }
            else
            {
                table.AddRow(
                    ind,
                    sel ? $"[bold cyan]{f}[/]" : $"[grey]{f}[/]",
                    sel ? $"[bold white]{val}[/]" : val);
            }
        }

        return table;
    }

    private static Table BuildCurrentCountryStatsTable(CurationStats? stats, string country)
    {
        var table = new Table()
            .Title($"[bold cyan]{country}[/]")
            .HideHeaders()
            .AddColumn(new TableColumn("").LeftAligned())
            .AddColumn(new TableColumn("").RightAligned());

        if (stats is null)
        {
            table.AddRow("[grey]No data[/]", "");
            return table;
        }

        table.AddRow("[grey]TZ ✓[/]",
            $"{stats.TotalTimezoneChecked:N0}");
        table.AddRow("[grey]Nm ✓[/]",
            $"{stats.TotalNameChecked:N0}");
        table.AddRow("[grey]Curated[/]",
            stats.TotalCurated < stats.Total
                ? $"[yellow]{stats.TotalCurated:N0}[/]"
                : $"[green]{stats.TotalCurated:N0}[/]");
        table.AddRow("[grey]Total[/]",
            $"{stats.Total:N0}");
        table.AddRow("[grey]Remaining[/]",
            stats.Remaining > 0 ? $"[bold yellow]{stats.Remaining:N0}[/]" : "[green]0[/]");
        table.AddRow("[grey]Progress[/]",
            $"[bold]{stats.PctComplete:F2}%[/]");

        if (stats.OrphanAltNames > 0)
            table.AddRow("[grey]Orphans[/]", $"[yellow]{stats.OrphanAltNames:N0}[/]");

        if (stats.GoldCount > 0)
            table.AddRow("[grey]Gold ★[/]", $"[bold yellow]{stats.GoldCount:N0}[/]");

        return table;
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private static async Task<(List<BrowseRow> page, int totalCount, CurationStats? stats)>
        LoadBrowsePageAsync(IWorkDbConnectionFactory factory, string country, int offset, bool flaggedMode = false)
    {
        try
        {
            using var conn = factory.CreateConnection();

            var stats = await conn.QueryFirstOrDefaultAsync<CurationStats>(
                CommonQueries.GetCurationStats,
                new { CountryId = country });

            var totalCount = await conn.ExecuteScalarAsync<int>(
                flaggedMode ? CommonQueries.GetFlaggedBrowseRowCount : CommonQueries.GetBrowseRowCount,
                new { CountryId = country });

            var orphanCount = flaggedMode ? 0 : await conn.ExecuteScalarAsync<int>(
                CommonQueries.GetOrphanAltNameCount,
                new { CountryId = country });

            var goldCount = 0;
            try
            {
                goldCount = await conn.ExecuteScalarAsync<int>(
                    CommonQueries.GetGoldCodeCount,
                    new { CountryId = country });
            }
            catch
            {
                // data.GoldCode not yet migrated — run MigrateAddGoldCodeTable first
            }

            var page = (await conn.QueryAsync<BrowseRow>(
                flaggedMode ? CommonQueries.GetFlaggedBrowseRowsPage : CommonQueries.GetBrowseRowsPage,
                new { CountryId = country, Offset = offset, PageSize })).ToList();

            if (stats is not null)
            {
                stats.OrphanAltNames = orphanCount;
                stats.GoldCount      = goldCount;
            }

            return (page, totalCount, stats);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]  ✗ Load failed: {Markup.Escape(ex.Message)}[/]");
            return ([], 0, null);
        }
    }

    // ── Candidate mode ────────────────────────────────────────────────────────

    private static async Task BrowseCandidatesAsync(string country)
    {
        WorkDbContext db;
        try { db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory()); }
        catch (Exception ex)
        {
            HeaderBar.Render($"ZpCode Editor › {country} › Candidate");
            AnsiConsole.MarkupLine($"[red]  ✗ {Markup.Escape(ex.Message)}[/]");
            AnsiConsole.WriteLine();
            FooterBar.PressAnyKey();
            Console.ReadKey(intercept: true);
            return;
        }

        var factory = db.GetFactory();

        while (true)
        {
            List<CandidateStatusCount> counts;
            try
            {
                using var conn = factory.CreateConnection();
                counts = (await conn.QueryAsync<CandidateStatusCount>(
                    CommonQueries.GetCandidateStatusSummary,
                    new { CountryId = country })).ToList();
            }
            catch (Exception ex)
            {
                HeaderBar.Render($"ZpCode Editor › {country} › Candidate");
                AnsiConsole.MarkupLine($"[red]  ✗ Load failed: {Markup.Escape(ex.Message)}[/]");
                AnsiConsole.WriteLine();
                FooterBar.PressAnyKey();
                Console.ReadKey(intercept: true);
                return;
            }

            HeaderBar.Render($"ZpCode Editor › {country} › Candidate");
            AnsiConsole.WriteLine();

            if (counts.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]  No candidates for this country.[/]");
                AnsiConsole.WriteLine();
                FooterBar.PressAnyKey();
                Console.ReadKey(intercept: true);
                return;
            }

            var statusChoices = counts.Select(c => c.Status).Append("← Back").ToList();
            var selected = CdtSelectMenu.Show(
                statusChoices,
                s =>
                {
                    if (s == "← Back") return "[grey]← Back[/]";
                    var c = counts.First(x => x.Status == s);
                    return $"{CandidateStatusMarkup(s),-35}  [grey]{c.Count:N0}[/]";
                },
                escapeReturns: "← Back",
                title: "Browse by status:");

            if (selected == "← Back") return;

            await BrowseCandidateStatusAsync(factory, country, selected);
        }
    }

    private static async Task BrowseCandidateStatusAsync(
        IWorkDbConnectionFactory factory, string country, string status)
    {
        var header        = $"ZpCode Editor › {country} › Candidate › {status}";
        await PaginatedBrowser.RunAsync<CandidateBrowseRow>(
            PageSize,
            loadPage: off => LoadCandidatePageAsync(factory, country, status, off),
            render: (p, sel, off, total) =>
            {
                HeaderBar.Render(header);
                AnsiConsole.Write(BuildCandidateBrowseTable(p, sel, off, total, status));
                CdtCommandMenu.Render("  [grey]↑↓ move   PgUp/PgDn page   Enter view   Esc back[/]");
            },
            onEnter: row => ViewCandidateCodeAsync(factory, country, row.ZpCode),
            onEmpty: () =>
            {
                HeaderBar.Render(header);
                AnsiConsole.MarkupLine("[green]  ✓ No codes with this status.[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]  Press any key to return...[/]");
                Console.ReadKey(intercept: true);
                return Task.FromResult(true);
            });
    }

    private static async Task ViewCandidateCodeAsync(
        IWorkDbConnectionFactory factory, string country, string zpCode)
    {
        var header = $"ZpCode Editor › {country} › Candidate › {zpCode}";

        while (true)
        {
            List<CandidateBrowseRow> candidateRows;
            List<BrowseRow>          refRows;

            try
            {
                using var conn = factory.CreateConnection();

                candidateRows = (await conn.QueryAsync<CandidateBrowseRow>(
                    CommonQueries.GetCandidateRowsByCode,
                    new { CountryId = country, ZpCode = zpCode })).ToList();

                refRows = (await conn.QueryAsync<BrowseRow>(
                    CommonQueries.GetReferenceBrowseRowsByCode,
                    new { CountryId = country, ZpCode = zpCode })).ToList();
            }
            catch (Exception ex)
            {
                HeaderBar.Render(header);
                AnsiConsole.MarkupLine($"[red]  ✗ {Markup.Escape(ex.Message)}[/]");
                AnsiConsole.WriteLine();
                FooterBar.PressAnyKey();
                Console.ReadKey(intercept: true);
                return;
            }

            HeaderBar.Render(header);

            var candidateTable = new Table()
                .Border(TableBorder.Square)
                .Caption("[grey]Candidate rows[/]")
                .AddColumn(new TableColumn("[grey]Status[/]"))
                .AddColumn(new TableColumn("[grey]Place Name[/]"))
                .AddColumn(new TableColumn("[grey]Admin[/]"))
                .AddColumn(new TableColumn("[grey]Timezone[/]"));

            foreach (var r in candidateRows)
            {
                candidateTable.AddRow(
                    CandidateStatusMarkup(r.Status),
                    Markup.Escape(r.PlaceName),
                    Markup.Escape(r.Admin1Code),
                    Markup.Escape(r.Timezone));
            }

            AnsiConsole.Write(candidateTable);
            AnsiConsole.WriteLine();

            if (refRows.Count > 0)
            {
                var refTable = new Table()
                    .Border(TableBorder.Square)
                    .Caption("[grey]Reference rows[/]")
                    .AddColumn(new TableColumn("[grey]D[/]").Centered())
                    .AddColumn(new TableColumn("[grey]Place Name[/]"))
                    .AddColumn(new TableColumn("[grey]Admin[/]"))
                    .AddColumn(new TableColumn("[grey]Timezone[/]"));

                foreach (var r in refRows)
                {
                    refTable.AddRow(
                        r.IsDefault ? "[cyan]✓[/]" : "[grey]–[/]",
                        Markup.Escape(r.PlaceName),
                        Markup.Escape(r.Admin1Code),
                        Markup.Escape(r.Timezone));
                }

                AnsiConsole.Write(refTable);
                AnsiConsole.WriteLine();
            }
            else
            {
                AnsiConsole.MarkupLine("  [grey](No reference rows for this code)[/]");
                AnsiConsole.WriteLine();
            }

            var isRejected = candidateRows.Count > 0 && candidateRows.All(r => r.Status == "Rejected");
            var hint = isRejected
                ? "  [bold]U[/][grey] un-reject   Esc back[/]"
                : "  [bold]R[/][grey] reject all   Esc back[/]";
            CdtCommandMenu.Render(hint);

            var key = Console.ReadKey(intercept: true).Key;

            switch (key)
            {
                case ConsoleKey.R when !isRejected:
                    try
                    {
                        await ExecAsync(factory, CommonQueries.RejectCandidateZpCode,
                            new { CountryId = country, ZpCode = zpCode });
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]  ✗ {Markup.Escape(ex.Message)}[/]");
                        AnsiConsole.MarkupLine("[grey]  Press any key...[/]");
                        Console.ReadKey(intercept: true);
                    }
                    return;

                case ConsoleKey.U when isRejected:
                    try
                    {
                        await ExecAsync(factory,
                            CommonQueries.SetCandidatePendingByZpCode,
                            new { CountryId = country, ZpCode = zpCode });
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]  ✗ {Markup.Escape(ex.Message)}[/]");
                        AnsiConsole.MarkupLine("[grey]  Press any key...[/]");
                        Console.ReadKey(intercept: true);
                    }
                    return;

                case ConsoleKey.Escape:
                    return;
            }
        }
    }

    // ── Candidate helpers ─────────────────────────────────────────────────────

    private static async Task<(List<CandidateBrowseRow> page, int totalCount)> LoadCandidatePageAsync(
        IWorkDbConnectionFactory factory, string country, string status, int offset)
    {
        try
        {
            using var conn = factory.CreateConnection();
            var total = await conn.ExecuteScalarAsync<int>(
                CommonQueries.GetCandidatesBrowseCount,
                new { CountryId = country, Status = status });
            var page = (await conn.QueryAsync<CandidateBrowseRow>(
                CommonQueries.GetCandidatesBrowsePage,
                new { CountryId = country, Status = status, Offset = offset, PageSize })).ToList();
            return (page, total);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]  ✗ Load failed: {Markup.Escape(ex.Message)}[/]");
            return ([], 0);
        }
    }

    private static Table BuildCandidateBrowseTable(
        List<CandidateBrowseRow> page, int selectedIndex, int offset, int totalCount, string status)
    {
        var totalPages  = (int)Math.Ceiling(totalCount / (double)PageSize);
        var currentPage = offset / PageSize + 1;

        var table = new Table()
            .Border(TableBorder.Square)
            .Caption($"[grey]{totalCount:N0} {status} — page {currentPage}/{totalPages}[/]")
            .AddColumn(new TableColumn("").Padding(0, 0, 0, 0))
            .AddColumn(new TableColumn("[grey]Code[/]"))
            .AddColumn(new TableColumn("[grey]Place Name[/]"))
            .AddColumn(new TableColumn("[grey]Admin[/]"))
            .AddColumn(new TableColumn("[grey]Timezone[/]"));

        for (var i = 0; i < page.Count; i++)
        {
            var r   = page[i];
            var sel = i == selectedIndex;
            var ind  = sel ? "[bold green]❯[/]" : " ";
            var code = sel ? $"[bold]{Markup.Escape(r.ZpCode)}[/]" : Markup.Escape(r.ZpCode);
            var name = r.PlaceName.Length > 40 ? r.PlaceName[..37] + "..." : r.PlaceName;

            table.AddRow(ind, code, Markup.Escape(name), Markup.Escape(r.Admin1Code), Markup.Escape(r.Timezone));
        }

        return table;
    }

    private static string CandidateStatusMarkup(string status) => status switch
    {
        "Discrepancy" => "[bold yellow]Discrepancy[/]",
        "Unfound"     => "[bold red]Unfound[/]",
        "Pending"     => "[grey]Pending[/]",
        "Error"       => "[bold red]Error[/]",
        "Rejected"    => "[grey]Rejected[/]",
        "Clean"       => "[green]Clean[/]",
        _             => Markup.Escape(status),
    };
}
