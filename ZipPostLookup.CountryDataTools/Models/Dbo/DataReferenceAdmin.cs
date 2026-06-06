using System.Text;
using Dapper.Contrib.Extensions;
using ZipPostLookup.Core;

namespace ZipPostLookup.CountryDataTools.Models.Dbo;

[Table("data.ReferenceAdmins")]
public class DataReferenceAdmin : IDataSchema
{
    public DataReferenceAdmin() { }

    public DataReferenceAdmin(int level, AdminLevel adminLevel)
    {
        AdminLevelId = level;
        Value = adminLevel.Value;
        Code = adminLevel.Code;
    }
    
    [Key] public long ReferenceAdminId { get; set; }
    public long ReferenceId { get; set; }
    public int AdminLevelId { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
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