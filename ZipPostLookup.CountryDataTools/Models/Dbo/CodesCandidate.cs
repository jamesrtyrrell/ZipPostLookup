using System.Text;
using Dapper.Contrib.Extensions;
using ZipPostLookup.CountryDataTools.DSV;
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
            var codeCandidateAdmin = new CodesCandidateAdmin(1, entry.Admin1  ,entry.Admin1Code);
            AdminCandidateList.Add(codeCandidateAdmin);
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

    /// <summary>Value of the first admin level (e.g. province name).</summary>
    [Write(false)]
    public string Admin1 =>
        AdminCandidateList.OrderBy(a => a.AdminLevelId).FirstOrDefault()?.Value ?? "";

    /// <summary>Code of the first admin level (e.g. "ON").</summary>
    [Write(false)]
    public string Admin1Code =>
        AdminCandidateList.OrderBy(a => a.AdminLevelId).FirstOrDefault()?.Code ?? "";

    
    
    public override string ToString()
    {
        return GetType().GetProperties()
            .Select(info => (info.Name, Value: info.GetValue(this, null) ?? "(null)"))
            .Aggregate(
                new StringBuilder(),
                (sb, pair) => sb.AppendLine($"{pair.Name}: {pair.Value}"),
                sb => sb.ToString());
    }

    public bool RemapCandidatesList(Dictionary<int, int> adminLevelMap)
    {
        foreach (var admin in AdminCandidateList)
        {
            // Remap level number to the real AdminLevelId FK.
            // Skip if the admin level is not defined for this country yet.
            if (!adminLevelMap.TryGetValue(admin.AdminLevelId, out var resolvedAdminLevelId))
            {
                return false;
            }
            
            admin.AdminLevelId = resolvedAdminLevelId;

            return true;
            // await conn.ExecuteAsync(
            //     CommonQueries.InsertCandidateAdmin,
            //     new
            //     {
            //         CandidateId = candidateId,
            //         AdminLevelId = resolvedAdminLevelId,
            //         admin.Value,
            //         admin.Code,
            //     });
        }

        return true;
    }
}