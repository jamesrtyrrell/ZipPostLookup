using ZipPostLookup.CountryDataTools.CountryRules;
using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Ingestion.AutoImport;
using ZipPostLookup.CountryDataTools.Ingestion.Models;
using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// Auto-import command: orchestrates all 7 phases of the AI import helper.
/// Command: countrydatatools import auto [file] [options]
/// </summary>
public static class AutoImportCommand
{
    /// <summary>
    /// Options for auto-import command.
    /// </summary>
    public sealed record Options(
        string FilePath,
        int SampleRows = 200,
        double MinHitRate = 0.70,
        string? Country = null,
        bool DryRun = false,
        bool NoLlm = false,
        bool LlmSummary = false,
        bool NoUi = false
    );

    /// <summary>
    /// Run auto-import with typed options.
    /// </summary>
    public static async Task<int> RunAsync(Options opts)
    {
        // TODO: Phase orchestration
        // Phase 1: File Sniff
        // Phase 2: Oracle Probe
        // Phase 3: Column Correlation
        // Phase 4: Disambiguation (if needed and not --no-llm)
        // Phase 5: Mapping Confirmation UI (if not --no-ui)
        // Phase 6: Ingest
        // Phase 7: Post-Ingest Summary + Integrity Checks

        Console.WriteLine($"Auto-importing file: {opts.FilePath}");
        Console.WriteLine();

        string? tempCsvFromConversion = null;  // track so we can delete on exit

        try
        {
            // Phase 1: File Sniff — detects CSV/TSV/JSON/Excel
            Console.WriteLine("Phase 1: Detecting file format...");
            var snifferService = new FileSnifferService();
            var sniff = snifferService.Sniff(opts.FilePath, sampleRows: 20);
            PrintSniffSummary(sniff);

            // Phase 1b: Pre-convert Excel/JSON to a temp CSV so the rest of the
            // pipeline is format-agnostic (probe, correlation, ingestion all expect CSV/TSV).
            string probeFilePath = opts.FilePath;
            FileSniffResult probeSniff = sniff;

            if (sniff.Format == FileFormat.Excel)
            {
                Console.WriteLine();
                Console.WriteLine("Phase 1b: Converting Excel to CSV...");
                tempCsvFromConversion = ExcelReaderService.ConvertToCsv(opts.FilePath);
                probeFilePath = tempCsvFromConversion;
                probeSniff = snifferService.Sniff(probeFilePath, sampleRows: 20);
                Console.WriteLine($"  Converted: {Path.GetFileName(opts.FilePath)} → temp CSV ({probeSniff.ColumnCount} columns, {(probeSniff.HasHeaderRow ? "header detected" : "no header")})");
            }
            else if (sniff.Format == FileFormat.Json)
            {
                Console.WriteLine();
                Console.WriteLine("Phase 1b: Flattening JSON to CSV...");
                tempCsvFromConversion = JsonFlattenerService.ConvertToCsv(opts.FilePath);
                probeFilePath = tempCsvFromConversion;
                probeSniff = snifferService.Sniff(probeFilePath, sampleRows: 20);
                Console.WriteLine($"  Flattened: {Path.GetFileName(opts.FilePath)} → temp CSV ({probeSniff.ColumnCount} columns, header from JSON keys)");
            }

            // Phase 2: Oracle Probe
            Console.WriteLine();
            Console.WriteLine("Phase 2: Probing for postal code column...");
            var oracleService = new OracleProbeService();
            var probe = oracleService.Probe(probeFilePath, probeSniff, opts.SampleRows, opts.MinHitRate);
            PrintProbeSummary(probe);

            // Phase 3: Column Correlation
            Console.WriteLine();
            Console.WriteLine("Phase 3: Correlating columns to fields...");
            var correlationService = new ColumnCorrelationService();
            var sampleRows = ReadSampleRows(probeFilePath, probeSniff, opts.SampleRows);
            var proposal = correlationService.Correlate(probeSniff, probe, sampleRows);
            PrintProposalSummary(proposal);

            // Phase 4: Disambiguation (if needed)
            if (proposal.RequireDisambiguation)
            {
                if (opts.NoLlm)
                {
                    Console.WriteLine();
                    Console.WriteLine("❌ Mapping is ambiguous and --no-llm was specified. Cannot proceed.");
                    Console.WriteLine("Ambiguities:");
                    foreach (var reason in proposal.AmbiguityReasons)
                        Console.WriteLine($"  - {reason}");
                    return 1;
                }

                Console.WriteLine();
                Console.WriteLine("Phase 4: Disambiguating with LLM...");
                var disambiguationService = new DisambiguationService();
                var request = new DisambiguationRequest
                {
                    Sniff = probeSniff,
                    Probe = probe,
                    Proposal = proposal,
                    SampleRows = sampleRows.Take(5).ToArray()
                };
                proposal = await disambiguationService.DisambiguateAsync(request);
                PrintProposalSummary(proposal);
            }

            // Phase 5: Mapping Confirmation UI (or auto-accept with --no-ui)
            MappingProposal confirmedMapping;
            if (opts.NoUi)
            {
                Console.WriteLine();
                Console.WriteLine("Phase 5: Skipping UI confirmation (--no-ui specified).");
                confirmedMapping = proposal;
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Phase 5: Confirming mapping...");
                var confirmationService = new MappingConfirmationService();
                var country = opts.Country ?? probe.DominantCountry;
                var rules = CountryRulesFactory.For(country);
                confirmedMapping = confirmationService.ShowConfirmationUI(probeSniff, probe, proposal, sampleRows, rules);
            }

            // Phase 6: Ingest (always from the CSV path — original or converted temp)
            Console.WriteLine();
            Console.WriteLine("Phase 6: Ingesting data...");
            var country2 = opts.Country ?? probe.DominantCountry;
            var rules2 = CountryRulesFactory.For(country2);
            var ingestionService = new IngestionService(rules2, country2);
            var result = await ingestionService.IngestAsync(probeFilePath, probeSniff, probe, confirmedMapping, opts.DryRun);

            // Phase 7: Post-Ingest Summary
            Console.WriteLine();
            PrintIngestionSummary(result);

            // Oracle-miss feedback report
            if (probe.MissedCodes.Count > 0)
            {
                WriteOracleFeedbackReport(opts.FilePath, probe);
            }

            // Auto-run integrity checks (if not dry-run and data was inserted)
            if (!opts.DryRun && result.Inserted > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Running integrity checks...");
                try
                {
                    // TODO: Call CdtDbIntegrityCommand when it's exposed
                    Console.WriteLine("  (Integrity check integration pending)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠ Integrity check failed: {ex.Message}");
                }
            }

            // Optional LLM summary
            if (opts.LlmSummary && !opts.DryRun)
            {
                Console.WriteLine();
                Console.WriteLine("Generating summary...");
                var summaryPrompt = BuildSummaryPrompt(result);
                var disambiguationService = new DisambiguationService();
                var summary = await disambiguationService.GenerateSummaryAsync(summaryPrompt);
                Console.WriteLine();
                Console.WriteLine(summary);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"❌ Error: {ex.Message}");
            return 1;
        }
        finally
        {
            // Clean up any temp CSV created by Excel/JSON pre-conversion
            if (tempCsvFromConversion != null)
            {
                try { File.Delete(tempCsvFromConversion); } catch { /* best-effort */ }
            }
        }
    }

    /// <summary>
    /// Run auto-import with command-line args (CLI entry point).
    /// </summary>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var filePath = args[0];
        var sampleRows = 200;
        var minHitRate = 0.70;
        string? country = null;
        var dryRun = false;
        var noLlm = false;
        var llmSummary = false;
        var noUi = false;

        // Parse remaining args
        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--sample-rows":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var sr))
                    {
                        sampleRows = sr;
                        i++;
                    }
                    break;
                case "--min-hit-rate":
                    if (i + 1 < args.Length && double.TryParse(args[i + 1], out var hr))
                    {
                        minHitRate = hr;
                        i++;
                    }
                    break;
                case "--country":
                    if (i + 1 < args.Length)
                    {
                        country = args[i + 1].ToUpperInvariant();
                        i++;
                    }
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--no-llm":
                    noLlm = true;
                    break;
                case "--llm-summary":
                    llmSummary = true;
                    break;
                case "--no-ui":
                    noUi = true;
                    break;
            }
        }

        var opts = new Options(
            FilePath: filePath,
            SampleRows: sampleRows,
            MinHitRate: minHitRate,
            Country: country,
            DryRun: dryRun,
            NoLlm: noLlm,
            LlmSummary: llmSummary,
            NoUi: noUi
        );

        return await RunAsync(opts);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: countrydatatools import auto <file> [options]");
        Console.WriteLine();
        Console.WriteLine("Auto-detect file format and import postal code data.");
        Console.WriteLine("Supports CSV, TSV, Excel (.xlsx/.xls), and JSON.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --sample-rows N      Number of rows to probe (default: 200)");
        Console.WriteLine("  --min-hit-rate N     Minimum hit rate threshold 0.0-1.0 (default: 0.70)");
        Console.WriteLine("  --country CC         Force country (US/CA/MX), skip detection");
        Console.WriteLine("  --dry-run            Stop before DB insert, show counts only");
        Console.WriteLine("  --no-llm             Abort on ambiguity instead of calling LLM");
        Console.WriteLine("  --llm-summary        Generate conversational summary after import");
        Console.WriteLine("  --no-ui              Skip interactive confirmation, accept proposal");
    }

    private static void PrintSniffSummary(FileSniffResult sniff)
    {
        Console.WriteLine($"  Format:       {sniff.Format}");
        if (sniff.Format is FileFormat.Csv or FileFormat.Tsv)
        {
            Console.WriteLine($"  Delimiter:    '{sniff.Delimiter}'");
            Console.WriteLine($"  Encoding:     {sniff.Encoding.EncodingName}");
            Console.WriteLine($"  Header row:   {(sniff.HasHeaderRow ? "Yes" : "No")}");
            Console.WriteLine($"  Columns:      {sniff.ColumnCount}");
        }
        if (sniff.AmbiguityReasons.Count > 0)
        {
            Console.WriteLine("  Ambiguities:");
            foreach (var reason in sniff.AmbiguityReasons)
                Console.WriteLine($"    - {reason}");
        }
    }

    private static void PrintProbeSummary(ProbeResult probe)
    {
        Console.WriteLine($"  Postal code column: {probe.PostalCodeColumnIndex}");
        Console.WriteLine($"  Hit rate:           {probe.ColumnHitRates[probe.PostalCodeColumnIndex]:P0}");
        Console.WriteLine($"  Dominant country:   {probe.DominantCountry}");
        Console.WriteLine($"  Oracle hits:        {probe.SampleHits.Count}");
        Console.WriteLine($"  Oracle misses:      {probe.MissedCodes.Count}");
        if (probe.IsAmbiguous)
            Console.WriteLine("  ⚠ Ambiguous: multiple columns within 10% hit rate");
    }

    private static void PrintProposalSummary(MappingProposal proposal)
    {
        Console.WriteLine("  Proposed mappings:");
        foreach (var mapping in proposal.Mappings)
        {
            var badge = mapping.Confidence switch
            {
                >= 0.8 => "★★★",
                >= 0.6 => "★★☆",
                >= 0.4 => "★☆☆",
                _ => "☆☆☆"
            };
            Console.WriteLine($"    {mapping.FieldName,-15} → column {mapping.ColumnIndex} {badge} ({mapping.Confidence:P0})");
        }
        if (proposal.RequireDisambiguation)
        {
            Console.WriteLine("  ⚠ Requires disambiguation:");
            foreach (var reason in proposal.AmbiguityReasons)
                Console.WriteLine($"    - {reason}");
        }
    }


    private static void WriteOracleFeedbackReport(string filePath, ProbeResult probe)
    {
        var feedbackPath = Path.ChangeExtension(filePath, ".oracle-misses.txt");
        var lines = new List<string>
        {
            "# Oracle Feedback Report",
            $"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            $"# Country: {probe.DominantCountry}",
            $"# File: {filePath}",
            "",
            $"The following postal codes were NOT found in the built-in {probe.DominantCountry} registry:",
            ""
        };
        lines.AddRange(probe.MissedCodes.Select(c => $"  {c}"));

        File.WriteAllLines(feedbackPath, lines);

        Console.WriteLine();
        Console.WriteLine($"⚠ Oracle missed {probe.MissedCodes.Count} codes.");
        Console.WriteLine($"  Feedback report: {feedbackPath}");
        Console.WriteLine($"  Consider enriching these codes and re-exporting the {probe.DominantCountry} registry.");
    }


    private static string[][] ReadSampleRows(string filePath, FileSniffResult sniff, int sampleRows)
    {
        var rows = Dsv.DelimitedFile.ReadRows(filePath, sniff.Delimiter, maxRows: sampleRows + (sniff.HasHeaderRow ? 1 : 0));

        // Skip header if present
        if (sniff.HasHeaderRow && rows.Count > 0)
        {
            rows.RemoveAt(0);
        }

        return rows.ToArray();
    }

    private static void PrintIngestionSummary(IngestionResult result)
    {
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine("  Auto-Import Summary");
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine($"Total rows:        {result.TotalRows}");
        Console.WriteLine($"Candidates:        {result.CandidatesGenerated}");
        Console.WriteLine($"Inserted:          {result.Inserted}");
        Console.WriteLine($"Discrepancies:     {result.Discrepancies}");
        Console.WriteLine($"Skipped:           {result.Skipped}");
        Console.WriteLine($"Rejected:          {result.RejectedRows}");
        if (result.RejectedFilePath != null)
            Console.WriteLine($"Rejected file:     {result.RejectedFilePath}");
        if (result.DryRun)
            Console.WriteLine("(DRY RUN - no data written)");
        Console.WriteLine("═══════════════════════════════════════════════════");
    }

    private static string BuildSummaryPrompt(IngestionResult result)
    {
        return $@"You just imported {result.Inserted} postal code candidates from a user file.

**Ingestion results:**
- Total rows: {result.TotalRows}
- Inserted: {result.Inserted}
- Discrepancies: {result.Discrepancies}
- Rejected: {result.RejectedRows}

Provide a 2-3 sentence summary for the user, highlighting key findings and next steps.";
    }
}
