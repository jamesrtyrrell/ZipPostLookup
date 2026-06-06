namespace ZipPostLookup.CountryDataTools.Export.Stages;

/// <summary>
/// A single stage in the export pipeline.
/// Each stage receives the current row list and metadata, and returns
/// a (possibly transformed) row list and updated metadata.
/// Stages are applied in order by <see cref="ExportPipeline"/>.
/// </summary>
internal interface IExportStage
{
    string StageName { get; }

    (List<ExportRow> Rows, ExportMeta Meta) Apply(List<ExportRow> rows, ExportMeta meta);
}
