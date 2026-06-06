namespace ZipPostLookup.CountryDataTools.Commands;

/// <summary>
/// CountryDataTools export --country XX [--target ref|main|zpi] [--output path/to/file] [--curated-only] [--uncompressed]
///
/// Three export targets:
///
///   --target ref   Full ReferenceDataCSV written to CountryDataTools/Data/{cc}/{cc}.csv.
///                  Preserves all curation flags (TimezoneChecked, NameChecked).
///                  Commit this file after every curation session — it is the source
///                  of truth and the input for 'ingest ref' if the DB needs rebuilding.
///
///   --target main  (default) Optimised ZipPostLookupDataCSV written to
///                  ZipPostLookup/Data/{cc}/{cc}.csv, ready for embedding.
///                  For US, CA, and MX the full export pipeline is applied:
///                  lat/lng stripped, ranges collapsed, timezone/admin1 indexed.
///
///   --target zpi   The same optimised data as a ZP frozen image (binary minimal-perfect-hash
///                  blob) written to ZipPostLookup/Data/{cc}/{cc}.zpi.br. Designed for a
///                  zero-parse, zero-allocation load path. --uncompressed writes raw {cc}.zpi.
/// </summary>
public static class ExportCommand
{
    public static async Task<int> RunAsync(string[] args) =>
        await Handlers.ExportReferenceCommand.RunAsync(args);
}