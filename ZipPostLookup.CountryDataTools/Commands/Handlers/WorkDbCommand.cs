using Dapper;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Utilities;
using ZipPostLookup.CountryDataTools.Validation;
using ZipPostLookup.CountryDataTools.Validation.Export;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools workdb &lt;subcommand&gt;
///
/// Manages the per-folder working database configuration.
///
/// SUBCOMMANDS
///
///   init --country XX --connection "..." [--provider sqlserver]
///       Creates workdb.json in the current directory.
///       Tests the connection and verifies the schema exists.
///       The connection string is never stored in source control —
///       workdb.json should be in .gitignore.
///
///   status
///       Shows the current workdb.json config and DB connection status.
///       Prints run history for the active country.
///
///   newrun --source &lt;filename&gt;
///       Creates a new run in pipeline.runs and updates activeRunId
///       in workdb.json. Useful when re-running a scan against a
///       revised candidate file without losing previous run data.
///
///   test
///       Tests the connection and schema reachability only.
///       Exit code 0 = OK, 1 = failed.
/// </summary>
public static class WorkDbCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args.Any(a => a is "-h" or "--help"))
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "init" => await RunInitAsync(args[1..]),
            "status" => await RunStatusAsync(args[1..]),
            "newrun" => await RunNewRunAsync(args[1..]),
            "test" => await RunTestAsync(args[1..]),
            "clear" => await RunClearAsync(args[1..]),
            "reset" => await RunResetAsync(args[1..]),
            "normalize-tz"    => await RunNormalizeTzAsync(),
            "normalize-names" => await RunNormalizeNamesAsync(args[1..]),
            _ => UnknownSubcommand(args[0]),
        };
    }

    // -------------------------------------------------------------------------
    // workdb init
    // -------------------------------------------------------------------------

    private static async Task<int> RunInitAsync(string[] args)
    {
        string country = "";
        string connection = "";
        string provider = "sqlserver";

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--country": country = args[++i]; break;
                case "--connection": connection = args[++i]; break;
                case "--provider": provider = args[++i]; break;
            }
        }

        if (string.IsNullOrWhiteSpace(country) || string.IsNullOrWhiteSpace(connection))
        {
            await Console.Error.WriteLineAsync(
                "Usage: countrydatatools workdb init --country US --connection \"Server=...\"");
            return 2;
        }

        var configPath = Path.Combine(Directory.GetCurrentDirectory(), WorkDbConfig.FileName);

        if (File.Exists(configPath))
        {
            if (!AnsiConsole.Confirm($"{configPath} already exists. Overwrite?"))
            {
                Console.WriteLine("  Aborted.");
                return 0;
            }
        }

        // Test the connection before writing the config
        Console.WriteLine("  Testing connection…");
        var testConfig = new WorkDbConfig
        {
            Provider = provider,
            ConnectionString = connection,
            CountryCode = country.ToUpperInvariant(),
            ActiveRunId = "",
        };

        var factory = WorkDbConnectionFactory.Create(testConfig);
        var (ok, error) = await factory.TestConnectionAsync();

        if (!ok)
        {
            await Console.Error.WriteLineAsync($"  ✗ Connection failed: {error}");
            await Console.Error.WriteLineAsync(
                "  Check that SQL Server is running and the schema script has been executed.");
            return 1;
        }

        Console.WriteLine("  ✓ Connection successful — schema verified.");

        // Write the config
        testConfig.Save(configPath);
        Console.WriteLine($"  workdb.json written to: {configPath}");
        Console.WriteLine();
        Console.WriteLine("  Add workdb.json to .gitignore to keep your connection string out of source control.");
        Console.WriteLine("  Next: countrydatatools fullscan <candidate.csv>");

        return 0;
    }

    // -------------------------------------------------------------------------
    // workdb status
    // -------------------------------------------------------------------------

    private static async Task<int> RunStatusAsync(string[] _)
    {
        var configPath = WorkDbConfig.FindConfigFile(Directory.GetCurrentDirectory());
        if (configPath == null)
        {
            await Console.Error.WriteLineAsync(
                $"No {WorkDbConfig.FileName} found. Run 'workdb init' first.");
            return 1;
        }

        Console.WriteLine($"Config : {configPath}");

        WorkDbConfig config;
        try
        {
            config = WorkDbConfig.Load(configPath);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"  ✗ Failed to load config: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Country  : {config.CountryCode}");
        Console.WriteLine($"Provider : {config.Provider}");
        Console.WriteLine($"Run ID   : {(string.IsNullOrEmpty(config.ActiveRunId) ? "(none)" : config.ActiveRunId)}");

        var factory = WorkDbConnectionFactory.Create(config);
        var (ok, error) = await factory.TestConnectionAsync();

        if (!ok)
        {
            AnsiConsole.MarkupLine($"  [red]✗ DB connection failed: {Markup.Escape(error ?? string.Empty)}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("  [green]✓ Connection OK[/]");

        // Show recent runs
        var db = WorkDbContext.FromConfig(config);
        var runs = await db.Runs.GetRunsAsync(config.CountryCode);

        if (runs.Count == 0)
        {
            Console.WriteLine("  No runs yet.");
        }
        else
        {
            Console.WriteLine($"\n  Recent runs for {config.CountryCode}:");
            Console.WriteLine($"  {"Run ID",-26} {"Status",-12} {"Source",-30} Started");
            Console.WriteLine($"  {new string('-', 85)}");

            foreach (var run in runs.Take(10))
            {
                var marker = run.RunId == config.ActiveRunId ? "▶ " : "  ";
                Console.WriteLine(
                    $"  {marker}{run.RunId,-24} {run.Status,-12} " +
                    $"{TruncatePath(run.SourceFilename, 28),-30} " +
                    $"{run.StartedAt:yyyy-MM-dd HH:mm}");
            }
        }

        return 0;
    }

    // -------------------------------------------------------------------------
    // workdb newrun
    // -------------------------------------------------------------------------

    private static async Task<int> RunNewRunAsync(string[] args)
    {
        string source = "";
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--source", StringComparison.OrdinalIgnoreCase))
                source = args[++i];

        if (string.IsNullOrWhiteSpace(source))
        {
            await Console.Error.WriteLineAsync(
                "Usage: countrydatatools workdb newrun --source <candidate.csv>");
            return 2;
        }

        var configPath = WorkDbConfig.FindConfigFile(Directory.GetCurrentDirectory())
            ?? throw new FileNotFoundException("No workdb.json found. Run 'workdb init' first.");

        var config = WorkDbConfig.Load(configPath);
        var db = WorkDbContext.FromConfig(config);

        var runId = await db.Runs.CreateRunAsync(config.CountryCode, source);
        Console.WriteLine($"  New run created: {runId}");

        // Update the activeRunId in workdb.json
        var updated = new WorkDbConfig
        {
            Provider = config.Provider,
            ConnectionString = config.ConnectionString,
            CountryCode = config.CountryCode,
            ActiveRunId = runId,
        };
        updated.Save(configPath);
        Console.WriteLine($"  workdb.json updated — activeRunId: {runId}");

        return 0;
    }

    // -------------------------------------------------------------------------
    // workdb test
    // -------------------------------------------------------------------------

    private static async Task<int> RunTestAsync(string[] _)
    {
        var configPath = WorkDbConfig.FindConfigFile(Directory.GetCurrentDirectory());
        if (configPath == null)
        {
            await Console.Error.WriteLineAsync(
                $"No {WorkDbConfig.FileName} found. Run 'workdb init' first.");
            return 1;
        }

        var config = WorkDbConfig.Load(configPath);
        var factory = WorkDbConnectionFactory.Create(config);
        var (ok, error) = await factory.TestConnectionAsync();

        if (ok)
        {
            AnsiConsole.MarkupLine("[green]✓ Connection OK — schema verified.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]✗ Connection failed: {Markup.Escape(error ?? string.Empty)}[/]");
        return 1;
    }

    // -------------------------------------------------------------------------
    // db normalize-tz
    // -------------------------------------------------------------------------

    private static async Task<int> RunNormalizeTzAsync()
    {
        var db = await LoadDbAsync();
        if (db == null) return 1;

        using var conn = db.GetFactory().CreateConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            Console.WriteLine("  Normalising deprecated IANA timezone aliases...");
            var updated = await Dapper.SqlMapper.ExecuteAsync(conn,
                CommonQueries.NormalizeDeprecatedTimezones, transaction: tx);
            AnsiConsole.MarkupLine($"  [green]✓ {updated} row(s) updated to canonical timezone.[/]");

            Console.WriteLine("  Collecting duplicate ReferenceIds (IsDefault=0 now matching an IsDefault=1 row)...");
            Console.WriteLine("  Deleting ReferenceAdmin rows for duplicates...");
            var adminsDeleted = await Dapper.SqlMapper.ExecuteAsync(conn,
                CommonQueries.DeleteDeprecatedAliasDuplicateAdmins, transaction: tx);
            AnsiConsole.MarkupLine($"  [green]✓ {adminsDeleted} ReferenceAdmin row(s) deleted.[/]");

            Console.WriteLine("  Deleting duplicate Reference rows...");
            var refsDeleted = await Dapper.SqlMapper.ExecuteAsync(conn,
                CommonQueries.DeleteDeprecatedAliasDuplicates, transaction: tx);
            AnsiConsole.MarkupLine($"  [green]✓ {refsDeleted} Reference row(s) deleted.[/]");

            Console.WriteLine("  Normalising admin level 1 name variants...");
            var ccUpper = db.CountryCode.ToUpperInvariant();
            var variants = (await conn.QueryAsync<AdminNameVariant>(
                CommonQueries.DetectAdminNameVariants,
                new { CountryId = ccUpper }, transaction: tx)).ToList();

            if (variants.Count > 0)
            {
                foreach (var v in variants)
                    Console.WriteLine($"    {v.Code}: '{v.MinorityValue}' ({v.MinorityCnt}) → '{v.DominantValue}'");
                var adminFixed = await Dapper.SqlMapper.ExecuteAsync(conn,
                    CommonQueries.NormalizeAdminNames,
                    new { CountryId = ccUpper }, transaction: tx);
                AnsiConsole.MarkupLine($"  [green]✓ {adminFixed} admin name variant(s) normalised.[/]");
            }
            else
            {
                Console.WriteLine("  ✓ Admin names consistent — no variants found.");
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        // ── Resolve timezones from coordinates (all pipeline countries) ──────
        // Runs outside the main transaction — operates on potentially large row
        // sets and each update is independently safe to retry.
        // Covers both TimezoneChecked=0 AND rows where TimezoneChecked=1 but
        // Timezone is empty/--- (data quality fix).
        Console.WriteLine("  Resolving IANA timezones from coordinates for unverified rows...");
        var unverified = new List<(long ReferenceId, string ZpCode, string Lat, string Lng, string Timezone)>();
        foreach (var pipelineCountry in new[] { "US", "CA", "MX" })
        {
            var countryRows = await conn.QueryAsync<(long ReferenceId, string ZpCode, string Lat, string Lng, string Timezone)>(
                CommonQueries.GetUnverifiedWithCoords,
                new { CountryId = pipelineCountry });
            unverified.AddRange(countryRows);
        }

        if (unverified.Count > 0)
        {
            Console.WriteLine($"    Found {unverified.Count:N0} row(s) with coordinates and TimezoneChecked=0.");
            var tzResolved = 0;
            var tzConfirmed = 0;

            foreach (var row in unverified)
            {
                var resolved = TimezoneResolver.TryResolveWithCoordinates(row.Lat, row.Lng);
                if (resolved == null) continue;

                var tzMissing = string.IsNullOrEmpty(row.Timezone) || row.Timezone == "---";
                var tzDiffers = !string.Equals(resolved, row.Timezone, StringComparison.OrdinalIgnoreCase);

                if (tzMissing || tzDiffers)
                {
                    // Timezone missing or coords give a different value — update it
                    await conn.ExecuteAsync(
                        @"UPDATE data.Reference
                          SET Timezone = @Timezone, TimezoneChecked = 1, UpdatedAt = SYSUTCDATETIME()
                          WHERE ReferenceId = @ReferenceId",
                        new { Timezone = resolved, row.ReferenceId });
                    tzResolved++;
                }
                else
                {
                    // Timezone already correct — just ensure TimezoneChecked = 1
                    await conn.ExecuteAsync(
                        @"UPDATE data.Reference
                          SET TimezoneChecked = 1, UpdatedAt = SYSUTCDATETIME()
                          WHERE ReferenceId = @ReferenceId AND TimezoneChecked = 0",
                        new { row.ReferenceId });
                    tzConfirmed++;
                }
            }

            if (tzResolved > 0)
                AnsiConsole.MarkupLine($"  [green]✓ {tzResolved:N0} timezone(s) updated from coordinates.[/]");
            if (tzConfirmed > 0)
                AnsiConsole.MarkupLine($"  [green]✓ {tzConfirmed:N0} timezone(s) confirmed correct and marked verified.[/]");
            if (tzResolved == 0 && tzConfirmed == 0)
                Console.WriteLine("  ✓ Coordinates present but no timezone could be resolved.");
        }
        else
        {
            Console.WriteLine("  ✓ No unverified rows with coordinates — nothing to resolve.");
        }

        Console.WriteLine();
        Console.WriteLine("  Re-export and rebuild ZPI for affected countries after this.");
        return 0;
    }

    // -------------------------------------------------------------------------
    // db normalize-names
    // -------------------------------------------------------------------------

    private static async Task<int> RunNormalizeNamesAsync(string[] args)
    {
        var all     = args.Any(a => a.Equals("--all", StringComparison.OrdinalIgnoreCase));
        var country = ParseCountry(args);

        if (!all && string.IsNullOrWhiteSpace(country))
        {
            await Console.Error.WriteLineAsync(
                "Usage: countrydatatools db normalize-names --country XX\n" +
                "    or countrydatatools db normalize-names --all");
            return 2;
        }

        var db = await LoadDbAsync();
        if (db == null) return 1;

        var countries = all
            ? new[] { "US", "CA", "MX" }
            : new[] { country.ToUpperInvariant() };

        var exitCode = 0;
        var check    = new NormalizePlaceNamesCheck();

        foreach (var cc in countries)
        {
            if (all)
            {
                Console.WriteLine();
                AnsiConsole.Write(new Rule($"[bold]{cc}[/]").LeftJustified());
            }

            using var conn = db.GetFactory().CreateConnection();

            Console.WriteLine($"  Scanning {cc} for place-name abbreviation alternates...");
            int linked;
            try
            {
                linked = await check.RunAsync(conn, cc, db.RepoRoot);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"  ✗ {cc} failed: {ex.Message}");
                exitCode = 1;
                continue;
            }

            if (linked == 0)
                Console.WriteLine("  ✓ No new alternates found — AltNameOf is up to date.");
            else
                AnsiConsole.MarkupLine($"  [green]✓ {linked} alternate link(s) set.[/]");
        }

        Console.WriteLine();
        Console.WriteLine("  Re-export --target ref for affected countries to include links in the CSV.");
        return exitCode;
    }

    // -------------------------------------------------------------------------

    private static int UnknownSubcommand(string name)
    {
        Console.Error.WriteLine($"Unknown workdb subcommand: {name}");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage() =>
        Console.WriteLine("""
            countrydatatools workdb <subcommand>

            SUBCOMMANDS
              init    --country XX --connection "Server=localhost,1433;..."
                          Create workdb.json in the current directory.
              status  Show config, connection status, and recent runs.
              newrun  --source <file>   Create a new run and update activeRunId.
              test    Verify the connection and schema only.
              clear   --country XX
                          Clear pipeline working data for a country
                          (codes.candidate, codes.discrepancies, pipeline.runs,
                          pipeline.decisions). Keeps data.reference intact.
                          Use when switching countries or starting a fresh import.
              reset   --country XX
                          Full wipe for a country — clears pipeline data AND
                          data.reference. Use to start completely from scratch.
              normalize-tz
                          1. Normalise deprecated IANA timezone aliases to canonical forms.
                          2. Delete resulting duplicate rows.
                          3. Normalise admin level 1 name variants to dominant spelling.
                          4. Resolve IANA timezone from Lat/Lng for rows where
                             TimezoneChecked=0 — sets correct timezone and marks verified.
                          Idempotent. Re-export affected countries after running.
              normalize-names --country XX
              normalize-names --all
                          Detect place-name abbreviation alternates (e.g. "St. Martin"
                          / "Saint Martin") for the same code and lat/lon, and set
                          AltNameOf on the abbreviated row to link it to the canonical
                          name. Safe to re-run; skips already-linked rows.
                          --all runs US, CA, MX in sequence.
            """);

    private static string TruncatePath(string path, int maxLen) =>
        path.Length <= maxLen ? path : "…" + path[^(maxLen - 1)..];


    // -------------------------------------------------------------------------
    // db clear
    // -------------------------------------------------------------------------

    private static async Task<int> RunClearAsync(string[] args)
    {
        var country = ParseCountry(args);
        if (string.IsNullOrWhiteSpace(country))
        {
            await Console.Error.WriteLineAsync("Usage: countrydatatools db clear --country XX");
            return 2;
        }

        var db = await LoadDbAsync();
        if (db == null) return 1;

        var cc = country.ToUpperInvariant();

        Console.WriteLine($"  This will delete all pipeline working data for {cc}:");
        Console.WriteLine($"    · pipeline.runs");
        Console.WriteLine($"    · pipeline.decisions");
        Console.WriteLine($"    · codes.candidate");
        Console.WriteLine($"    · codes.discrepancies");
        Console.WriteLine($"  data.reference and data.country_info are NOT affected.");
        Console.WriteLine();
        if (!AnsiConsole.Confirm("Proceed?"))
        {
            Console.WriteLine("  Aborted.");
            return 0;
        }

        using var conn = db.GetFactory().CreateConnection();

        await Dapper.SqlMapper.ExecuteAsync(conn,
            CommonQueries.ClearPipelineData,
            new { CountryId = cc });

        AnsiConsole.MarkupLine($"  [green]✓ Pipeline data cleared for {Markup.Escape(cc)}.[/]");
        Console.WriteLine("  Run 'ingest ref' to load reference data, then 'ingest candidate' to start fresh.");
        return 0;
    }

    // -------------------------------------------------------------------------
    // db reset
    // -------------------------------------------------------------------------

    private static async Task<int> RunResetAsync(string[] args)
    {
        var country = ParseCountry(args);
        if (string.IsNullOrWhiteSpace(country))
        {
            await Console.Error.WriteLineAsync("Usage: countrydatatools db reset --country XX");
            return 2;
        }

        var db = await LoadDbAsync();
        if (db == null) return 1;

        var cc = country.ToUpperInvariant();

        Console.WriteLine($"  ⚠ WARNING: This will delete ALL data for {cc} including:");
        Console.WriteLine($"    · data.reference  (the curated reference rows)");
        Console.WriteLine($"    · pipeline.runs, pipeline.decisions");
        Console.WriteLine($"    · codes.candidate, codes.discrepancies");
        Console.WriteLine();
        Console.WriteLine($"  data.country_info metadata (regex, notes) is kept.");
        Console.WriteLine();
        var confirm = AnsiConsole.Prompt(
            new TextPrompt<string>($"Type [bold]{Markup.Escape(cc)}[/] to confirm:"));
        if (confirm.Trim().ToUpperInvariant() != cc)
        {
            Console.WriteLine("  Aborted.");
            return 0;
        }

        using var conn = db.GetFactory().CreateConnection();

        await Dapper.SqlMapper.ExecuteAsync(conn,
            CommonQueries.ResetCountryData,
            new { CountryId = cc });

        AnsiConsole.MarkupLine($"  [green]✓ Full reset complete for {Markup.Escape(cc)}.[/]");
        Console.WriteLine("  Run 'ingest ref' to reload reference data and start fresh.");
        return 0;
    }

    // -------------------------------------------------------------------------
    // Helpers shared by clear / reset
    // -------------------------------------------------------------------------

    private static string ParseCountry(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--country", StringComparison.OrdinalIgnoreCase))
                return args[i + 1].ToUpperInvariant();
        }
        return "";
    }

    private static async Task<WorkDbContext?> LoadDbAsync()
    {
        var configPath = WorkDbConfig.FindConfigFile(Directory.GetCurrentDirectory());
        if (configPath == null)
        {
            await Console.Error.WriteLineAsync(
                $"No {WorkDbConfig.FileName} found. Run 'db init' first.");
            return null;
        }

        try
        {
            return await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"  ✗ DB connection failed: {ex.Message}");
            return null;
        }
    }


}