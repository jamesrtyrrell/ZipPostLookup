using Dapper;
using Microsoft.Data.SqlClient;
using Z.Dapper.Plus;
using ZipPostLookup.CountryDataTools.Database.WorkDb;

namespace ZipPostLookup.CountryDataTools.Database;

/// <inheritdoc cref="ICommandServices"/>
public sealed class CommandServices : ICommandServices
{
    private readonly IWorkDbConnectionFactory _factory;

    public CommandServices(IWorkDbConnectionFactory factory)
        => _factory = factory;

    /// <inheritdoc/>
    public async Task<int> ExecuteAsync(string commonQuery, object? parameters = null)
    {
        if (string.IsNullOrEmpty(commonQuery))
            throw new ArgumentNullException(nameof(commonQuery), "Common query cannot be null or empty");

        await using var conn = (SqlConnection)_factory.CreateConnection();
        return await conn.ExecuteAsync(commonQuery, parameters);
    }

    /// <inheritdoc/>
    public Task BulkInsertAsync<T>(string mappingName, IEnumerable<T> items)
    {
        using var conn = (SqlConnection)_factory.CreateConnection();
        conn.BulkInsert(mappingName, items);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task BulkUpdateAsync<T>(string mappingName, IEnumerable<T> items)
    {
        using var conn = (SqlConnection)_factory.CreateConnection();
        conn.BulkUpdate(mappingName, items);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task BulkInsertWithChildrenAsync<T>(IEnumerable<T> items, Func<T, object> childSelector)
    {
        using var conn = (SqlConnection)_factory.CreateConnection();
        conn.BulkInsert(items).AlsoBulkMerge(childSelector);
        return Task.CompletedTask;
    }
}
