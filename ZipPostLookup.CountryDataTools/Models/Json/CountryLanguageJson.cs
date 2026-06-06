using System.Text.Json.Serialization;

namespace ZipPostLookup.CountryDataTools.Models.Json;

public sealed class CountryLanguageJson
{
    [JsonPropertyName("Alpha2")]
    public string Alpha2 { get; set; } = string.Empty;
 
    [JsonPropertyName("Alpha3")]
    public string? Alpha3 { get; set; }
 
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;
}