using Dapper.Contrib.Extensions;

namespace ZipPostLookup.CountryDataTools.Models.Dbo;

[Table("data.ApiUsage")]
public sealed class DataApiUsage
{
    public string         ApiName    { get; set; } = "";
    public DateOnly       UsageDate  { get; set; }
    public int            CallCount  { get; set; }
    public int?            DailyLimit   { get; set; }
    public int?            MonthlyLimit { get; set; }
    public DateTimeOffset? ResetTime    { get; set; }
    public DateTimeOffset  UpdatedAt    { get; set; } = DateTimeOffset.UtcNow;
}
