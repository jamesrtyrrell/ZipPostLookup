using System.Globalization;
using CsvHelper.Configuration;

namespace ZipPostLookup.CountryDataTools.Dsv;

/// <summary>
/// A raw row parsed from the input CSV, before any validation or fixing.
/// All fields are strings so we can report exactly what was in the file.
/// RecordNumber is 1-based (matches line number in the file after the header).
/// </summary>
public sealed class CsvRow
{
    public int    RecordNumber { get; set; }
    public string RawLine     { get; set; } = "";

    // Fields — null means the column was absent entirely (TooFewColumns),
    // empty string means the column was present but blank.
    public string? ZpCode    { get; set; }
    public string? PlaceName { get; set; }
    public string? Timezone  { get; set; }
    public string? IsDefault { get; set; }
    public string? Lat       { get; set; }
    public string? Lng       { get; set; }
    public string? Admin1    { get; set; }
    public string? Admin1Code { get; set; }
}

/// <summary>
/// Reads a CSV file into <see cref="CsvRow"/> instances using CsvHelper.
///
/// CsvHelper handles BOM detection, RFC 4180 quoting, and embedded newlines
/// inside quoted fields natively — no manual parsing required.
///
/// Expected columns (PascalCase): ZpCode,PlaceName,Timezone,IsDefault,Lat,Lng,Admin1,Admin1Code
/// Lat, Lng, Admin1, and Admin1Code are optional but recognised.
///
/// Column name matching is case-insensitive, so files with legacy snake_case
/// headers (code, is_default, admin1_code) are also accepted.
///
/// If the file has the expected number of columns but no recognisable header row,
/// the standard PascalCase header is injected automatically before parsing begins.
/// </summary>
public static class CsvReader
{
    /// <summary>Columns that must be present for the header to be considered valid.</summary>
    public static readonly string[] RequiredColumns =
        ["ZpCode", "PlaceName", "Timezone", "IsDefault"];

    /// <summary>All columns the reader recognises (required + optional).</summary>
    public static readonly string[] ExpectedColumns =
        ["ZpCode", "PlaceName", "Timezone", "IsDefault", "Lat", "Lng", "Admin1", "Admin1Code"];

    /// <summary>
    /// Reads all data rows from <paramref name="filePath"/>.
    /// Returns the rows and reports whether the header was structurally valid.
    /// </summary>
    public static (IReadOnlyList<CsvRow> Rows, bool HeaderOk, IReadOnlyList<string> MissingColumns)
        Read(string filePath)
    {
        // Read raw text once — detectEncodingFromByteOrderMarks strips the BOM automatically.
        string rawText;
        using (var sr = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true))
        {
            rawText = sr.ReadToEnd();
        }

        // Detect a headerless file: peek at the first line and check whether any
        // token matches a known column name (case-insensitive). If not, prepend the
        // standard PascalCase header.
        var firstLine = rawText.Split(["\r\n", "\r", "\n"], 2, StringSplitOptions.None)[0];
        var firstTokens = firstLine.Split(',')
            .Select(t => t.Trim().Trim('"'))
            .ToArray();

        bool hasHeader = firstTokens.Any(t =>
            ExpectedColumns.Contains(t, StringComparer.OrdinalIgnoreCase));

        if (!hasHeader)
        {
            rawText = string.Join(",", ExpectedColumns) + "\n" + rawText;
            Console.WriteLine("  ⚠ No header row detected — standard header injected automatically.");
        }

        // Validate which required columns are present in the header line.
        var headerLine = rawText.Split(["\r\n", "\r", "\n"], 2, StringSplitOptions.None)[0];
        var headerTokens = headerLine.Split(',')
            .Select(t => t.Trim().Trim('"'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingColumns = RequiredColumns
            .Where(col => !headerTokens.Contains(col))
            .ToList();

        bool headerOk = missingColumns.Count == 0;

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // Allow rows with fewer fields than the header — maps missing ones to null.
            MissingFieldFound = null,
            // Trim whitespace from unquoted field values.
            TrimOptions = TrimOptions.Trim,
            // Skip completely blank lines silently.
            ShouldSkipRecord = args => args.Row.Parser.Record?.All(string.IsNullOrWhiteSpace) ?? true,
            // Log bad rows to the console rather than throwing, so one malformed
            // line does not abort the entire import.
            BadDataFound = args =>
            {
                if (args.Context.Parser != null)
                {
                    Console.WriteLine($"  ⚠ Bad data at row {args.Context.Parser.Row}: {args.Field}");
                }
            },
            // Header matching is case-insensitive so PascalCase and snake_case files
            // both resolve to the same CsvRow properties.
            HeaderValidated = null,
        };

        var rows = new List<CsvRow>();

        using var reader = new StringReader(rawText);
        using var csv    = new CsvHelper.CsvReader(reader, config);

        csv.Context.RegisterClassMap<CsvRowMap>();

        // Read() advances to the next record. RecordNumber tracks the 1-based
        // data-row index (excluding the header line).
        while (csv.Read())
        {
            CsvRow record;
            try
            {
                record = csv.GetRecord<CsvRow>();
            }
            catch (Exception ex)
            {
                if (csv.Context.Parser != null)
                {
                    Console.WriteLine($"  ⚠ Skipping row {csv.Context.Parser.Row}: {ex.Message}");
                }
                continue;
            }

            if (csv.Context.Parser != null)
            {
                record.RecordNumber = csv.Context.Parser.Row - 1; // 1-based data row
                record.RawLine = csv.Context.Parser.RawRecord.TrimEnd('\r', '\n');
            }

            rows.Add(record);
        }

        return (rows, headerOk, missingColumns);
    }

    // -------------------------------------------------------------------------
    // ClassMap — maps CSV header names to CsvRow properties.
    // Multiple Name() overloads allow legacy snake_case headers to map to the
    // same properties as the canonical PascalCase headers.
    // -------------------------------------------------------------------------

    private sealed class CsvRowMap : ClassMap<CsvRow>
    {
        public CsvRowMap()
        {
            // RecordNumber and RawLine are set in code, not from CSV columns.
            Map(r => r.RecordNumber).Ignore();
            Map(r => r.RawLine).Ignore();

            // Accept canonical names, legacy PascalCase names, and snake_case column names.
            Map(r => r.ZpCode).Name("ZpCode", "Code", "code", "zip", "Zip");
            Map(r => r.PlaceName).Name("PlaceName", "Name", "name", "city", "City");
            Map(r => r.Timezone).Name("Timezone", "timezone");
            Map(r => r.IsDefault).Name("IsDefault", "is_default");
            Map(r => r.Lat).Name("Lat", "lat").Optional();
            Map(r => r.Lng).Name("Lng", "lng").Optional();
            Map(r => r.Admin1).Name("Admin1", "admin1").Optional();
            Map(r => r.Admin1Code).Name("Admin1Code", "admin1_code", "Admin1Code").Optional();
        }
    }
}