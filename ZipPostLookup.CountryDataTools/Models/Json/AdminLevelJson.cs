using System.Text.Json.Serialization;

namespace ZipPostLookup.CountryDataTools.Models.Json;

public sealed class AdminLevelJson
{
    [JsonPropertyName("Level")]
    public int Level { get; set; }
 
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;
 
    [JsonPropertyName("Aliases")]
    public List<string> Aliases { get; set; } = [];
 
    [JsonPropertyName("LocalName")]
    public string? LocalName { get; set; }
 
    [JsonPropertyName("CodeType")]
    public string? CodeType { get; set; }
	
	[JsonPropertyName("OfficialLanguages")]
	public List<CountryLanguageJson> OfficialLanguages { get; set; } = [];
    
	[JsonPropertyName("MinorityLanguages")]
	public List<CountryLanguageJson> MinorityLanguages { get; set; } = [];

	
}