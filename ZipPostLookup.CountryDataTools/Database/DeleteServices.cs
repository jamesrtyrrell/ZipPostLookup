
using Dapper;
using Microsoft.Data.SqlClient;
using ZipPostLookup.CountryDataTools.Database.WorkDb;

namespace ZipPostLookup.CountryDataTools.Database;

public class DeleteServices : IDeleteServices
{
    private readonly IWorkDbConnectionFactory _factory;

    public DeleteServices(IWorkDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<bool> DeleteCommandAsync(
        string commonQuery, 
        string countryId = "")
    {
        
        if (string.IsNullOrEmpty(commonQuery))
        {
            throw new ArgumentNullException(nameof(commonQuery), "Common query cannot be null or empty");
        }
        
        var parameters = new DynamicParameters();
        parameters.Add("CountryId", countryId);
        
        await using var conn = (SqlConnection)_factory.CreateConnection();
        await using var tx = conn.BeginTransaction();
        try
        {
            await conn.ExecuteAsync(commonQuery, parameters, tx);
            tx.Commit();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting records: {ex.Message}");
            tx.Rollback();
            return false;   
        }
    }
    
}