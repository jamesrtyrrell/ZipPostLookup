using System.Data;

namespace ZipPostLookup.CountryDataTools.Validation.Export;

/// <summary>
/// A single DB-level check that runs before an export writes its output file.
/// Checks detect and fix data inconsistencies (e.g. admin name variants) so the
/// exported CSV reflects a clean, consistent snapshot.
///
/// Returns the number of rows affected (0 = nothing to fix).
/// Implementations may log detail directly to the console; the runner logs a
/// summary table after all checks complete.
/// </summary>
public interface IExportPreCheck
{
    string Name { get; }
    Task<int> RunAsync(IDbConnection conn, string ccUpper, string repoRoot);
}
