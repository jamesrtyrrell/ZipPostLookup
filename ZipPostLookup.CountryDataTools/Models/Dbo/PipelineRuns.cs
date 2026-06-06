using System.Text;
using Dapper.Contrib.Extensions;

namespace ZipPostLookup.CountryDataTools.Models.Dbo;

[Table("pipeline.Runs")]
public class PipelineRuns : IPipelineSchema
{
    public PipelineRuns() { 
        //required for dapper
    }

    [ExplicitKey] public string RunId { get; set; } = string.Empty;
    public string CountryId { get; set; } = string.Empty;
    public string SourceFilename { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Notes { get; set; }
    
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