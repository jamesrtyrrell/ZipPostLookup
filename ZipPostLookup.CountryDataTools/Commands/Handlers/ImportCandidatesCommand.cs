using System.Diagnostics;
using Dapper;
using Microsoft.Data.SqlClient;
using Z.Dapper.Plus;
using ZipPostLookup.CountryDataTools.Database.Repositories;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.DSV;
using ZipPostLookup.CountryDataTools.Extensions;
using Spectre.Console;
using ZipPostLookup.CountryDataTools.Models.Commands;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Models.Enums;
using ZipPostLookup.CountryDataTools.Utilities;
using ZipPostLookup.CountryDataTools.Validation;
using ZipPostLookup.CountryDataTools.Commands.Display;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// CountryDataTools importcandidates &lt;file.csv&gt; --country US
///
/// Imports a candidate CSV against the verified reference data in the working
/// database, applying the following rules per row:
///
///   1. Title-case the candidate Name on the way in (BOSTON → Boston)
///
///   2. New code (not in [data].[reference] at all)
///      → INSERT into [data].[reference] with TimezoneChecked=1 if lat/lng present, else 0
///      → candidate row status = clean
///
///   3. Existing code+Name (case-insensitive match) — timezones match
///      → candidate status = clean
///      → if reference has no lat/lng AND candidate has coords, derive timezone from
///        coords; if it matches the reference timezone, back-fill lat/lng + TimezoneChecked=1
///
///   4. Existing code+Name — timezone differs AND TimezoneChecked = 0
///      → codes.discrepancies (timezone field), candidate status = discrepancy
///
///   5. Existing code+Name — timezone differs AND TimezoneChecked = 1
///      → pipeline.decisions (auto-rejected), candidate status = rejected
///      → reference timezone is trusted, candidate is overruled
///
///   6. Existing code — Name not found (case-insensitive) in reference
///      → codes.discrepancies (Name field), candidate status = discrepancy
/// </summary>
public static class ImportCandidatesCommand
{
    private const int ChunkSize = 5_000;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help")) { PrintUsage(); return 0; }

        if (!TryParseArgs(args, out var file, out var country))
        {
            PrintUsage();
            return 2;
        }

        if (!File.Exists(file))
        {
            await Console.Error.WriteLineAsync($"File not found: {file}");
            return 2;
        }

        if (!CommandArgs.ResolveCountry(file, country, out country)) { return 2; }

        Console.WriteLine($"Importing candidates: {file} [{country}]");

        // --- 1. Read and fix candidate rows ---
        var (rows, headerOk, _) = CsvReader.Read(file);
        if (!headerOk)
        {
            await Console.Error.WriteLineAsync("  ✗ Header errors — run 'validate' first.");
            return 1;
        }

        var (fixedRows, _) = Fixer.Fix(rows, country);
        Console.WriteLine($"  Rows read: {fixedRows.Count:N0}");

        // --- 2. Connect to working DB ---
        WorkDbContext db;
        try
        {
            // Walk up from the current working directory, not the candidate file's
            // directory — workdb.json lives in the repo root, not alongside the CSV.
            db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"  ✗ DB connection failed: {ex.Message}");
            return 1;
        }

        // --- 3. Create a run ---
        var runId = await db.Runs.CreateRunAsync(country, Path.GetFileName(file));
        Console.WriteLine($"  Run ID: {runId}");

        await using var conn = (SqlConnection)db.GetFactory().CreateConnection();

        // Resolve LevelNumber → AdminLevelId (DB identity) from codes.admin_levels.
        // CodesCandidate stores level numbers (1, 2, ...) not the identity PK.
        var adminLevelMap = (await conn.QueryAsync<DataAdminLevel>(
                CommonQueries.GetCandidateAdminLevels,
                new { CountryId = country.ToUpperInvariant() }))
            .ToDictionary(r => r.LevelNumber, r => r.AdminLevelId);

        // Build CodesCandidate objects — title-casing and normalisation applied by
        // Fixer.Fix and the CodesCandidate constructor. fixedRows is not referenced
        // again after this point.
        var codesCandidateList = new List<CodesCandidate>();
        foreach (var fixedRow in fixedRows)
        {
            var codesCandidate = new CodesCandidate(country, fixedRow) { RunId = runId };
            if (codesCandidate.RemapCandidatesList(adminLevelMap))
            {
                codesCandidateList.Add(codesCandidate);
            }
        }

        // --- 4. Bulk-insert all candidate rows ---
        var insertedCount = 0;

        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var insertTask = ctx.AddTask(
                    $"Inserting {codesCandidateList.Count:N0} candidates",
                    maxValue: codesCandidateList.Count);

                foreach (var candidateChunk in codesCandidateList.Chunk(ChunkSize))
                {
                    conn.BulkInsert(candidateChunk).AlsoBulkMerge(x => x.AdminCandidateList);
                    insertedCount += candidateChunk.Length;
                    insertTask.Increment(candidateChunk.Length);
                }

                await Task.CompletedTask;
            });

        // --- 5. Process each candidate against reference in chunks ---
        var counters            = new ImportCounters();
        var acc                 = new ChunkAccumulators();
        var pendingCoordUpdates = new List<DataReference>();
        var total               = codesCandidateList.Count;
        var stopwatch           = Stopwatch.StartNew();
        var countryRules        = CountryRulesFactory.For(country);

        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn(), new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var processTask = ctx.AddTask(
                    $"Processing {total:N0} rows",
                    maxValue: total);

                for (int chunkStart = 0; chunkStart < total; chunkStart += ChunkSize)
                {
                    var chunkEnd        = Math.Min(chunkStart + ChunkSize, total);
                    var chunkCandidates = codesCandidateList.GetRange(chunkStart, chunkEnd - chunkStart);
                    var chunkCodes      = chunkCandidates.Select(c => c.ZpCode).Distinct().ToArray();

                    var parameters = new DynamicParameters();
                    parameters.Add("Codes",     DataTools.ConvertArrayToCodeTableParameter(chunkCodes));
                    parameters.Add("CountryId", country.ToUpperInvariant());

                    var refLookup = (await conn.QueryAsync<ReferenceReadRow>(CommonQueries.GetReferenceForImportBatch, parameters))
                        .GroupBy(r => r.ZpCode, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                    ProcessChunkCandidates(chunkCandidates, refLookup,
                        acc, pendingCoordUpdates, counters, countryRules);

                    await FlushChunkAsync(conn, db, country, runId, acc);

                    processTask.Increment(chunkCandidates.Count);
                }
            });

        // Flush coord enrichments across all chunks in one bulk round-trip.
        if (pendingCoordUpdates.Count > 0)
        {
            conn.BulkUpdate("CoordUpdate", pendingCoordUpdates);
        }

        await db.Runs.CompleteRunAsync(runId);

        // --- 6. Summary ---
        Console.WriteLine();
        Console.WriteLine($"Import complete — {stopwatch.ZipPostLookupTaskElapsedTime()}:");

        ImportCandidatesDisplay.PrintSummary(counters);
        Console.WriteLine();

        if (counters.TimezoneDiscrepancies > 0 || counters.NameDiscrepancies > 0)
        {
            Console.WriteLine("  Review discrepancies in SSMS:");
            Console.WriteLine("    SELECT * FROM pipeline.PendingDiscrepancies");
            Console.WriteLine($"    WHERE CountryId = '{country.ToUpperInvariant()}';");
        }

        return 0;
    }

    // =========================================================================
    // Chunk flush — writes all accumulated records to the database
    // =========================================================================

    /// <summary>
    /// Writes everything queued in <paramref name="acc"/> to the database in as
    /// few round-trips as possible, then clears the accumulators for the next chunk.
    ///
    /// Flush order:
    ///   1. New reference rows — BulkInsert("NewReference"), one round-trip.
    ///   2. Discrepancy rows  — bulk append with deduplication.
    ///   3. Rule-5 decisions  — one ApplyDecisionAsync per entry (rare, transactional).
    ///   4. Status updates    — BulkUpdate("CandidateStatusOnly"), one round-trip.
    /// </summary>
    private static async Task FlushChunkAsync(
        SqlConnection     conn,
        WorkDbContext     db,
        string            country,
        string            runId,
        ChunkAccumulators acc)
    {
        // 1. Bulk-insert all genuinely new code+Name rows into data.reference.
        if (acc.NewReferenceRows.Count > 0)
        {
            conn.BulkInsert("NewReference", acc.NewReferenceRows);
        }

        // 2. Append field-level discrepancies (Name and timezone mismatches).
        if (acc.Discrepancies.Count > 0)
        {
            await db.Discrepancies.AppendAsync(runId, country, acc.Discrepancies);
        }

// 3. Bulk-insert all Rule-5 auto-rejection audit rows into pipeline.decisions
//    in a single round-trip. No discrepancy rows exist for these candidates
//    (Rule-5 never calls AppendAsync) and status is already handled by
//    BulkUpdate("CandidateStatusOnly") above.
        if (acc.Decisions.Count > 0)
        {
            var decisionRows = acc.Decisions
                .Select(d => new PipelineDecisions
                {
                    CountryId      = country.ToUpperInvariant(),
                    RunId          = runId,
                    ZpCode         = d.Code,
                    PlaceName      = d.Name,
                    AcceptIncoming = false,
                    DecidedBy      = "auto — TimezoneChecked=1 on reference",
                    Notes          = $"Candidate timezone '{d.Tz}' overruled by verified reference timezone '{d.RefTz}'",
                    CreatedAt      = DateTimeOffset.UtcNow,
                })
                .ToList();

            await db.Discrepancies.BulkInsertDecisionsAsync(decisionRows);
        }

        // 4. Bulk-update candidate statuses (clean / discrepancy / rejected) — one round-trip.
        if (acc.StatusUpdates.Count > 0)
        {
            conn.BulkUpdate("CandidateStatusOnly", acc.StatusUpdates);
        }

        acc.Clear();
    }

    // =========================================================================
    // Main per-chunk classification loop
    // =========================================================================

    /// <summary>
    /// Iterates every candidate in <paramref name="chunkCandidates"/> and classifies
    /// it against the pre-fetched <paramref name="refLookup"/>, accumulating all
    /// writes into <paramref name="acc"/> without touching the database.
    ///
    /// Each rule sets <c>candidate.Status</c> directly so the candidate object is
    /// ready for a single BulkUpdate("CandidateStatusOnly") call at flush time.
    ///
    /// Rules applied per candidate:
    ///   2. New code → queue reference insert + mark clean.
    ///   3. Code+Name match, timezone match → mark clean (optionally back-fill coords).
    ///   4. Code+Name match, timezone differs, TimezoneChecked=0 → timezone discrepancy.
    ///   5. Code+Name match, timezone differs, TimezoneChecked=1 → auto-reject decision.
    ///   6. Code exists, Name not found → name discrepancy (optionally back-fill coords).
    /// </summary>
    private static void ProcessChunkCandidates(
        IReadOnlyList<CodesCandidate>             chunkCandidates,
        IReadOnlyDictionary<string, List<ReferenceReadRow>> refLookup,
        ChunkAccumulators                         acc,
        List<DataReference>                       pendingCoordUpdates,
        ImportCounters                            counters,
        ICountryRules                             rules)
    {
        for (int i = 0; i < chunkCandidates.Count; i++)
        {
            var candidate = chunkCandidates[i];
            counters.Processed++;

            // Step 2 — normalise the candidate fields.
            var code = candidate.ZpCode;
            var name = candidate.PlaceName;
            var tz   = candidate.Timezone == "---" ? "" : candidate.Timezone;

            // Step 3 — special-domain bypass: military, territory, PRS codes are
            // already curated in data.reference; any name/tz format differences
            // from the source file are expected and should not create discrepancies.
            if (rules.IsKnownSpecialCode(code) || rules.IsKnownSpecialName(name))
            {
                candidate.Status = StatusString(CandidateStatus.Clean);
                acc.StatusUpdates.Add(candidate);
                counters.SpecialCodes++;
                continue;
            }

            // Step 4 — look up all reference rows for this code (pre-fetched for the chunk).
            refLookup.TryGetValue(code, out var refRows);

            // Step 5 — Rule 2: code is not in reference at all → queue for insert, mark clean.
            if (refRows == null || refRows.Count == 0)
            {
                ClassifyAsNewCode(candidate, name, tz, acc, counters);
                candidate.Status = StatusString(CandidateStatus.Clean);
                acc.StatusUpdates.Add(candidate);
                continue;
            }

            // Step 5 — attempt a case-insensitive match on the name field.
            var nameMatch = refRows.FirstOrDefault(r =>
                string.Equals(r.PlaceName, name, StringComparison.OrdinalIgnoreCase));

            // Step 6 — Rule 6: code is known but this name is not in reference.
            // Before creating a discrepancy, check if the incoming name is a known
            // alternate (AltNameOf link) for any row with this code — fast index seek.
            if (nameMatch == null)
            {
                // AltNameOf check is done via the pre-fetched refRows rather than
                // a DB round-trip: if any refRow.AltNameOf equals the incoming name,
                // treat as clean (abbreviation match).
                var altMatch = refRows.FirstOrDefault(r =>
                    string.Equals(r.AltNameOf, name, StringComparison.OrdinalIgnoreCase));

                if (altMatch != null)
                {
                    // Known alternate — treat as clean, no discrepancy created.
                    candidate.Status = StatusString(CandidateStatus.Clean);
                    acc.StatusUpdates.Add(candidate);
                    counters.AbbreviationMatches++;
                    continue;
                }

                ClassifyAsNameDiscrepancy(candidate, code, name, refRows,
                    acc, pendingCoordUpdates, counters);
                candidate.Status = StatusString(CandidateStatus.Discrepancy);
                acc.StatusUpdates.Add(candidate);
                continue;
            }

            // Step 7 — name matched; check whether the timezone also agrees.
            bool tzMatch = string.Equals(nameMatch.Timezone, tz, StringComparison.OrdinalIgnoreCase);

            if (tzMatch)
            {
                // Step 8a — Rule 3: perfect match. Optionally back-fill missing coords.
                TryEnrichCoordsForCleanMatch(candidate, nameMatch, pendingCoordUpdates, counters);
                candidate.Status = StatusString(CandidateStatus.Clean);
                acc.StatusUpdates.Add(candidate);
                counters.AlreadyClean++;
            }
            else if (nameMatch.TimezoneChecked)
            {
                // Step 8b — Rule 5: reference timezone is authoritative → auto-reject candidate.
                TryEnrichCoordsForCleanMatch(candidate, nameMatch, pendingCoordUpdates, counters);
                acc.Decisions.Add((code, name, tz, nameMatch.Timezone));
                candidate.Status = StatusString(CandidateStatus.Rejected);
                acc.StatusUpdates.Add(candidate);
                counters.AutoRejected++;
            }
            else
            {
                // Step 8c — Rule 4: timezone differs but not yet verified → needs human review.
                ClassifyAsTimezoneDiscrepancy(code, name, tz, nameMatch, acc, counters);
                candidate.Status = StatusString(CandidateStatus.Discrepancy);
                acc.StatusUpdates.Add(candidate);
            }
        }
    }

    // =========================================================================
    // Classification helpers — one method per outcome
    // =========================================================================

    /// <summary>
    /// Rule 2 — the code is genuinely new: build a DataReference ready for bulk
    /// insert into data.reference. Status is set by the caller.
    /// </summary>
    private static void ClassifyAsNewCode(
        CodesCandidate    candidate,
        string            name,
        string            tz,
        ChunkAccumulators acc,
        ImportCounters    counters)
    {
        acc.NewReferenceRows.Add(new DataReference
        {
            CountryId       = candidate.CountryId,
            ZpCode          = candidate.ZpCode,
            PlaceName       = name,
            Timezone        = tz,
            IsDefault       = candidate.IsDefault,
            TimezoneChecked = HasCoords(candidate.Lat, candidate.Lng),
            Lat             = candidate.Lat,
            Lng             = candidate.Lng,
        });

        counters.NewCodes++;
    }

    /// <summary>
    /// Rule 6 — the code exists in reference but none of its rows carry a matching
    /// name. Records a Name discrepancy for human review. Status is set by the caller.
    /// If the candidate carries coordinates, also attempts to back-fill any reference
    /// rows that share the derived timezone but are currently missing coords.
    /// </summary>
    private static void ClassifyAsNameDiscrepancy(
        CodesCandidate      candidate,
        string              code,
        string              name,
        List<ReferenceReadRow>        refRows,
        ChunkAccumulators   acc,
        List<DataReference> pendingCoordUpdates,
        ImportCounters      counters)
    {
        // Opportunistically back-fill coords on existing reference rows when possible.
        TryEnrichCoordsForNameMismatch(candidate, refRows, pendingCoordUpdates, counters);

        var defaultRef = refRows.FirstOrDefault(r => r.IsDefault) ?? refRows[0];
        acc.Discrepancies.Add(new DiscrepancyInput(
            Code:         code,
            Name:         name,
            AdminLevelId: null,
            FieldName:    "Name",
            RefValue:     defaultRef.PlaceName,
            InValue:      name));

        counters.NameDiscrepancies++;
    }

    /// <summary>
    /// Rule 4 — name matches but the timezone disagrees and has not yet been
    /// verified (TimezoneChecked = 0). Records a timezone discrepancy for review.
    /// Status is set by the caller.
    /// </summary>
    private static void ClassifyAsTimezoneDiscrepancy(
        string            code,
        string            name,
        string            tz,
        ReferenceReadRow            nameMatch,
        ChunkAccumulators acc,
        ImportCounters    counters)
    {
        acc.Discrepancies.Add(new DiscrepancyInput(
            Code:         code,
            Name:         name,
            AdminLevelId: null,
            FieldName:    "timezone",
            RefValue:     nameMatch.Timezone,
            InValue:      tz));

        counters.TimezoneDiscrepancies++;
    }

    // =========================================================================
    // Coordinate back-fill helpers
    // =========================================================================

    /// <summary>
    /// Rule 3 (coord back-fill path) — when the clean reference row is missing
    /// coordinates and the candidate supplies lat/lng, derives the timezone from those
    /// coordinates and, if it matches the reference timezone, queues a coord update.
    /// </summary>
    private static void TryEnrichCoordsForCleanMatch(
        CodesCandidate      candidate,
        ReferenceReadRow              nameMatch,
        List<DataReference> pendingCoordUpdates,
        ImportCounters      counters)
    {
        bool refMissingCoords   = IsMissingCoords(nameMatch.Lat, nameMatch.Lng);
        bool candidateHasCoords = HasCoords(candidate.Lat, candidate.Lng);

        if (!refMissingCoords || !candidateHasCoords) { return; }

        var derivedTz = TimezoneResolver.TryResolveWithCoordinates(candidate.Lat, candidate.Lng);

        if (derivedTz == null) { return; }

        // Only update when the derived timezone agrees with the reference — avoids
        // assigning misleading coords when the coordinate point straddles a zone boundary.
        if (!string.Equals(derivedTz, nameMatch.Timezone, StringComparison.OrdinalIgnoreCase)) { return; }

        pendingCoordUpdates.Add(new DataReference
        {
            ReferenceId     = nameMatch.ReferenceId,
            Lat             = candidate.Lat,
            Lng             = candidate.Lng,
            TimezoneChecked = true,
        });
        counters.CoordsEnriched++;
    }

    /// <summary>
    /// Rule 6 (coord back-fill path) — when a name mismatch occurs but the candidate
    /// carries coordinates, attempts to back-fill any reference rows for the same code
    /// that share the derived timezone but are currently missing coords.
    /// </summary>
    private static void TryEnrichCoordsForNameMismatch(
        CodesCandidate      candidate,
        List<ReferenceReadRow>        refRows,
        List<DataReference> pendingCoordUpdates,
        ImportCounters      counters)
    {
        if (!HasCoords(candidate.Lat, candidate.Lng)) { return; }

        var derivedTz = TimezoneResolver.TryResolveWithCoordinates(candidate.Lat, candidate.Lng);

        if (derivedTz == null) { return; }

        // Find any reference rows that match the derived timezone but lack coords.
        var toEnrich = refRows
            .Where(r => string.Equals(r.Timezone, derivedTz, StringComparison.OrdinalIgnoreCase)
                        && IsMissingCoords(r.Lat, r.Lng))
            .ToList();

        foreach (var r in toEnrich)
        {
            pendingCoordUpdates.Add(new DataReference
            {
                ReferenceId     = r.ReferenceId,
                Lat             = candidate.Lat,
                Lng             = candidate.Lng,
                TimezoneChecked = true,
            });
        }

        counters.CoordsEnriched += toEnrich.Count;
    }

    // =========================================================================
    // Coord utility helpers
    // =========================================================================

    /// <summary>Returns true when both lat and lng are present and not the sentinel "---".</summary>
    private static bool HasCoords(string? lat, string? lng) =>
        !string.IsNullOrEmpty(lat) && lat != "---" &&
        !string.IsNullOrEmpty(lng) && lng != "---";

    /// <summary>Returns true when either lat or lng is absent or is the sentinel "---".</summary>
    private static bool IsMissingCoords(string? lat, string? lng) =>
        string.IsNullOrEmpty(lat) || lat == "---" ||
        string.IsNullOrEmpty(lng) || lng == "---";

    // =========================================================================
    // Status string helper
    // =========================================================================

    // Enum name IS the DB value — "Clean", "Discrepancy", etc.
    private static string StatusString(CandidateStatus status) => status.ToString();

    // =========================================================================
    // Argument parsing
    // =========================================================================

    private static bool TryParseArgs(string[] args, out string file, out string country)
    {
        file    = "";
        country = "";

        if (args.Length == 0) { return false; }

        file = args[0];

        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--country", StringComparison.OrdinalIgnoreCase))
            {
                country = args[++i];
            }
        }

        return !string.IsNullOrEmpty(file);
    }

    private static void PrintUsage() =>
        Console.WriteLine(
            "Usage: countrydatatools importcandidates <file.csv> --country US");

    // =========================================================================
    // Private types
    // =========================================================================

    /// <summary>
    /// Holds all deferred writes accumulated while processing a single chunk.
    /// Passed into ProcessChunkCandidates (filled) then FlushChunkAsync (drained).
    /// </summary>
    private sealed class ChunkAccumulators
    {
        /// <summary>
        /// Candidate objects with Status already set, ready for a single
        /// BulkUpdate("CandidateStatusOnly") call. CandidateId must be populated
        /// (done by the BulkInsert in step 4) for the update identity to resolve.
        /// </summary>
        public List<CodesCandidate> StatusUpdates { get; } = [];

        /// <summary>Field-level discrepancy rows to append (Name or timezone mismatches).</summary>
        public List<DiscrepancyInput> Discrepancies { get; } = [];

        /// <summary>
        /// Genuinely new code+Name rows built from CodesCandidate, ready for
        /// BulkInsert("NewReference") at flush time.
        /// </summary>
        public List<DataReference> NewReferenceRows { get; } = [];

        /// <summary>
        /// Rule-5 auto-rejections — kept separate because each requires an
        /// ApplyDecisionAsync call (transactional, rare).
        /// </summary>
        public List<(string Code, string Name, string Tz, string RefTz)> Decisions { get; } = [];

        /// <summary>Clears all lists so the instance can be reused for the next chunk.</summary>
        public void Clear()
        {
            StatusUpdates.Clear();
            Discrepancies.Clear();
            NewReferenceRows.Clear();
            Decisions.Clear();
        }
    }

}