using System.Formats.Tar;
using System.IO.Compression;

namespace ZipPostLookup.CountryDataTools.Dsv;

/// <summary>
/// Minimal gzip-compressed tar (<c>.tar.gz</c>) helper for the single-CSV reference archives.
///
/// <para>The CountryDataTools source-of-truth reference CSVs (<c>Data/{cc}/{cc}.csv</c>) are large
/// — CA is ~120&#160;MB raw, over GitHub's 100&#160;MB file limit. They are stored compressed as
/// <c>{cc}.csv.tar.gz</c> (~9&#160;MB for CA) and expanded on read. The format is a standard tar
/// stream (so plain <c>tar&#160;-xvzf {cc}.csv.tar.gz</c> works on any machine) wrapped in gzip,
/// produced and consumed here with the built-in <see cref="System.Formats.Tar"/> +
/// <see cref="GZipStream"/> APIs (no external dependency).</para>
///
/// <para>Each archive holds exactly one regular-file entry — the <c>{cc}.csv</c> it was built from.</para>
/// </summary>
internal static class TarGzArchive
{
    /// <summary>File-name suffix of a compressed reference archive.</summary>
    public const string Suffix = ".csv.tar.gz";

    /// <summary>
    /// Writes <paramref name="content"/> as a single tar entry named <paramref name="entryName"/>
    /// into a gzip-compressed tar at <paramref name="outputPath"/> (created/overwritten).
    /// </summary>
    public static async Task WriteSingleFileAsync(string outputPath, string entryName, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        await using var file = new FileStream(
            outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 1 << 16, useAsync: true);
        await using var gzip = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true);
        await using var tar  = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true);

        var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
        {
            DataStream = new MemoryStream(content, writable: false),
        };

        await tar.WriteEntryAsync(entry);
    }

    /// <summary>
    /// Decompresses a <c>.tar.gz</c> stream and returns the bytes of its single <c>.csv</c> entry —
    /// the in-process equivalent of <c>tar -xvzf</c>. The input stream is read from its current
    /// position; the caller owns its lifetime.
    /// </summary>
    /// <exception cref="InvalidDataException">No <c>.csv</c> entry was found in the archive.</exception>
    public static byte[] ExtractSingleCsv(Stream tarGz)
    {
        using var gzip = new GZipStream(tarGz, CompressionMode.Decompress, leaveOpen: true);
        using var tar  = new TarReader(gzip, leaveOpen: true);

        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile &&
                entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) &&
                entry.DataStream is { } data)
            {
                using var ms = new MemoryStream();
                data.CopyTo(ms);
                return ms.ToArray();
            }
        }

        throw new InvalidDataException("No .csv entry found in the tar.gz archive.");
    }
}
