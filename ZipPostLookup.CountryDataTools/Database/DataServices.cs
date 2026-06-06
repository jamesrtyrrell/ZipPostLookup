using Microsoft.Data.SqlClient;
using Dapper;
using Z.Dapper.Plus;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Database;

public class DataServices : IDataServices
{
    private readonly IWorkDbConnectionFactory _factory;
    
    public DataServices(IWorkDbConnectionFactory factory)
    {
        _factory = factory;
    }
    
    /// <summary>
    /// Retrieves validation codes (DataReferenceAdmin) from the data schema.
    /// Associated CommonQueries: GetReferenceAdmin
    /// </summary>
    public async Task<List<DataReferenceAdmin>> RetrieveValidationCodesAsync(string commonQuery)
    {
        if (string.IsNullOrEmpty(commonQuery))
        {
            throw new ArgumentNullException(nameof(commonQuery), "Common query cannot be null or empty");
        }

        await using var conn = (SqlConnection)_factory.CreateConnection();
        try
        {
            var result = (await conn.QueryAsync<DataReferenceAdmin>(commonQuery)).ToList();
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving DataReferenceAdmin: {ex.Message}");
        }

        return new List<DataReferenceAdmin>();
    }

    /// <summary>
    /// Retrieves records from the data schema.
    /// Associated CommonQueries: GetCountryInfoById, GetAllCountryInfo, HasReferenceData,
    /// GetAdminLevels, GetAdminLevelIdByLevel, GetReferenceByCode, GetReferenceByCodeAndName,
    /// GetReferenceForImportBatch, GetReferenceStateByCodes, GetAllReferenceDataWithAdmins,
    /// CountReferenceByCode, GetDistinctCodeCount, GetCuratedReferenceCount, GetReferenceAdmin,
    /// ExportReferenceData, ExportReferenceDataCuratedOnly, ExportReferenceDataWithCuration,
    /// ExportReferenceDataCuratedOnlyWithCuration, GetTestDataSet, GetNonDefaultTestDataSet
    /// </summary>
    public async Task<List<IDataSchema>> RetrieveDataRecordsAsync(
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
        int count = 0)
    {
        if (string.IsNullOrEmpty(commonQuery))
        {
            throw new ArgumentNullException(nameof(commonQuery), "Common query cannot be null or empty");
        }

        var parameters = new DynamicParameters();
        parameters.Add("CountryId", countryId);
        parameters.Add("CountryName", countryName);
        parameters.Add("HasPostalCodes", hasPostalCodes);
        parameters.Add("CodeRegex", codeRegex);
        parameters.Add("ConstrainedRegex", constrainedRegex);
        parameters.Add("ConstraintNotes", constraintNotes);
        parameters.Add("DataCurated", dataCurated);
        parameters.Add("CurationStatus", curationStatus);
        parameters.Add("Notes", notes);
        parameters.Add("Code", code);
        parameters.Add("Name", name);
        parameters.Add("ZpCode", code);
        parameters.Add("PlaceName", name);
        parameters.Add("ReferenceId", referenceId);
        parameters.Add("AdminLevelId", adminLevelId);
        parameters.Add("Count", count);

        await using var conn = (SqlConnection)_factory.CreateConnection();
        try
        {
            var result = (await conn.QueryAsync<IDataSchema>(commonQuery, parameters)).ToList();
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving Data records: {ex.Message}");
        }

        return new List<IDataSchema>();
    }

    /// <summary>
    /// Merges/Upserts records into the data schema.
    /// Associated CommonQueries: UpsertCountryInfo, UpdateCountryCuration, UpdateReferenceNameChecked,
    /// UpdateReferenceTimezoneBatch, UpdateReferenceNameCheckedBatch
    /// </summary>
    public async Task<bool> MergeDataRecordsAsync(List<IDataSchema> dataRecords)
    {
        if (dataRecords.Count == 0) { return true; }
        
        await using var conn = (SqlConnection)_factory.CreateConnection();
        try
        {
            await conn.BulkMergeAsync<IDataSchema>(dataRecords);
            return true;
        }
        catch (Exception ex)
        {
            var type = dataRecords.First().GetType();
            Console.WriteLine($"Error merging {type.Name}: {ex.Message}");
        }
        return false;   
    }
}