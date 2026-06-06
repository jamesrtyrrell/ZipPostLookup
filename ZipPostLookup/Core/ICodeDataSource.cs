namespace ZipPostLookup.Core;

/// <summary>
/// Represents a source of postal code data that can be loaded into a <see cref="ZipPostRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// The library ships with a built-in implementation (<c>BuiltInDataSource</c>) that reads
/// embedded CSV files for each supported country. Implement this interface to supply your own
/// data — for example, military APO/FPO codes, custom regional data, or a database-backed source.
/// </para>
/// <para>
/// Custom sources can be merged with built-in data using
/// <see cref="ZipPostLookup.CreateCustomRegistry"/>:
/// </para>
/// <code>
/// var registry = ZipPostLookup.CreateCustomRegistry(new MyMilitaryZipSource());
/// var entry = registry.GetByZip("09001");
/// </code>
/// <para>
/// When multiple sources are loaded into a registry, entries from later sources are appended
/// after entries from earlier sources for the same postal code. If a later source provides an
/// entry with <see cref="CodeEntry.IsDefault"/> set to <c>true</c> for a code that already has
/// a default, the later entry takes precedence as the new default.
/// </para>
/// <para>
/// Implementations should be stateless and repeatable — <see cref="GetEntries"/> may be called
/// more than once.
/// </para>
/// </remarks>
public interface ICodeDataSource
{
    /// <summary>
    /// A short identifier for this data source, used in diagnostic messages and logging.
    /// </summary>
    /// <remarks>
    /// Should be lowercase, hyphenated, and descriptive — e.g. <c>"built-in-us"</c>,
    /// <c>"military-apo"</c>, or <c>"custom-po-boxes"</c>.
    /// </remarks>
    string SourceName { get; }

    /// <summary>
    /// Returns all postal code entries provided by this source.
    /// </summary>
    /// <returns>
    /// A sequence of <see cref="CodeEntry"/> records. The sequence may be lazily evaluated
    /// (e.g. streamed from a file), so callers should not assume it can be enumerated more
    /// than once without reconstructing the source.
    /// </returns>
    IEnumerable<CodeEntry> GetEntries();
}