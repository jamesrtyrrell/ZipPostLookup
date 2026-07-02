using System.Text;
using Dapper.Contrib.Extensions;
using ZipPostLookup.CountryDataTools.Models.Enums;
using ZipPostLookup.CountryDataTools.Models.Json;

namespace ZipPostLookup.CountryDataTools.Models.Dbo;

[Table("data.CountryInfo")]
public class DataCountryInfo : IDataSchema
{
    // Parameterless constructor required by Dapper
    public DataCountryInfo() { }

    public DataCountryInfo(CountriesJson countryInfo)
    {
        CountryId = countryInfo.CountryId;
        CountryName = countryInfo.CountryName;
        CodeRegex = countryInfo.CodeRegex;
        ConstrainedRegex = countryInfo.ConstrainedRegex;
        ConstraintRegex = countryInfo.ConstrainedRegex;
        CurationStatus = countryInfo.CurationStatus;
        Notes = countryInfo.Notes;
        DataCurated = countryInfo.DataCurated;
    }
    
    [ExplicitKey] public string CountryId { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool HasPostalCodes { get; set; }
    public string? CodeRegex { get; set; }
    public string? ConstrainedRegex { get; set; }
    public string? ConstraintRegex { get; set; }
    public int CodeCount { get; set; }
    public bool DataCurated { get; set; }
    public CurationStatus CurationStatus { get; set; } = CurationStatus.NoData;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

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