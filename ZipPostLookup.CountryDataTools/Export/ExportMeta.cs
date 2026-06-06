namespace ZipPostLookup.CountryDataTools.Export;

/// <summary>
/// Immutable metadata produced by the export pipeline describing what
/// optimisations were applied.  Written into the <c>#meta:</c> header
/// of the output CSV so that <c>BuiltInDataSource</c> can decode it.
/// </summary>
internal sealed record ExportMeta
{
    /// <summary>Whether lat/lng columns are included in the output CSV.</summary>
    public bool IncludeCoords { get; init; } = true;

    /// <summary>
    /// Timezone index — the ordered list of IANA timezone strings used as
    /// a lookup table.  Rows store the 0-based integer index instead of
    /// the full string.  <c>null</c> means timezone strings are written verbatim.
    /// </summary>
    public string[]? TimezoneIndex { get; init; }

    /// <summary>
    /// Admin1 index — ordered list of (Code, Name) pairs.  Rows store the
    /// 0-based integer index for both the admin1 name and admin1 code columns.
    /// <c>null</c> means admin values are written verbatim.
    /// </summary>
    public (string Code, string Name)[]? AdminIndex { get; init; }

    /// <summary>
    /// Admin level names — ordered list matching the admin division hierarchy
    /// (e.g. <c>["State"]</c> for US, <c>["Province"]</c> for CA).
    /// Written as the <c>levels=</c> segment of the <c>#meta:</c> header so
    /// <c>BuiltInDataSource</c> can set <see cref="Core.AdminLevel.LevelName"/>
    /// without a separate config file.
    /// <c>null</c> means level names are not embedded — caller falls back to
    /// <c>CountryInfoSource.GetAdminLevelNames</c>.
    /// </summary>
    public string[]? AdminLevelNames { get; init; }

    /// <summary>Whether any indexing was applied.</summary>
    public bool IsIndexed => TimezoneIndex != null || AdminIndex != null;

    /// <summary>
    /// Number of output rows after all pipeline stages (including range compression).
    /// Set by <see cref="ExportPipeline.RunAsync"/> and <see cref="ExportPipeline.Transform"/>.
    /// </summary>
    public int RowCount { get; init; }
}
