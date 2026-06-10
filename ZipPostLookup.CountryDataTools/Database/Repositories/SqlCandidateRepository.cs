using Microsoft.Data.SqlClient;
using Z.Dapper.Plus;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Database.Repositories;

/// <summary>
/// SQL Server implementation of <see cref="ICandidateRepository"/>.
/// Bulk-inserts candidates (and their admin rows) via Z.Dapper.Plus.
/// </summary>
public sealed class SqlCandidateRepository : ICandidateRepository
{
    private readonly IWorkDbConnectionFactory _factory;

    public SqlCandidateRepository(IWorkDbConnectionFactory factory)
        => _factory = factory;

    /// <inheritdoc/>
    public async Task InsertBatchAsync(IReadOnlyList<CodesCandidate> candidates)
    {
        if (candidates.Count == 0) { return; }

        await using var conn = (SqlConnection)_factory.CreateConnection();

        var candidateChunks = candidates.Chunk(5000);
        var counter = 0;
        var codesCandidatesEnumerable = candidateChunks.ToList();

        foreach (var codeCandidates in codesCandidatesEnumerable)
        {
            conn.BulkInsert(codeCandidates)
                .AlsoBulkMerge(x => x.AdminCandidateList);

            counter++;
            if (counter % 5 == 0)
            {
                Console.WriteLine($"Processed {counter} chunks of {codesCandidatesEnumerable.Count()}");
            }
        }
    }
}
