using System.Text;
using Dapper.Contrib.Extensions;
using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Models.Dbo;

[Table("codes.Candidate")]
public class CodesCandidate : ICodesSchema
{
    public CodesCandidate()
    {
        // dapper requires an empty constructor
    }
    
    public CodesCandidate(
        string countryCode,
        CsvRow entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);
        ArgumentNullException.ThrowIfNull(entry);

        CountryId = countryCode.ToUpperInvariant();
        ZpCode = entry.ZpCode!;
        PlaceName = entry.PlaceName!;
        Timezone = entry.Timezone ?? "---";
        Lat = entry.Lat ?? "---";
        Lng = entry.Lng ?? "---";
        IsDefault = bool.TryParse(entry.IsDefault, out var isDefault) && isDefault;
        Status = nameof(CandidateStatus.Pending);
        AdminCandidateList = new List<CodesCandidateAdmin>();
        if (!string.IsNullOrWhiteSpace(entry.Admin1) && !string.IsNullOrWhiteSpace(entry.Admin1Code))
        {
            var codeCandidateAdmin = new CodesCandidateAdmin(1, entry.Admin1, entry.Admin1Code);
            AdminCandidateList.Add(codeCandidateAdmin);
            Admin1     = entry.Admin1;
            Admin1Code = entry.Admin1Code;
        }
    }

    [Key] public long CandidateId { get; set; }
    public string CountryId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public int? RecordNumber { get; set; }
    public string ZpCode { get; set; } = string.Empty;
    public string PlaceName { get; set; } = string.Empty;
    public string Timezone { get; set; } = "---";
    public bool IsDefault { get; set; }
    public string Lat { get; set; } = "---";
    public string Lng { get; set; } = "---";
    [Write(false)]public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    [Write(false)] public List<CodesCandidateAdmin> AdminCandidateList { get; set; } = new();

    /// <summary>Admin level 1 value. Set by Dapper from JOIN results; populated from AdminCandidateList by the CsvRow constructor.</summary>
    [Write(false)] public string Admin1 { get; set; } = "";

    /// <summary>Admin level 1 code. Set by Dapper from JOIN results; populated from AdminCandidateList by the CsvRow constructor.</summary>
    [Write(false)] public string Admin1Code { get; set; } = "";

    // Flat admin levels 2–5 (name + code). GeoNames carries up to five administrative
    // levels; these mirror Admin1/Admin1Code so the column-mapping widget can bind each
    // level by column. Fanned into AdminCandidateList by CodesCandidateExtension.BuildAdminLevels().
    [Write(false)] public string Admin2 { get; set; } = "";
    [Write(false)] public string Admin2Code { get; set; } = "";
    [Write(false)] public string Admin3 { get; set; } = "";
    [Write(false)] public string Admin3Code { get; set; } = "";
    [Write(false)] public string Admin4 { get; set; } = "";
    [Write(false)] public string Admin4Code { get; set; } = "";
    [Write(false)] public string Admin5 { get; set; } = "";
    [Write(false)] public string Admin5Code { get; set; } = "";

    public override string ToString()
    {
        return GetType().GetProperties()
            .Select(info => (info.Name, Value: info.GetValue(this, null) ?? "(null)"))
            .Aggregate(
                new StringBuilder(),
                (sb, pair) => sb.AppendLine($"{pair.Name}: {pair.Value}"),
                sb => sb.ToString());
    }

    /// <summary>
    /// Remaps every admin entry's level number (1..5) to the country's real
    /// <c>codes.admin_levels.AdminLevelId</c> FK. Returns false (so the caller skips the
    /// candidate) if any level isn't defined for the country yet. Must process ALL levels —
    /// the previous version returned after the first, which silently left levels 2+ unmapped.
    /// </summary>
    public bool RemapCandidatesList(Dictionary<int, int> adminLevelMap)
    {
        foreach (var admin in AdminCandidateList)
        {
            if (!adminLevelMap.TryGetValue(admin.AdminLevelId, out var resolvedAdminLevelId))
            {
                return false;
            }

            admin.AdminLevelId = resolvedAdminLevelId;
        }

        return true;
    }
}