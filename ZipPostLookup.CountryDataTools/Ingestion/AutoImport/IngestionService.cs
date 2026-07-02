using System.Text;
using ZipPostLookup.CountryDataTools.CountryRules;
using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Ingestion.Models;
using ZipPostLookup.CountryDataTools.Utilities;
using ZipPostLookup.CountryDataTools.Models.Dbo;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.Normalizers;

namespace ZipPostLookup.CountryDataTools.Ingestion.AutoImport;

/// <summary>
/// Phase 6: Ingest mapped rows — validate, normalise, write a CDT-format temp CSV,
/// then delegate to ImportCandidatesCommand which owns the DB pipeline.
/// </summary>
public class IngestionService
{
    private readonly ICountryRules _rules;
    private readonly ICountryCodeRules? _normalizer;
    private readonly string _country;

    public IngestionService(ICountryRules rules, string country)
    {
        _rules = rules;
        _country = country;
        _normalizer = country.ToUpperInvariant() switch
        {
            "US" => new UsCountryCodeRules(),
            "CA" => new CaCountryCodeRules(),
            "MX" => new MxCountryCodeRules(),
            _    => null,
        };
    }

    /// <summary>
    /// Validate, normalize rows, build CodesCandidate objects,
    /// then call ImportCandidatesCommand.ProcessBatchAsync to insert into the working DB.
    /// </summary>
    public async Task<IngestionResult> IngestAsync(
        string filePath,
        FileSniffResult sniff,
        ProbeResult probe,
        MappingProposal mapping,
        bool dryRun = false)
    {
        // 1. Read all rows (skip header if present)
        var allRows = DelimitedFile.ReadRows(filePath, sniff.Delimiter, maxRows: null);
        if (sniff.HasHeaderRow && allRows.Count > 0)
            allRows.RemoveAt(0);

        var candidates = new List<CodesCandidate>();
        var rejectedRows = new List<(int rowNum, string[] row, string reason)>();

        // 2. Build CodesCandidate objects
        for (int i = 0; i < allRows.Count; i++)
        {
            var row = allRows[i];

            try
            {
                var zpCode = GetMappedValue(mapping, row, "ZpCode");
                if (string.IsNullOrWhiteSpace(zpCode))
                {
                    rejectedRows.Add((i + 1, row, "Missing postal code"));
                    continue;
                }

                // Normalize to the canonical form (strip spaces/hyphens, uppercase) and reject
                // codes that fail the country's structural format. ImportCandidatesCommand does
                // NOT do this — in the classic pipeline it happens upstream in validate/fix —
                // so without this gate raw source formats ("T0A 0A4") and stray header/index
                // rows land in data.Reference as distinct bogus codes.
                if (_normalizer is not null)
                {
                    var normalized = _normalizer.Normalize(zpCode);
                    if (!_normalizer.Validate(normalized))
                    {
                        rejectedRows.Add((i + 1, row,
                            $"Invalid {_country.ToUpperInvariant()} postal code format: '{zpCode}'"));
                        continue;
                    }

                    zpCode = normalized;
                }

                var placeName = GetMappedValue(mapping, row, "PlaceName");
                if (string.IsNullOrWhiteSpace(placeName))
                {
                    rejectedRows.Add((i + 1, row, "Missing place name"));
                    continue;
                }

                var timezone = GetMappedValue(mapping, row, "Timezone") ?? "---";
                var isDefaultStr = GetMappedValue(mapping, row, "IsDefault");
                var isDefault = bool.TryParse(isDefaultStr, out var isDef) ? isDef : false;

                // Parse coordinates (stored as strings in CodesCandidate)
                var latStr = GetMappedValue(mapping, row, "Latitude") ?? "---";
                var lngStr = GetMappedValue(mapping, row, "Longitude") ?? "---";

                // Derive timezone from coords if missing
                if (timezone == "---" || string.IsNullOrWhiteSpace(timezone))
                {
                    var derivedTz = TimezoneResolver.TryResolveWithCoordinates(latStr, lngStr);
                    if (!string.IsNullOrWhiteSpace(derivedTz))
                    {
                        timezone = _rules.CanonicalizeTimezone(derivedTz) ?? derivedTz;
                    }
                }

                // Derive Admin1 if missing
                var admin1 = GetMappedValue(mapping, row, "Admin1");
                var admin1Code = GetMappedValue(mapping, row, "Admin1Code");
                if (string.IsNullOrWhiteSpace(admin1) && _rules.SupportsAdmin1Derivation)
                {
                    var adminResult = _rules.ResolveAdmin1(zpCode);
                    if (adminResult.HasValue)
                    {
                        admin1Code = adminResult.Value.Code;
                        admin1 = adminResult.Value.Name;
                    }
                }

                // Build admin candidate list
                var adminList = new List<CodesCandidateAdmin>();
                if (!string.IsNullOrWhiteSpace(admin1))
                {
                    adminList.Add(new CodesCandidateAdmin(1, admin1, admin1Code ?? ""));
                }

                // Build CodesCandidate
                var candidate = new CodesCandidate
                {
                    CountryId = _country.ToUpperInvariant(),
                    ZpCode = zpCode,
                    PlaceName = placeName,
                    Timezone = timezone,
                    IsDefault = isDefault,
                    Lat = latStr,
                    Lng = lngStr,
                    Admin1 = admin1 ?? "",
                    Admin1Code = admin1Code ?? "",
                    AdminCandidateList = adminList,
                    Status = "Pending"  // Will be updated by ProcessBatchAsync
                };

                candidates.Add(candidate);
            }
            catch (Exception ex)
            {
                rejectedRows.Add((i + 1, row, $"Error: {ex.Message}"));
            }
        }

        // 3. Write rejected rows to sidecar file
        string? rejectedFilePath = null;
        if (rejectedRows.Count > 0)
        {
            rejectedFilePath = Path.ChangeExtension(filePath, ".rejected.csv");
            WriteRejectedFile(rejectedFilePath, rejectedRows, sniff.Delimiter);
        }

        if (dryRun)
        {
            return new IngestionResult
            {
                TotalRows = allRows.Count,
                CandidatesGenerated = candidates.Count,
                RejectedRows = rejectedRows.Count,
                OracleMissedCodes = probe.MissedCodes,
                DryRun = true
            };
        }

        // 4. Connect to DB and process batch
        var db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
        var runId = await db.Runs.CreateRunAsync(_country.ToUpperInvariant(), Path.GetFileName(filePath));

        // Set RunId on all candidates
        foreach (var candidate in candidates)
        {
            candidate.RunId = runId;
        }

        var counters = await ImportCandidatesCommand.ProcessBatchAsync(db, _country, runId, candidates);

        return new IngestionResult
        {
            TotalRows = allRows.Count,
            CandidatesGenerated = candidates.Count,
            Inserted = counters.NewCodes + counters.AlreadyClean + counters.CoordsEnriched,
            Discrepancies = counters.NameDiscrepancies + counters.TimezoneDiscrepancies,
            Skipped = counters.AutoRejected,
            RejectedRows = rejectedRows.Count,
            RejectedFilePath = rejectedFilePath,
            OracleMissedCodes = probe.MissedCodes,
            DryRun = false
        };
    }

    private string? GetMappedValue(MappingProposal mapping, string[] row, string fieldName)
    {
        var fieldMapping = mapping.Mappings.FirstOrDefault(m =>
            string.Equals(m.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));

        if (fieldMapping?.ColumnIndex == null || fieldMapping.ColumnIndex >= row.Length)
            return null;

        return row[fieldMapping.ColumnIndex.Value]?.Trim();
    }

    private static void WriteRejectedFile(
        string outputPath,
        List<(int rowNum, string[] row, string reason)> rejectedRows,
        char delimiter)
    {
        using var writer = new StreamWriter(outputPath);
        writer.WriteLine($"RowNumber{delimiter}Reason{delimiter}OriginalRow");
        foreach (var (rowNum, row, reason) in rejectedRows)
        {
            var escapedReason = reason.Replace("\"", "\"\"");
            var originalRow = string.Join(delimiter.ToString(), row.Select(c => $"\"{c?.Replace("\"", "\"\"")}\""));
            writer.WriteLine($"{rowNum}{delimiter}\"{escapedReason}\"{delimiter}{originalRow}");
        }
    }
}
