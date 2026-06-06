using System.Text.Json.Serialization;

namespace ZipPostLookup.CountryDataTools.Models.Json;

public sealed class IslandJson
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;
 
    [JsonPropertyName("Ansi")]
    public string? Ansi { get; set; }
}