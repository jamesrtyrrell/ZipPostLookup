namespace ZipPostLookup.CountryDataTools.Models.Dsv;

/// <summary>
/// Represents the full source-of-truth reference row in CSV format.
/// Written to ZipPostLookup.CountryDataTools/Data/{cc}/{cc}.csv by
/// <c>export --target ref</c>.
///
/// This format preserves all curation metadata (TimezoneChecked, NameChecked)
/// so the file can be re-imported without losing verified state.
/// It is the authoritative record for a country and the input for the
/// optimised export pipeline that produces ZipPostLookupDataCSV.
/// </summary>
public class ReferenceDataCSV : ICsvDsvModel
{
    [CsvHelper.Configuration.Attributes.Name("ZpCode", "Code")]
    public string ZpCode          { get; set; } = string.Empty;
    [CsvHelper.Configuration.Attributes.Name("PlaceName", "Name")]
    public string PlaceName       { get; set; } = string.Empty;
    public string Timezone        { get; set; } = "---";

    // ICsvDsvModel explicit interface — map renamed properties to the contract
    string ICsvDsvModel.Code { get => ZpCode;    set => ZpCode    = value; }
    string ICsvDsvModel.Name { get => PlaceName; set => PlaceName = value; }
    public bool   IsDefault       { get; set; } = false;
    public string Lat             { get; set; } = "---";
    public string Lng             { get; set; } = "---";
    public string Admin1          { get; set; } = "---";
    public string Admin1Code      { get; set; } = "---";
    public bool   TimezoneChecked { get; set; } = false;
    public bool   NameChecked     { get; set; } = false;

    // Additional administrative divisions — populated when source data provides them.
    public string? Admin2     { get; set; }
    public string? Admin2Code { get; set; }
    public string? Admin3     { get; set; }
    public string? Admin3Code { get; set; }
    public string? Admin4     { get; set; }
    public string? Admin4Code { get; set; }
    public string? Admin5     { get; set; }
    public string? Admin5Code { get; set; }
}