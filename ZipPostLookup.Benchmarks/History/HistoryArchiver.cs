using System.Formats.Tar;
using System.IO.Compression;

namespace ZipPostLookup.Benchmarks.History;

/// <summary>
/// Manages dated CSV entries inside per-class tar.gz archives under the History/ folder.
///
/// Archive layout:
///   History/LookupBenchmarks.tar.gz
///     2026-06-04-LookupBenchmarks-report.csv
///     2026-06-10-LookupBenchmarks-report.csv
///
/// Append strategy: tar does not support in-place append, so we read all existing entries
/// into memory, write them plus the new entry to a temp file, then atomically replace.
/// </summary>
internal static class HistoryArchiver
{
    private const string NamespacePrefix = "ZipPostLookup.Benchmarks.";

    /// <summary>
    /// Extracts the short class name from a BenchmarkDotNet CSV filename.
    /// "ZipPostLookup.Benchmarks.LookupBenchmarks-report.csv" → "LookupBenchmarks"
    /// Returns null if the file doesn't match the expected pattern.
    /// </summary>
    public static string? GetClassName(string csvPath)
    {
        var stem = Path.GetFileNameWithoutExtension(csvPath);
        if (!stem.EndsWith("-report", StringComparison.OrdinalIgnoreCase))
            return null;

        stem = stem[..^"-report".Length];

        if (stem.StartsWith(NamespacePrefix, StringComparison.OrdinalIgnoreCase))
            stem = stem[NamespacePrefix.Length..];

        return string.IsNullOrWhiteSpace(stem) ? null : stem;
    }

    /// <summary>
    /// Appends the CSV at <paramref name="csvPath"/> into History/{className}.tar.gz
    /// as a dated entry: {yyyy-MM-dd}-{className}-report.csv.
    /// Creates the archive if it doesn't exist.
    /// Returns false (and prints a warning) if a same-date entry already exists.
    /// </summary>
    public static bool AppendToArchive(string historyDir, string className, string csvPath)
    {
        var date        = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var entryName   = $"{date}-{className}-report.csv";
        var archivePath = Path.Combine(historyDir, $"{className}.tar.gz");
        var csvBytes    = File.ReadAllBytes(csvPath);

        // Read all existing entries (if archive exists).
        var existing = new List<(string Name, byte[] Data)>();

        if (File.Exists(archivePath))
        {
            using var readStream = File.OpenRead(archivePath);
            using var gzipIn     = new GZipStream(readStream, CompressionMode.Decompress);
            using var reader     = new TarReader(gzipIn, leaveOpen: false);

            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) != null)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    continue;

                if (entry.Name == entryName)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  ⚠  '{entryName}' already exists in {className}.tar.gz — skipped.");
                    Console.ResetColor();
                    return false;
                }

                using var ms = new MemoryStream();
                entry.DataStream?.CopyTo(ms);
                existing.Add((entry.Name, ms.ToArray()));
            }
        }

        // Write existing entries + new entry to a temp file, then atomically replace.
        var tmp = archivePath + ".tmp";
        try
        {
            using (var writeStream = File.Create(tmp))
            using (var gzipOut     = new GZipStream(writeStream, CompressionLevel.Optimal))
            using (var writer      = new TarWriter(gzipOut, TarEntryFormat.Pax, leaveOpen: false))
            {
                foreach (var (name, data) in existing)
                {
                    var e = new PaxTarEntry(TarEntryType.RegularFile, name);
                    e.DataStream = new MemoryStream(data);
                    writer.WriteEntry(e);
                }

                var newEntry = new PaxTarEntry(TarEntryType.RegularFile, entryName);
                newEntry.DataStream = new MemoryStream(csvBytes);
                writer.WriteEntry(newEntry);
            }

            File.Move(tmp, archivePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            throw;
        }

        return true;
    }

    /// <summary>
    /// Returns all *-report.csv files in <paramref name="artifactsDir"/> whose last-write
    /// time is at or after <paramref name="runStarted"/>, as (className, fullPath) pairs.
    /// </summary>
    public static IReadOnlyList<(string ClassName, string CsvPath)> FindFreshReports(
        string artifactsDir, DateTime runStarted)
    {
        if (!Directory.Exists(artifactsDir))
            return [];

        var results = new List<(string, string)>();

        foreach (var file in Directory.EnumerateFiles(artifactsDir, "*-report.csv"))
        {
            if (File.GetLastWriteTimeUtc(file) < runStarted.ToUniversalTime())
                continue;

            var className = GetClassName(file);
            if (className != null)
                results.Add((className, file));
        }

        return results;
    }
}
