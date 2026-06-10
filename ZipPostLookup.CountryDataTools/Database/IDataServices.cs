using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Database;

public interface IDataServices
{
    /// <summary>
    /// Retrieves records from the data schema.
    /// Associated CommonQueries: GetCountryInfoById, GetAllCountryInfo, HasReferenceData, GetAdminLevels, GetAdminLevelIdByLevel,
    /// GetReferenceByCode, GetReferenceByCodeAndName, GetReferenceForImportBatch, GetReferenceStateByCodes, GetAllReferenceDataWithAdmins,
    /// CountReferenceByCode, GetDistinctCodeCount, GetCuratedReferenceCount, GetReferenceAdmin, ExportReferenceData,
    /// ExportReferenceDataCuratedOnly, ExportReferenceDataWithCuration, ExportReferenceDataCuratedOnlyWithCuration,
    /// GetTestDataSet, GetNonDefaultTestDataSet
    /// </summary>
    Task<List<IDataSchema>> RetrieveDataRecordsAsync(
        string commonQuery,
        string countryId = "",
        string countryName = "",
        bool hasPostalCodes = false,
        string codeRegex = "",
        string constrainedRegex = "",
        string constraintNotes = "",
        bool dataCurated = false,
        string curationStatus = "",
        string notes = "",
        string code = "",
        string name = "",
        long referenceId = 0,
        int adminLevelId = 0,
        int count = 0);
    
    /// <summary>
    /// Retrieves DataReferenceAdmin records from the data schema.
    /// Convenience typed accessor when the concrete return type is known.
    /// Associated CommonQueries: GetReferenceAdmin.
    /// </summary>
    Task<List<DataReferenceAdmin>> RetrieveValidationCodesAsync(string commonQuery);

    /// <summary>
    /// Bulk-merges records into the data schema via Dapper Plus BulkMergeAsync.
    /// List items must be concrete Dbo types (DataCountryInfo, DataReference, etc.).
    /// Single-item saves: wrap in a list — MergeDataRecordsAsync([myRecord]).
    /// Associated CommonQueries: UpsertCountryInfo, UpdateCountryCuration, UpdateReferenceNameChecked,
    /// UpdateReferenceTimezoneBatch, UpdateReferenceNameCheckedBatch.
    /// </summary>
    Task<bool> MergeDataRecordsAsync(List<IDataSchema> dataRecords);
}