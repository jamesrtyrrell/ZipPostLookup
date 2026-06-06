using System.Text.Json.Serialization;

namespace ZipPostLookup.CountryDataTools.Models.Json;

// =============================================================================
// Root document — {cc}_info.json
// =============================================================================

/// <summary>
/// Deserialisation POCO for <c>{cc}_info.json</c> embedded country metadata files
/// (e.g. <c>us_info.json</c>, <c>ca_info.json</c>).
/// 
/// JSON keys are PascalCase;<see>
///     <cref>JsonPropertyName</cref>
/// </see>
/// attributes map each
/// property to its exact JSON key so <c>System.Text.Json</c> can round-trip the
/// file without a custom naming policy.
/// </summary>
public sealed class CountryInfoJson
{
    [JsonPropertyName("Country")]
    public string Country { get; set; } = string.Empty;
 
    [JsonPropertyName("Iso3")]
    public string? Iso3 { get; set; }
 
    [JsonPropertyName("Continent")]
    public string? Continent { get; set; }
 
    [JsonPropertyName("Region")]
    public string? Region { get; set; }
 
    [JsonPropertyName("Languages")]
    public List<CountryLanguageJson>? Languages { get; set; }
 
    [JsonPropertyName("NameLabel")]
    public string? NameLabel { get; set; }
 
    [JsonPropertyName("CodeCount")]
    public int CodeCount { get; set; }
 
    [JsonPropertyName("CodeRanges")]
    public bool CodeRanges { get; set; }
 
    [JsonPropertyName("Curated")]
    public bool Curated { get; set; }
 
    [JsonPropertyName("CurationStatus")]
    public string CurationStatus { get; set; } = nameof(Enums.CurationStatus.NoData);
 
    [JsonPropertyName("LastUpdated")]
    public string? LastUpdated { get; set; }
 
    [JsonPropertyName("CodeFormat")]
    public string? CodeFormat { get; set; }
 
    [JsonPropertyName("CodeRegex")]
    public string? CodeRegex { get; set; }
 
    [JsonPropertyName("Description")]
    public string? Description { get; set; }
 
    [JsonPropertyName("Source")]
    public string? Source { get; set; }
 
    [JsonPropertyName("AdminLevels")]
    public List<AdminLevelJson> AdminLevels { get; set; } = [];
 
    [JsonPropertyName("Divisions")]
    public List<DivisionJson> Divisions { get; set; } = [];
}