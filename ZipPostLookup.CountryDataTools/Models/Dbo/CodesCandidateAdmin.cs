using System.Text;
using Dapper.Contrib.Extensions;

namespace ZipPostLookup.CountryDataTools.Models.Dbo;

[Table("codes.CandidateAdmins")]
public class CodesCandidateAdmin : ICodesSchema
{
    public CodesCandidateAdmin() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodesCandidateAdmin"/> class.
    /// </summary>
    /// <param name="adminLevelId">The resolved <c>codes.admin_levels.AdminLevelId</c> FK value.</param>
    /// <param name="value">Human-readable admin level value (e.g. "California").</param>
    /// <param name="code">Short code for the admin level (e.g. "CA").</param>
    public CodesCandidateAdmin(int adminLevelId, string value, string code)
    {
        AdminLevelId = adminLevelId;
        Value = value;
        Code = code;
    }

    [Key] public long CandidateAdminId { get; set; }
    public long CandidateId { get; set; }
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