using Dapper;
using Microsoft.Data.SqlClient;
using Z.Dapper.Plus;
using ZipPostLookup.Core;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Database.Repositories;

/// <summary>
/// SQL Server implementation of <see cref="IReferenceRepository"/>.
///
/// Loads the embedded ZipPostLookup CSV data into <c>data.reference</c> once,
/// then serves lookups from the DB so the rest of the pipeline can query the
/// reference data with SQL rather than loading it into memory on every run.
///
/// Admin-level upserts use Dapper.Contrib against <see cref="DataAdminLevel"/>.
/// The bulk reference insert uses a TVP + MERGE (Dapper.Contrib cannot handle
/// TVP batch inserts, so raw ADO.NET is retained for that path only).
/// </summary>
public sealed class SqlReferenceRepository : IReferenceRepository
{
    private readonly IWorkDbConnectionFactory _factory;

    public SqlReferenceRepository(IWorkDbConnectionFactory factory)
        => _factory = factory;

    /// <inheritdoc/>
    public async Task<bool> HasDataAsync(string countryCode)
    {
        using var conn = _factory.CreateConnection();
        var count = await conn.ExecuteScalarAsync<int>(
            CommonQueries.HasReferenceData,
            new { CountryId = countryCode.ToUpperInvariant() });
        return count > 0;
    }

    /// <inheritdoc/>
    public async Task LoadFromEmbeddedCsvAsync(string countryCode)
    {
        // CountryDataTools is the source of truth — read from its own embedded CSV,
        // not from the ZipPostLookup consumer library.
        var entries = EmbeddedCsvLoader.Load(countryCode);

        if (entries.Count == 0)
        {
            throw new InvalidOperationException(
                $"No embedded CSV data found for country '{countryCode}'. " +
                $"Ensure Data/{countryCode.ToLower()}/{countryCode.ToLower()}.csv " +
                $"is marked as EmbeddedResource in the CountryDataTools .csproj.");
        }

        var countryInfo = CountryInfoRegistry.TryGet(countryCode);
        var isCurated = countryInfo?.DataCurated ?? false;

        // Resolve admin level PKs so DataReference can store correct FKs into
        // data.reference_admins (AdminLevelId references data.admin_levels PK).
        await using var conn = (SqlConnection)_factory.CreateConnection();

        var adminLevelRows = await SqlMapper.QueryAsync<(int LevelNumber, int AdminLevelId)>(
            conn,
            CommonQueries.GetAdminLevels,
            new { CountryId = countryCode.ToUpperInvariant() });

        var adminLevelIds = adminLevelRows.ToDictionary(r => r.LevelNumber, r => r.AdminLevelId);

        Console.WriteLine($"Loading {entries.Count} entries for country '{countryCode}'...");
        Console.WriteLine($"  Is curated    : {isCurated}");
        Console.WriteLine($"  Admin levels  : {adminLevelIds.Count} found in data.admin_levels");

        var dataRangeList = new List<DataReference>();

        foreach (var entry in entries)
        {
            try
            {
                var newDataRange = new DataReference(countryCode, entry, isCurated, adminLevelIds);
                dataRangeList.Add(newDataRange);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing entry {entry.ZpCode} for country '{countryCode}': {ex.Message}");
            }
        }

        if (!dataRangeList.Any())
        {
            Console.WriteLine($"No entries found for country '{countryCode}'.");
            return;
        }

        // Enforce the lat/lng pairing rule: a row may never persist one coordinate
        // without the other (both are blanked to "---" if the pair is incomplete).
        foreach (var dataRange in dataRangeList)
            dataRange.NormalizeCoordinatePair();

        try
        {
            conn.BulkMerge(dataRangeList)
                .AlsoBulkMerge(dataRef => dataRef.AdminReferenceList);
        }
        catch (SqlException sqlEx)
        {
            var errorDetails = string.Join(" | ", sqlEx.Errors
                .Cast<SqlError>()
                .Select(e => $"[{e.Number}] Severity {e.Class} State {e.State}: {e.Message}" +
                             $" (Proc: {e.Procedure}, Line: {e.LineNumber})"));

            Console.WriteLine($"Failed to load CSV data for {countryCode}. SQL error: {errorDetails}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load CSV data for {countryCode}. Error: {ex.Message}");
            throw;
        }



    }

    /// <inheritdoc/>
    public async Task<int> GetCountAsync(string countryCode)
    {
        using var conn = _factory.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            CommonQueries.HasReferenceData,
            new { CountryId = countryCode.ToUpperInvariant() });
    }

    /// <inheritdoc/>
    public async Task DeleteAllAsync(string countryCode)
    {
        await using var conn = (SqlConnection)_factory.CreateConnection();
        await using var tx = conn.BeginTransaction();
        try
        {
            await conn.ExecuteAsync(
                CommonQueries.DeleteReferenceData,
                new { CountryId = countryCode.ToUpperInvariant() }, tx);

            await conn.ExecuteAsync(
                CommonQueries.ResetCountryCodeCount,
                new { CountryId = countryCode.ToUpperInvariant() }, tx);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}