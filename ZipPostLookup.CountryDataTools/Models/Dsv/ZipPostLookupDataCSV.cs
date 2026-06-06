namespace ZipPostLookup.CountryDataTools.Models.Dsv;

/// <summary>
/// The optimised CSV format written to ZipPostLookup/Data/{cc}/{cc}.csv by
/// <c>export --target main</c>.
///
/// This is a compacted, pipeline-processed subset of ReferenceDataCSV — lat/lng
/// and curation metadata are stripped, timezone and admin1 strings are replaced
/// with integer indices, and eligible code ranges are collapsed to range rows.
/// It is consumed directly by ZipPostLookup.Sources.BuiltInDataSource.
/// </summary>
public class ZipPostLookupDataCSV : ICsvDsvModel
{
    public string ZpCode     { get; set; } = string.Empty;
    public string PlaceName  { get; set; } = string.Empty;
    public string Timezone   { get; set; } = "---";
    public bool   IsDefault  { get; set; } = false;
    public string Admin1     { get; set; } = "---";
    public string Admin1Code { get; set; } = "---";

    // ICsvDsvModel explicit interface — map renamed properties to the contract
    string ICsvDsvModel.Code { get => ZpCode;    set => ZpCode    = value; }
    string ICsvDsvModel.Name { get => PlaceName; set => PlaceName = value; }
}