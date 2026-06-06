using System.Text;
using System.Text.Json;
using Dapper.Contrib.Extensions;
using ZipPostLookup.CountryDataTools.Models.Json;

namespace ZipPostLookup.CountryDataTools.Models.Dbo;

[Table("data.AdminLevels")]
public class DataAdminLevel : IDataSchema
{
    public DataAdminLevel() { }

    public DataAdminLevel(AdminLevelJson level, string cc)
    {
        // store the json array in a string
        var aliases = JsonSerializer.Serialize(level.Aliases);
        CountryId = cc.ToUpperInvariant();
        LevelNumber = level.Level;
        LevelName = level.Name ?? string.Empty;
        CodeType = level.CodeType ?? string.Empty;
        Aliases = aliases;
        CreatedAt = DateTime.UtcNow;

    }
    
    [Key] public int AdminLevelId { get; set; }
    public string CountryId { get; set; } = string.Empty;
    public int LevelNumber { get; set; }
    public string LevelName { get; set; } = string.Empty;
    public string? CodeType { get; set; }
    public string? Aliases { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    
    public override string ToString()
    {
        return GetType().GetProperties()
            .Select(info => (info.Name, Value: info.GetValue(this, null) ?? "(null)"))
            .Aggregate(
                new StringBuilder(),
                (sb, pair) => sb.AppendLine($"{pair.Name}: {pair.Value}"),
                sb => sb.ToString());
    }
}