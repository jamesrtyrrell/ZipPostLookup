using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using ZipPostLookup.CountryDataTools.Validation.Csv;

namespace ZipPostLookup.CountryDataTools.Dsv;

/// <summary>
/// Writes a list of <see cref="CsvRow"/> instances back to a CSV file using CsvHelper.
/// Output is always UTF-8 with BOM, which is unambiguous for editors and Excel.
///
/// Quoting rules:
///   · zip, Name, lat, lng, admin1, Admin1Code — always double-quoted
///     (zip: preserves leading zeros in Excel; others: handle embedded commas)
///   · timezone, IsDefault — quoted only when the value contains a comma,
///     double-quote, or newline (RFC 4180 minimum quoting via CsvHelper default)
///
/// Because <see cref="ShouldQuoteArgs"/> carries no member metadata in CsvHelper v33,
/// always-quote fields are handled by wrapping them in quotes inside ConvertUsing
/// in the ClassMap, with global ShouldQuote disabled (returns false) so CsvHelper
/// never adds a second layer of quotes around those pre-quoted values.
/// The remaining fields (timezone, IsDefault) are written normally and let
/// CsvHelper apply RFC 4180 quoting automatically.
/// </summary>
public static class CsvWriter
{
    /// <summary>
    /// Writes <paramref name="rows"/> to <paramref name="outputPath"/>.
    /// Rows are written in their current order (caller is responsible for sorting if needed).
    /// </summary>
    public static void Write(IReadOnlyList<CsvRow> rows, string outputPath)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            NewLine = "\r\n",
            // Disable automatic quoting globally — always-quote fields embed their
            // own quotes via ConvertUsing; CsvHelper would double-quote them otherwise.
            ShouldQuote = _ => false,
        };

        using var writer = new StreamWriter(outputPath, append: false, encoding: ReportWriter.Utf8Bom);
        using var csv = new CsvHelper.CsvWriter(writer, config);

        csv.Context.RegisterClassMap<CsvRowWriteMap>();

        csv.WriteHeader<CsvRow>();
        csv.NextRecord();

        foreach (var row in rows)
        {
            csv.WriteRecord(row);
            csv.NextRecord();
        }
    }

    // -------------------------------------------------------------------------
    // ClassMap
    //
    // Always-quoted fields use ConvertUsing to embed RFC 4180 quoting directly
    // in the converted value. The global ShouldQuote = false ensures CsvHelper
    // does not add another layer on top.
    //
    // Timezone and IsDefault are written as plain strings. Because ShouldQuote
    // returns false globally those fields are never quoted — which is acceptable
    // for timezone (IANA names contain no commas) and IsDefault (true/false).
    // If a future timezone value ever contained a comma, switch ShouldQuote to
    // inspect the field value rather than disabling it entirely.
    // -------------------------------------------------------------------------

    private sealed class CsvRowWriteMap : ClassMap<CsvRow>
    {
        public CsvRowWriteMap()
        {
            Map(r => r.RecordNumber).Ignore();
            Map(r => r.RawLine).Ignore();
            Map(r => r.ZpCode).Name("ZpCode").Index(0).Convert(r => AlwaysQuote(r.Value.ZpCode));
            Map(r => r.PlaceName).Name("PlaceName").Index(1).Convert(r => AlwaysQuote(r.Value.PlaceName));
            Map(r => r.Timezone).Name("Timezone").Index(2);
            Map(r => r.IsDefault).Name("IsDefault").Index(3).Convert(r => (r.Value.IsDefault ?? "false").ToLowerInvariant());
            Map(r => r.Lat).Name("Lat").Index(4).Convert(r => AlwaysQuote(r.Value.Lat ?? "---"));
            Map(r => r.Lng).Name("Lng").Index(5).Convert(r => AlwaysQuote(r.Value.Lng ?? "---"));
            Map(r => r.Admin1).Name("Admin1").Index(6).Convert(r => AlwaysQuote(r.Value.Admin1));
            Map(r => r.Admin1Code).Name("Admin1Code").Index(7).Convert(r => AlwaysQuote(r.Value.Admin1Code));
        }

        /// <summary>
        /// Wraps <paramref name="value"/> in double-quotes, escaping any embedded
        /// double-quotes by doubling them (RFC 4180 §2.7).
        /// </summary>
        private static string AlwaysQuote(string? value) =>
            $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
    }
}
