using System.Text;
using Dapper.Contrib.Extensions;

namespace ZipPostLookup.CountryDataTools.Models.Dbo;

[Table("codes.ValidationErrors")]
public class CodesValidationErrors : ICodesSchema
{
    public CodesValidationErrors() { }
    
    [Key] public long ValidationErrorId { get; set; }
    public string CountryId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public int? RecordNumber { get; set; }
    public string? Zip { get; set; }
    public string? City { get; set; }
    public string ErrorType { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
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