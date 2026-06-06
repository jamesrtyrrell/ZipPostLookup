namespace ZipPostLookup.CountryDataTools.Models.Dsv;

// what the candidate CSV should look like
public class CandidateDataCSV : ICsvDsvModel
{
    [CsvHelper.Configuration.Attributes.Name("ZpCode", "Code")]
    public string ZpCode { get; set; } = string.Empty;
    [CsvHelper.Configuration.Attributes.Name("PlaceName", "Name")]
    public string PlaceName { get; set; } = string.Empty;
    public string Timezone { get; set; } = "---";

    // ICsvDsvModel explicit interface — map renamed properties to the contract
    string ICsvDsvModel.Code { get => ZpCode;    set => ZpCode    = value; }
    string ICsvDsvModel.Name { get => PlaceName; set => PlaceName = value; }
    public string? Lat { get; set; } = "---";
    public string? Lng { get; set; } = "---";
    public string? Admin1 { get; set; } = "---";
    public string? Admin1Code { get; set; } = "---";
    
    // adding more data is possible with different levels of administrative divisions
    public string? Admin2 { get; set; }
    public string? Admin2Code { get; set; }
    public string? Admin3 { get; set; }
    public string? Admin3Code { get; set; }
    public string? Admin4 { get; set; }
    public string? Admin4Code { get; set; }
    public string? Admin5 { get; set; }
    public string? Admin5Code { get; set; }
}
