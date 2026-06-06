using System.IO.Compression;

namespace ZipPostLookup.CountryDataTools.Export.ZPimage;

/// <summary>
/// Writes a <see cref="ZpImageBuilder">ZP frozen image</see> to disk — the binary
/// counterpart of <see cref="CsvExporter"/>.
///
/// <para>By default the raw image is wrapped in a Brotli stream (<c>.zpi.br</c>): the format
/// is built for a load path that decompresses once into a single <c>byte[]</c> and then reads
/// fields zero-copy, so the on-disk artefact should be as small as possible. Pass
/// <c>compress: false</c> to emit the uncompressed <c>.zpi</c> (useful for memory-mapping or
/// inspection).</para>
///
/// <para>This writer does not touch the existing CSV pipeline — it is an additional,
/// parallel output that callers opt into.</para>
/// </summary>
internal static class ZpImageWriter
{
    /// <summary>
    /// Builds and writes the image for <paramref name="countryCode"/> from <paramref name="rows"/>.
    /// </summary>
    /// <param name="countryCode">ISO country code (e.g. "US"); stored in the image header.</param>
    /// <param name="rows">The export rows (post-pipeline) to encode.</param>
    /// <param name="outputPath">
    /// Destination path. When <paramref name="compress"/> is <c>true</c> a <c>.br</c> suffix is
    /// appended if not already present.
    /// </param>
    /// <param name="meta">
    /// Optional pipeline metadata; when supplied its timezone/admin1 index tables are reused so
    /// the image stays byte-consistent with the CSV export. When <c>null</c> the tables are
    /// derived from the rows.
    /// </param>
    /// <param name="compress">Whether to Brotli-compress the image (default <c>true</c>).</param>
    public static async Task<ZpImageBuildResult> WriteAsync(
        string countryCode,
        IReadOnlyList<ExportRow> rows,
        string outputPath,
        ExportMeta? meta = null,
        bool compress = true)
    {
        var result = ZpImageBuilder.Build(countryCode, rows, meta);

        // Compute the output path: strip any trailing .zpi/.br and insert the nameIndex-width
        // qualifier (.u16 or .u32) so readers can determine the record stride from the filename.
        var indexSuffix = result.UsedU32NameIndex ? "u32" : "u16";
        var baseName = outputPath;
        foreach (var ext in new[] { ".br", ".zpi" })
        {
            if (baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                baseName = baseName[..^ext.Length];
        }
        var path = compress
            ? $"{baseName}.{indexSuffix}.zpi.br"
            : $"{baseName}.{indexSuffix}.zpi";

        result = result with { OutputPath = path };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        await using var file = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 1 << 16, useAsync: true);

        long onDisk;
        if (compress)
        {
            // Prepend 4-byte little-endian uncompressed size so the reader can pre-allocate
            // exactly the right buffer without a MemoryStream (see ZpImageReader.LoadCompressed).
            var sizePrefix = BitConverter.GetBytes((uint)result.Bytes.Length);
            await file.WriteAsync(sizePrefix);

            await using var brotli = new BrotliStream(file, CompressionLevel.SmallestSize, leaveOpen: true);
            await brotli.WriteAsync(result.Bytes);
            await brotli.FlushAsync();
            // BrotliStream must be disposed to flush its trailer before we read the length.
            await brotli.DisposeAsync();
            onDisk = file.Length;
        }
        else
        {
            await file.WriteAsync(result.Bytes);
            onDisk = result.Bytes.Length;
        }

        Console.WriteLine(
            $"    ZP image: {result.ExactCodes:N0} codes, {result.RecordCount:N0} records, " +
            $"{result.RangeCount:N0} ranges, {result.NameCount:N0} names, " +
            $"{result.TimezoneCount} tz, {result.AdminCount} admin1.");
        Console.WriteLine(
            $"    ZP image: {result.Bytes.Length:N0} B raw → {onDisk:N0} B on disk " +
            $"({(compress ? "Brotli" : "uncompressed")}) → {path}");

        return result;
    }
}
