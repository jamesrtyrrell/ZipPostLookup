namespace ZipPostLookup.CountryDataTools.Models.Dbo;

/// <summary>
/// Lightweight Dapper read projection for <c>data.Reference</c>.
/// Used where a full <see cref="DataReference"/> entity (Dapper.Contrib write entity)
/// is not appropriate — e.g. the import candidate comparison batch query.
/// Column names match <c>data.Reference</c> exactly.
/// </summary>
public sealed class ReferenceReadRow
{
    public long   ReferenceId     { get; set; }
    public string ZpCode          { get; set; } = "";
    public string PlaceName       { get; set; } = "";
    public string Timezone        { get; set; } = "";
    public bool   IsDefault       { get; set; }
    public bool   TimezoneChecked { get; set; }
    public string  Lat        { get; set; } = "---";
    public string  Lng        { get; set; } = "---";
    public string? AltNameOf  { get; set; }
}
