namespace ZipPostLookup.CountryDataTools.Models.Dsv;

/// <summary>
/// Minimum contract shared by all internal CSV format models:
/// <see cref="CandidateDataCSV"/>, <see cref="ReferenceDataCSV"/>,
/// <see cref="ZipPostLookupDataCSV"/>.
/// These three properties are non-nullable in every CSV model,
/// making them safe for generic code paths.
/// </summary>
public interface ICsvDsvModel : IDsvModel
{
    string Code     { get; set; }
    string Name     { get; set; }
    string Timezone { get; set; }
}
