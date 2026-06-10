namespace ZipPostLookup.CountryDataTools.Database;

public interface IDeleteServices
{
    /// <summary>
    /// Executes a DELETE statement inside a transaction.
    /// Pass an anonymous object or DynamicParameters for the query's bound parameters,
    /// e.g. new { CountryId = "US" }. Pass null for parameter-free queries.
    /// </summary>
    Task<bool> DeleteCommandAsync(string commonQuery, object? parameters = null);
}
