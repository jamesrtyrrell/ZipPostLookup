using System.Text;
using Dapper.Contrib.Extensions;

namespace ZipPostLookup.CountryDataTools.Models.Dbo;

[Table("codes.Discrepancies")]
public class CodesDiscrepancies : ICodesSchema
{
    public CodesDiscrepancies()
    {
        // Dapper requires empty constructor
    }
    
    [Key] public long discrepancyId { get; set; }
    public string CountryId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string ZpCode { get; set; } = string.Empty;
    public string PlaceName { get; set; } = string.Empty;
    public int? AdminLevelId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string? RefValue { get; set; }
    public string? InValue { get; set; }
    public string? Notes { get; set; }
    public string? OverrideValue { get; set; }
    public bool? AcceptIncoming { get; set; }
    public bool Process { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    
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