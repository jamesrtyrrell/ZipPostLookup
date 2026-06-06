namespace ZipPostLookup.CountryDataTools.Export.Stages;

/// <summary>
/// Strips the lat/lng columns from the export unconditionally.
///
/// Coordinates are stored in <c>data.reference</c> as enrichment metadata
/// but are not used by the ZipPostLookup library at runtime — only timezones
/// and admin1 divisions are consumed.  Including them in the embedded CSV
/// would bloat the binary for no benefit.
///
/// Sets <see cref="ExportMeta.IncludeCoords"/> to <c>false</c> — the CSV
/// writer omits the columns entirely and <c>BuiltInDataSource</c> returns
/// <c>null</c> for lat/lng on all entries.
/// </summary>
internal sealed class LatLngStripStage : IExportStage
{
    public string StageName => "LatLng Strip";

    public (List<ExportRow> Rows, ExportMeta Meta) Apply(
        List<ExportRow> rows, ExportMeta meta)
    {
        Console.WriteLine($"    {StageName}: stripping lat/lng columns (not used by ZipPostLookup).");
        return (rows, meta with { IncludeCoords = false });
    }
}