using System.Text;

namespace ZipPostLookup.CountryDataTools.Dsv;

/// <summary>
/// Shared reader for delimited text files (CSV / TSV). Handles BOM/encoding detection,
/// delimiter sniffing, and RFC-4180 quoted-field parsing in one place. Used by the
/// coordinate-enrichment command and the dashboard column-mapping widget so both parse files
/// identically.
/// </summary>
public static class DelimitedFile
{
    /// <summary>
    /// Sniffs the delimiter from the first non-empty line: tab if tabs outnumber commas,
    /// otherwise comma. Returns ',' for an empty/unreadable file.
    /// </summary>
    public static char SniffDelimiter(string path)
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) { continue; }

            var tabs   = line.Count(c => c == '\t');
            var commas = line.Count(c => c == ',');
            return tabs > commas ? '\t' : ',';
        }

        return ',';
    }

    /// <summary>
    /// Reads up to <paramref name="maxRows"/> rows (all rows when null), each split into fields
    /// by <paramref name="delimiter"/>. Blank lines are skipped. Comma files honour RFC-4180
    /// quoting; tab files are split literally (GeoNames-style, no quoting).
    /// </summary>
    public static List<string[]> ReadRows(string path, char delimiter, int? maxRows = null)
    {
        var rows = new List<string[]>();

        // detectEncodingFromByteOrderMarks handles UTF-8 BOM, UTF-16 LE/BE and UTF-32.
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) { continue; }

            rows.Add(SplitLine(line, delimiter));
            if (maxRows.HasValue && rows.Count >= maxRows.Value) { break; }
        }

        return rows;
    }

    /// <summary>
    /// Splits a single line into fields. For commas, respects RFC-4180 double-quoted fields that
    /// may contain embedded delimiters or escaped double-quotes; for any other delimiter (e.g.
    /// tab) it is a plain split.
    /// </summary>
    public static string[] SplitLine(string line, char delimiter)
    {
        if (delimiter != ',')
            return line.Split(delimiter);

        var fields = new List<string>();
        int pos = 0;

        while (pos <= line.Length)
        {
            if (pos < line.Length && line[pos] == '"')
            {
                pos++;
                var sb = new StringBuilder();
                while (pos < line.Length)
                {
                    if (line[pos] == '"')
                    {
                        pos++;
                        // Escaped double-quote inside a quoted field.
                        if (pos < line.Length && line[pos] == '"')
                        {
                            sb.Append('"');
                            pos++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(line[pos++]);
                    }
                }
                fields.Add(sb.ToString());
                if (pos < line.Length && line[pos] == ',') { pos++; }
            }
            else
            {
                int comma = line.IndexOf(',', pos);
                if (comma < 0)
                {
                    fields.Add(line[pos..]);
                    break;
                }
                fields.Add(line[pos..comma]);
                pos = comma + 1;
            }
        }

        return [.. fields];
    }
}
