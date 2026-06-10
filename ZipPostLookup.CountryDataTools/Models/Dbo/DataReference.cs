using System.Collections;
using System.Text;
using Dapper.Contrib.Extensions;
using ZipPostLookup.Core;

namespace ZipPostLookup.CountryDataTools.Models.Dbo;

[Table("data.Reference")]
public class DataReference : IDataSchema
{
    public DataReference() { }

    /// <summary>
    /// Constructs a reference row from a <see cref="CodeEntry"/>.
    ///
    /// <paramref name="adminLevelIds"/> maps LevelNumber (1, 2, 3…) to the
    /// corresponding <c>data.admin_levels.AdminLevelId</c> primary key so
    /// that <c>data.reference_admins</c> rows carry the correct FK.
    /// Pass an empty dictionary when no admin level seed data is available
    /// (falls back to storing LevelNumber directly).
    /// </summary>
    public DataReference(
        string countryCode,
        CodeEntry entry,
        bool isCurated,
        IReadOnlyDictionary<int, int>? adminLevelIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        ArgumentNullException.ThrowIfNull(entry);

        CountryId = countryCode.ToUpperInvariant();
        ZpCode = entry.ZpCode;
        PlaceName = entry.PlaceName;
        Timezone = entry.Timezone;
        Lat = "---";
        Lng = "---";
        IsDefault = entry.IsDefault;
        TimezoneChecked = isCurated;
        NameChecked = isCurated;
        AdminReferenceList = new List<DataReferenceAdmin>();
        Curated = false;

        var levelNumber = 1;
        foreach (var adminLevel in entry.Admins)
        {
            // Resolve the actual AdminLevelId PK from data.admin_levels.
            // Falls back to the level number if not found (maintains backward
            // compatibility when admin_levels hasn't been seeded yet).
            var adminLevelId = adminLevelIds != null &&
                               adminLevelIds.TryGetValue(levelNumber, out var id)
                ? id
                : levelNumber;

            AdminReferenceList.Add(new DataReferenceAdmin(adminLevelId, adminLevel));
            levelNumber++;
        }

        var level1 = AdminReferenceList.OrderBy(a => a.AdminLevelId).FirstOrDefault();
        Admin1     = level1?.Value ?? "---";
        Admin1Code = level1?.Code  ?? "---";
    }

    [Key] public long ReferenceId { get; set; }
    public string CountryId { get; set; } = string.Empty;
    public string ZpCode { get; set; } = string.Empty;
    public string PlaceName { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public bool IsDefault { get; set; } = true;
    public string Lat { get; set; } = "---";
    public string Lng { get; set; } = "---";
    public bool TimezoneChecked { get; set; }
    public bool NameChecked { get; set; }
    public bool Flagged { get; set; }

    public string? AltNameOf { get; set; }

    [Write(false)] public bool Curated { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [Write(false)] public List<DataReferenceAdmin> AdminReferenceList { get; set; } = new();

    /// <summary>Admin level 1 value (e.g. province name). Set by Dapper from JOIN results; populated from AdminReferenceList by the CodeEntry constructor.</summary>
    [Write(false)] public string Admin1 { get; set; } = "---";

    /// <summary>Admin level 1 code (e.g. "ON"). Set by Dapper from JOIN results; populated from AdminReferenceList by the CodeEntry constructor.</summary>
    [Write(false)] public string Admin1Code { get; set; } = "---";

    /// <summary>Admin level 2 value — set by Dapper from extended JOIN queries (GetCodeDetailRows). Not populated by the CodeEntry constructor.</summary>
    [Write(false)] public string Admin2 { get; set; } = "---";

    /// <summary>Admin level 2 code — set by Dapper from extended JOIN queries (GetCodeDetailRows). Not populated by the CodeEntry constructor.</summary>
    [Write(false)] public string Admin2Code { get; set; } = "---";

    /// <summary>Gold certification flag — set by Dapper from EXISTS subquery (GetCodeDetailRows). Not written to DB.</summary>
    [Write(false)] public bool IsGold { get; set; }
    
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var property in GetType().GetProperties())
        {
            var value = property.GetValue(this, null);
            if (value is null) { sb.AppendLine($"{property.Name}: (null)"); continue; }
            if (value is string) { sb.AppendLine($"{property.Name}: {value}"); continue; }
            if (value is IEnumerable _) { continue; }
            sb.AppendLine($"{property.Name}: {value}");
        }
        foreach (var admin in AdminReferenceList)
        {
            sb.AppendLine($"{admin}");
        }
        return sb.ToString();
    }
}