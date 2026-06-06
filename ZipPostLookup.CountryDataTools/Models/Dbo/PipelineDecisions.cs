using System.Text;
using Dapper.Contrib.Extensions;

namespace ZipPostLookup.CountryDataTools.Models.Dbo;

[Table("pipeline.Decisions")]
public class PipelineDecisions : IPipelineSchema
{
    public PipelineDecisions() { }
    
    [Key] public long DecisionId { get; set; }
    public string CountryId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string ZpCode { get; set; } = string.Empty;
    public string PlaceName { get; set; } = string.Empty;
    public bool AcceptIncoming { get; set; }
    public required string DecidedBy { get; set; }
    public required string Notes { get; set; }
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