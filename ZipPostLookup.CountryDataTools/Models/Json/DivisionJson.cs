using System.Text.Json.Serialization;

namespace ZipPostLookup.CountryDataTools.Models.Json;

public sealed class DivisionJson
{
    [JsonPropertyName("Type")]
    public string? Type { get; set; }
 
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;
 
    [JsonPropertyName("LocalName")]
    public string? LocalName { get; set; }
 
    [JsonPropertyName("Code")]
    public string? Code { get; set; }
 
    [JsonPropertyName("Ansi")]
    public string? Ansi { get; set; }
 
    [JsonPropertyName("Capital")]
    public string? Capital { get; set; }
 
    [JsonPropertyName("CapitalLocal")]
    public string? CapitalLocal { get; set; }
 
    [JsonPropertyName("ZipCount")]
    public int ZipCount { get; set; }
 
    [JsonPropertyName("NameCount")]
    public int NameCount { get; set; }
 
    [JsonPropertyName("Notes")]
    public string? Notes { get; set; }
 
    /// <summary>Sub-islands for entries like U.S. Minor Outlying Islands.</summary>
    [JsonPropertyName("Islands")]
    public List<IslandJson>? Islands { get; set; }
}