namespace ZipPostLookup.CountryDataTools.Database;

/// <summary>
/// General parameterised write execution for callers outside the <c>Database/</c> layer
/// (handlers, dashboards). This is the sanctioned way to run an INSERT / UPDATE / DELETE
/// without holding a raw connection: pass a named SQL constant from
/// <see cref="Sql.CommonQueries"/> plus its bound parameters.
///
/// The SQL itself still lives in <c>CommonQueries</c> (the SQL-location rule); this service
/// only owns connection handling. For typed base-model upserts use the schema services
/// (<c>db.Data</c> / <c>db.Codes</c> / <c>db.Pipeline</c>); for DELETEs that must be
/// transactional, <see cref="IDeleteServices"/> remains available.
/// </summary>
public interface ICommandServices
{
    /// <summary>
    /// Runs a single write statement and returns the number of rows affected.
    /// Pass an anonymous object or DynamicParameters for the query's bound parameters
    /// (e.g. <c>new { CountryId = "US" }</c>), or null for parameter-free queries.
    /// </summary>
    Task<int> ExecuteAsync(string commonQuery, object? parameters = null);

    /// <summary>
    /// Bulk-inserts <paramref name="items"/> using a registered Dapper.Plus named mapping
    /// (e.g. <c>"NewReference"</c>). The mapping (declared in <c>DapperPlusConfiguration</c>)
    /// defines the target table and the column set written.
    /// </summary>
    Task BulkInsertAsync<T>(string mappingName, IEnumerable<T> items);

    /// <summary>
    /// Bulk-updates <paramref name="items"/> using a registered Dapper.Plus named mapping
    /// (e.g. <c>"CoordUpdate"</c>, <c>"CandidateStatusOnly"</c>).
    /// </summary>
    Task BulkUpdateAsync<T>(string mappingName, IEnumerable<T> items);

    /// <summary>
    /// Bulk-inserts <paramref name="items"/> (default mapping) and bulk-merges the child
    /// collection returned by <paramref name="childSelector"/> for each parent — the parent's
    /// generated identity key propagates to the children via the entity's AfterAction hook.
    /// </summary>
    Task BulkInsertWithChildrenAsync<T>(IEnumerable<T> items, Func<T, object> childSelector);
}
