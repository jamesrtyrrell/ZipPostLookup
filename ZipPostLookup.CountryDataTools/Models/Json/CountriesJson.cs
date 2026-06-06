using System.Text.Json.Serialization;
using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Models.Json;

// =============================================================================
// Single entry in countries.json  (the array root)
// =============================================================================

/// <summary>
/// Deserialisation POCO for a single entry in <c>countries.json</c>.
/// 
/// <c>countries.json</c> is a flat JSON array of these objects, already in
/// camelCase — the <see>
///     <cref>JsonPropertyName</cref>
/// </see>
/// attributes preserve the
/// exact key names used in the file.
/// 
/// Load the whole file with:
/// <code>
/// var countries = JsonSerializer.Deserialize&lt;List&lt;CountriesJson&gt;&gt;(stream, options);
/// </code>
/// </summary>
public sealed class CountriesJson
{
    [JsonPropertyName("CountryId")]
    public string CountryId { get; set; } = string.Empty;

    [JsonPropertyName("CountryName")]
    public string CountryName { get; set; } = string.Empty;

    [JsonPropertyName("HasPostalCodes")]
    public bool HasPostalCodes { get; set; } = true;

    [JsonPropertyName("Status")]
    public string? Status { get; set; }

    [JsonPropertyName("CodeRegex")]
    public string? CodeRegex { get; set; }

    [JsonPropertyName("ConstrainedRegex")]
    public string? ConstrainedRegex { get; set; }

    [JsonPropertyName("ConstraintNotes")]
    public string? ConstraintNotes { get; set; }

    [JsonPropertyName("DataCurated")]
    public bool DataCurated { get; set; }

    [JsonPropertyName("CurationStatus")]
    public CurationStatus CurationStatus { get; set; } = CurationStatus.NoData;

    [JsonPropertyName("Notes")]
    public string? Notes { get; set; }
}