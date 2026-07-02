using ExcelDataReader;
using System.Data;
using System.Text;

namespace ZipPostLookup.CountryDataTools.Ingestion.AutoImport;

/// <summary>
/// Reads .xlsx/.xls files into a flat List&lt;string[]&gt; identical in shape to
/// DelimitedFile.ReadRows, so the rest of the auto-import pipeline is format-agnostic.
/// Uses ExcelDataReader (no Excel COM interop required).
/// </summary>
public static class ExcelReaderService
{
    /// <summary>
    /// Read up to <paramref name="maxRows"/> rows from the first non-empty sheet.
    /// Returns the sheet name that was selected (for diagnostics).
    /// </summary>
    public static (List<string[]> Rows, string SheetName) ReadRows(
        string filePath,
        int? maxRows = null)
    {
        // ExcelDataReader requires this registration on non-Windows runtimes.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = CreateReader(filePath, stream);

        var config = new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration
            {
                UseHeaderRow = false   // we detect header ourselves
            }
        };

        var dataSet = reader.AsDataSet(config);

        // Pick first non-empty sheet
        DataTable? sheet = null;
        string sheetName = "";
        foreach (DataTable table in dataSet.Tables)
        {
            if (table.Rows.Count > 0)
            {
                sheet = table;
                sheetName = table.TableName;
                break;
            }
        }

        if (sheet == null)
            return (new List<string[]>(), "");

        var rows = new List<string[]>();
        var limit = maxRows ?? int.MaxValue;

        foreach (DataRow row in sheet.Rows)
        {
            if (rows.Count >= limit) break;

            var cells = new string[row.ItemArray.Length];
            for (int i = 0; i < row.ItemArray.Length; i++)
            {
                cells[i] = row.ItemArray[i]?.ToString()?.Trim() ?? "";
            }
            rows.Add(cells);
        }

        return (rows, sheetName);
    }

    /// <summary>
    /// Write rows as a UTF-8 CSV so the rest of the pipeline can read it via DelimitedFile.
    /// </summary>
    public static string ConvertToCsv(string excelPath, int? maxRows = null)
    {
        var (rows, _) = ReadRows(excelPath, maxRows);
        var tempPath = Path.Combine(Path.GetTempPath(), $"zpl-excel-{Guid.NewGuid():N}.csv");
        WriteCsv(tempPath, rows);
        return tempPath;
    }

    private static void WriteCsv(string outputPath, List<string[]> rows)
    {
        using var writer = new StreamWriter(outputPath, append: false,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        foreach (var row in rows)
        {
            var line = string.Join(",", row.Select(CsvEscape));
            writer.WriteLine(line);
        }
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static IExcelDataReader CreateReader(string filePath, Stream stream)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".xlsx" or ".xlsm" => ExcelReaderFactory.CreateOpenXmlReader(stream),
            ".xls"             => ExcelReaderFactory.CreateBinaryReader(stream),
            _                  => ExcelReaderFactory.CreateReader(stream)   // auto-detect
        };
    }
}
