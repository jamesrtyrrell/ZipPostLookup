using System.Text;
using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Ingestion.Models;

namespace ZipPostLookup.CountryDataTools.Ingestion.AutoImport;

/// <summary>
/// Phase 1: File format, delimiter, encoding, and header detection.
/// Supports CSV, TSV, JSON, and Excel (.xlsx/.xls).
/// For Excel and JSON the service returns a sniff of the format only — the caller
/// is responsible for converting to CSV (via ExcelReaderService / JsonFlattenerService)
/// before running the oracle probe.
/// </summary>
public class FileSnifferService
{
    // Magic bytes for binary Excel (.xls) — Compound Document Header
    private static readonly byte[] XlsMagic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    // Magic bytes for OOXML (.xlsx/.xlsm) — PK ZIP header
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>
    /// Sniff file format and structure.
    /// </summary>
    public FileSniffResult Sniff(string filePath, int sampleRows = 20)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var result = new FileSniffResult();
        var ambiguities = new List<string>();

        // 1. Detect binary formats by magic bytes first (extension can be wrong)
        var magic = ReadMagicBytes(filePath, 8);
        if (StartsWithMagic(magic, XlsMagic))
        {
            result.Format = FileFormat.Excel;
            result.Encoding = Encoding.UTF8; // not applicable but keep consistent
            result.AmbiguityReasons = ambiguities;
            return result;
        }
        if (StartsWithMagic(magic, ZipMagic) && IsExcelExtension(filePath))
        {
            result.Format = FileFormat.Excel;
            result.Encoding = Encoding.UTF8;
            result.AmbiguityReasons = ambiguities;
            return result;
        }

        // 2. Detect JSON by leading '{' or '[' (skip BOM/whitespace)
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".json" || IsJsonContent(magic))
        {
            result.Format = FileFormat.Json;
            result.Encoding = DetectEncoding(filePath);
            result.AmbiguityReasons = ambiguities;
            return result;
        }

        // 3. Delimited text (CSV / TSV) — original logic
        result.Encoding = DetectEncoding(filePath);
        result.Delimiter = Dsv.DelimitedFile.SniffDelimiter(filePath);
        result.Format = result.Delimiter == '\t' ? FileFormat.Tsv : FileFormat.Csv;

        var rows = Dsv.DelimitedFile.ReadRows(filePath, result.Delimiter, maxRows: sampleRows);

        if (rows.Count == 0)
        {
            ambiguities.Add("File is empty or contains only blank lines");
            result.AmbiguityReasons = ambiguities;
            return result;
        }

        var columnCounts = rows.Select(r => r.Length).ToList();
        var columnCountMode = columnCounts
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .First();

        result.ColumnCount = columnCountMode.Key;

        var distinctCounts = columnCounts.Distinct().Count();
        if (distinctCounts > 2)
            ambiguities.Add($"Inconsistent column count: {distinctCounts} different counts found (mode: {result.ColumnCount} columns)");

        if (rows.Count > 0)
        {
            var firstRow = rows[0];
            var alphaCount = firstRow.Count(IsAlpha);
            var alphaPercent = alphaCount / (double)firstRow.Length;
            var uniquePercent = firstRow.Distinct().Count() / (double)firstRow.Length;

            // Check if first row is numeric-only "fake header" (e.g., "0	1	2	3...")
            // These are often column numbers from exported data and should be skipped entirely
            var isNumericOnlyHeader = firstRow.All(cell =>
                !string.IsNullOrWhiteSpace(cell) &&
                cell.All(char.IsDigit));

            if (isNumericOnlyHeader)
            {
                // Strip the numeric header row and treat it as if it has a header
                // (so ReadSampleRows will skip it), but don't save it as HeaderNames
                result.HasHeaderRow = true;
                result.HeaderNames = null;
            }
            else
            {
                result.HasHeaderRow = alphaPercent > 0.5 && uniquePercent > 0.8;

                if (alphaPercent > 0.3 && alphaPercent < 0.7)
                    ambiguities.Add($"Unclear header row: first row is {alphaPercent:P0} alphabetic (threshold: >50%)");

                if (result.HasHeaderRow)
                    result.HeaderNames = firstRow;
            }
        }

        result.AmbiguityReasons = ambiguities;
        return result;
    }

    private static byte[] ReadMagicBytes(string filePath, int count)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[count];
            var read = fs.Read(buf, 0, count);
            return buf[..read];
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private static bool StartsWithMagic(byte[] data, byte[] magic) =>
        data.Length >= magic.Length && magic.SequenceEqual(data[..magic.Length]);

    private static bool IsExcelExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".xlsx" or ".xlsm" or ".xls";
    }

    private static bool IsJsonContent(byte[] magic)
    {
        // Skip UTF-8 BOM if present
        int start = (magic.Length >= 3 && magic[0] == 0xEF && magic[1] == 0xBB && magic[2] == 0xBF) ? 3 : 0;
        if (start >= magic.Length) return false;
        var firstChar = (char)magic[start];
        return firstChar is '{' or '[';
    }

    private static Encoding DetectEncoding(string filePath)
    {
        using var reader = new StreamReader(filePath, detectEncodingFromByteOrderMarks: true);
        reader.ReadLine();
        return reader.CurrentEncoding;
    }

    private static bool IsAlpha(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var alphaCount = value.Count(char.IsLetter);
        return alphaCount > value.Length * 0.5;
    }
}
