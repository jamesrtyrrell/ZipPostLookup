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
internal enum ArchiveOutcome { Added, Overwritten, Skipped }

/// <summary>
/// Tracks the user's overwrite decision across a batch of archive appends. A single instance is
/// shared by every <see cref="HistoryArchiver.AppendToArchive"/> call in one run so that choosing
/// "yes to all" suppresses further prompts for the remaining reports.
/// </summary>
internal sealed class OverwritePrompt
{
    private bool _yesToAll;

    /// <summary>
    /// Returns true if the same-date entry should be overwritten. Prompts the user [y/n/a] unless
    /// "yes to all" was previously chosen. 'a' sets yes-to-all for the rest of the run; empty/'n'
    /// means skip; anything else re-prompts.
    /// </summary>
    public bool ShouldOverwrite(string entryName, string archiveName)
    {
        if (_yesToAll)
            return true;

        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"  ⚠  '{entryName}' already exists in {archiveName} — overwrite? [y/n/(a) yes to all] ");
            Console.ResetColor();

            switch (Console.ReadLine()?.Trim().ToUpperInvariant())
            {
                case "Y":
                    return true;
                case "A":
                    _yesToAll = true;
                    return true;
                case "N":
                case "":
                    return false;
                // Any other input: re-prompt.
            }
        }
    }
}

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
    /// If a same-date entry already exists, <paramref name="prompt"/> decides whether to overwrite
    /// it or skip; returns <see cref="ArchiveOutcome.Overwritten"/> or <see cref="ArchiveOutcome.Skipped"/>
    /// accordingly. Otherwise returns <see cref="ArchiveOutcome.Added"/>.
    /// </summary>
    public static ArchiveOutcome AppendToArchive(
        string historyDir, string className, string csvPath, OverwritePrompt prompt)
    {
        var date        = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var entryName   = $"{date}-{className}-report.csv";
        var archivePath = Path.Combine(historyDir, $"{className}.tar.gz");
        var csvBytes    = File.ReadAllBytes(csvPath);

        // Read all existing entries (if archive exists).
        var existing  = new List<(string Name, byte[] Data)>();
        var overwrote = false;

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
                    // Same-date entry already archived — ask whether to replace it. Skipping it here
                    // drops the old copy so the new CSV takes its place when we write below.
                    if (!prompt.ShouldOverwrite(entryName, $"{className}.tar.gz"))
                        return ArchiveOutcome.Skipped;

                    overwrote = true;
                    continue;
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

        return overwrote ? ArchiveOutcome.Overwritten : ArchiveOutcome.Added;
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
