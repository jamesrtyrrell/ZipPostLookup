using System.Data;
using Dapper;
using ZipPostLookup.CountryDataTools.Database.Sql;

namespace ZipPostLookup.CountryDataTools.Database;

/// <summary>
/// Shared gold-certification helper. Wraps the <c>BulkCertifyGoldCode</c> /
/// <c>RevokeAllGoldCodesForCountry</c> queries with the "data.GoldCode may not be migrated yet"
/// tolerance every caller needs. Callers own the connection and format their own output.
/// </summary>
internal static class GoldCertifier
{
    /// <summary>Outcome of a bulk certify attempt.</summary>
    public readonly record struct GoldResult(int Certified, string? Error)
    {
        /// <summary>True when the query failed (e.g. data.GoldCode not migrated yet).</summary>
        public bool Failed => Error is not null;
    }

    /// <summary>
    /// Bulk-certifies all newly-eligible gold codes for the country. Returns the certified count,
    /// or an <see cref="GoldResult.Error"/> message when the query fails.
    /// </summary>
    public static async Task<GoldResult> CertifyAsync(IDbConnection conn, string countryCode)
    {
        try
        {
            var certified = await conn.ExecuteAsync(
                CommonQueries.BulkCertifyGoldCode,
                new { CountryId = countryCode.ToUpperInvariant() });
            return new GoldResult(certified, null);
        }
        catch (Exception ex)
        {
            return new GoldResult(0, ex.Message);
        }
    }

    /// <summary>
    /// Deletes all gold certifications for the country (used by importref --force before a re-import).
    /// Returns true on success, false when the query fails (table not migrated yet).
    /// </summary>
    public static async Task<bool> TryRevokeAllAsync(IDbConnection conn, string countryCode)
    {
        try
        {
            await conn.ExecuteAsync(
                CommonQueries.RevokeAllGoldCodesForCountry,
                new { CountryId = countryCode });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
