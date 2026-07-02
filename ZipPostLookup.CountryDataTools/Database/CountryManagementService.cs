using Dapper;
using Microsoft.Data.SqlClient;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Database;

/// <summary>
/// Service for managing country configuration - enabling/disabling countries,
/// bulk initialization from countries.json, and querying country status.
/// </summary>
public class CountryManagementService
{
    private readonly IWorkDbConnectionFactory _factory;

    public CountryManagementService(IWorkDbConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Get all countries from the database.
    /// </summary>
    public async Task<List<DataCountryInfo>> GetAllCountriesAsync()
    {
        await using var conn = (SqlConnection)_factory.CreateConnection();
        var countries = await conn.QueryAsync<DataCountryInfo>(CommonQueries.GetAllCountryInfo);
        return countries.ToList();
    }

    /// <summary>
    /// Get only enabled countries.
    /// </summary>
    public async Task<List<DataCountryInfo>> GetEnabledCountriesAsync()
    {
        await using var conn = (SqlConnection)_factory.CreateConnection();
        var countries = await conn.QueryAsync<DataCountryInfo>(CommonQueries.GetEnabledCountries);
        return countries.ToList();
    }

    /// <summary>
    /// Get a specific country by ID.
    /// </summary>
    public async Task<DataCountryInfo?> GetCountryAsync(string countryId)
    {
        await using var conn = (SqlConnection)_factory.CreateConnection();
        var country = await conn.QueryFirstOrDefaultAsync<DataCountryInfo>(
            CommonQueries.GetCountryInfoById,
            new { CountryId = countryId }
        );
        return country;
    }

    /// <summary>
    /// Enable a country for import/export operations.
    /// </summary>
    public async Task<bool> EnableCountryAsync(string countryId)
    {
        return await SetCountryEnabledAsync(countryId, true);
    }

    /// <summary>
    /// Disable a country for import/export operations.
    /// </summary>
    public async Task<bool> DisableCountryAsync(string countryId)
    {
        return await SetCountryEnabledAsync(countryId, false);
    }

    /// <summary>
    /// Set country enabled status.
    /// </summary>
    private async Task<bool> SetCountryEnabledAsync(string countryId, bool enabled)
    {
        await using var conn = (SqlConnection)_factory.CreateConnection();
        var rows = await conn.ExecuteAsync(
            CommonQueries.UpdateCountryEnabled,
            new { CountryId = countryId, Enabled = enabled }
        );
        return rows > 0;
    }

    /// <summary>
    /// Bulk enable multiple countries.
    /// </summary>
    public async Task<int> BulkEnableCountriesAsync(IEnumerable<string> countryIds)
    {
        var count = 0;
        foreach (var countryId in countryIds)
        {
            if (await EnableCountryAsync(countryId))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Bulk disable multiple countries.
    /// </summary>
    public async Task<int> BulkDisableCountriesAsync(IEnumerable<string> countryIds)
    {
        var count = 0;
        foreach (var countryId in countryIds)
        {
            if (await DisableCountryAsync(countryId))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Initialize all countries from countries.json into the database.
    /// Uses MERGE logic - updates existing, inserts new.
    /// </summary>
    public async Task<(int inserted, int updated)> InitializeFromJsonAsync(string jsonPath)
    {
        var json = await File.ReadAllTextAsync(jsonPath);

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        var countries = System.Text.Json.JsonSerializer.Deserialize<List<CountryJsonModel>>(json, options);

        if (countries == null || countries.Count == 0)
            return (0, 0);

        var inserted = 0;
        var updated = 0;

        await using var conn = (SqlConnection)_factory.CreateConnection();
        foreach (var country in countries)
        {
            var existing = await conn.QueryFirstOrDefaultAsync<DataCountryInfo>(
                CommonQueries.GetCountryInfoById,
                new { CountryId = country.CountryId }
            );

            var dbCountry = new DataCountryInfo
            {
                CountryId = country.CountryId,
                CountryName = country.CountryName,
                Enabled = country.Enabled,
                HasPostalCodes = country.HasPostalCodes,
                CodeRegex = country.CodeRegex,
                ConstrainedRegex = country.ConstrainedRegex,
                ConstraintRegex = country.ConstrainedRegex,
                DataCurated = country.DataCurated,
                CurationStatus = country.CurationStatus,
                Notes = country.Notes
            };

            if (existing == null)
            {
                const string insertQuery = @"
                    INSERT INTO data.CountryInfo
                    (CountryId, CountryName, Enabled, HasPostalCodes, CodeRegex, ConstrainedRegex,
                     DataCurated, CurationStatus, Notes, CreatedAt, UpdatedAt)
                    VALUES
                    (@CountryId, @CountryName, @Enabled, @HasPostalCodes, @CodeRegex, @ConstrainedRegex,
                     @DataCurated, @CurationStatus, @Notes, SYSUTCDATETIME(), SYSUTCDATETIME())";

                var parameters = new
                {
                    dbCountry.CountryId,
                    dbCountry.CountryName,
                    dbCountry.Enabled,
                    dbCountry.HasPostalCodes,
                    dbCountry.CodeRegex,
                    dbCountry.ConstrainedRegex,
                    dbCountry.DataCurated,
                    CurationStatus = dbCountry.CurationStatus.ToString(),
                    dbCountry.Notes
                };

                await conn.ExecuteAsync(insertQuery, parameters);
                inserted++;
            }
            else
            {
                const string updateQuery = @"
                    UPDATE data.CountryInfo
                    SET CountryName = @CountryName,
                        Enabled = @Enabled,
                        HasPostalCodes = @HasPostalCodes,
                        CodeRegex = @CodeRegex,
                        ConstrainedRegex = @ConstrainedRegex,
                        Notes = @Notes,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE CountryId = @CountryId";

                await conn.ExecuteAsync(updateQuery, dbCountry);
                updated++;
            }
        }

        return (inserted, updated);
    }
}

/// <summary>
/// JSON model matching countries.json structure.
/// </summary>
public class CountryJsonModel
{
    public string CountryId { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool HasPostalCodes { get; set; }
    public string? CodeRegex { get; set; }
    public string? ConstrainedRegex { get; set; }
    public string? ConstraintNotes { get; set; }
    public bool DataCurated { get; set; }
    public Models.Enums.CurationStatus CurationStatus { get; set; }
    public string? Notes { get; set; }
}
