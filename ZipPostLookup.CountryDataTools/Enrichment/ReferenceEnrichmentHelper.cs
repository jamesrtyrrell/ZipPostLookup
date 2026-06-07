using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.SqlClient;
using ZipPostLookup.CountryDataTools.Database.Sql;
using ZipPostLookup.CountryDataTools.Enrichment.Api;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Enrichment;

/// <summary>
/// Shared helpers used by both EnrichCandidatesCommand (discrepancy pipeline) and
/// EnrichDirectCommand (direct reference backfill).
/// </summary>
internal static class ReferenceEnrichmentHelper
{
    // ── Update data.Reference with API result ─────────────────────────────────

    /// <summary>
    /// Applies an <see cref="ApiLookupResult"/> to all <c>data.Reference</c> rows for
    /// the given zip code. Sets TimezoneChecked, NameChecked, lat/lng, and admin level 1.
    /// When <paramref name="adminOverride"/> is provided it takes precedence over the
    /// API result's admin fields (used for deterministic range-based resolution, e.g. MX estados).
    /// Returns true if a new name row was inserted.
    /// </summary>
    public static async Task<bool> UpdateReferenceAsync(
        SqlConnection conn, string country, string zip, ApiLookupResult result, SqlTransaction tx,
        (string Code, string Name)? adminOverride = null)
    {
        var countryUpper = country.ToUpperInvariant();
        var cityChecked  = !string.IsNullOrEmpty(result.PlaceName);
        var latStr = result.Lat != 0
            ? result.Lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) : "---";
        var lngStr = result.Lon != 0
            ? result.Lon.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) : "---";

        var adminLevelId = await conn.ExecuteScalarAsync<int?>(
            CommonQueries.GetAdminLevelIdByLevel,
            new { CountryId = countryUpper }, tx);

        var admin1Code = adminOverride?.Code ?? result.Admin1Code;
        var admin1Name = adminOverride?.Name ?? result.Admin1Name;

        var existing = await conn.QueryFirstOrDefaultAsync<DataReference>(
            string.IsNullOrEmpty(result.PlaceName)
                ? CommonQueries.GetReferenceByCode
                : CommonQueries.GetReferenceByCodeAndName,
            new { CountryId = countryUpper, ZpCode = zip, PlaceName = result.PlaceName },
            tx);

        // If exact name not found, check for an AltNameOf alias pointing to this name.
        if (existing == null && !string.IsNullOrEmpty(result.PlaceName))
        {
            existing = await conn.QueryFirstOrDefaultAsync<DataReference>(
                @"SELECT * FROM data.Reference
                  WHERE CountryId = @CountryId AND ZpCode = @ZpCode AND AltNameOf = @PlaceName",
                new { CountryId = countryUpper, ZpCode = zip, PlaceName = result.PlaceName }, tx);
        }

        bool newNameInserted = false;

        if (existing != null)
        {
            if (result.Timezone != null)
            {
                existing.Timezone        = result.Timezone;
                existing.TimezoneChecked = true;
            }
            existing.Lat        = latStr;
            existing.Lng        = lngStr;
            existing.NameChecked = cityChecked;
            existing.UpdatedAt  = DateTimeOffset.UtcNow;
            await conn.UpdateAsync(existing, tx);

            if (adminLevelId.HasValue && !string.IsNullOrEmpty(admin1Code))
            {
                await UpsertReferenceAdminAsync(
                    conn, existing.ReferenceId, adminLevelId.Value,
                    admin1Name, admin1Code, tx);
            }
        }
        else
        {
            var anyExists = await conn.ExecuteScalarAsync<int>(
                CommonQueries.CountReferenceByCode,
                new { CountryId = countryUpper, ZpCode = zip }, tx);

            var newRef = new DataReference
            {
                CountryId        = countryUpper,
                ZpCode           = zip,
                PlaceName        = result.PlaceName,
                Timezone         = result.Timezone ?? "---",
                IsDefault        = anyExists == 0,
                Lat              = latStr,
                Lng              = lngStr,
                TimezoneChecked  = result.Timezone != null,
                NameChecked      = cityChecked,
                CreatedAt        = DateTimeOffset.UtcNow,
                UpdatedAt        = DateTimeOffset.UtcNow,
            };

            var newId = (long)await conn.InsertAsync(newRef, tx);
            newNameInserted = true;

            if (adminLevelId.HasValue && !string.IsNullOrEmpty(admin1Code))
            {
                await UpsertReferenceAdminAsync(
                    conn, newId, adminLevelId.Value,
                    admin1Name, admin1Code, tx);
            }
        }

        // Mark all rows for this zip as fully curated. The API confirmed the code
        // is valid; timezone and place names from the source CSV are trustworthy.
        await conn.ExecuteAsync(
            CommonQueries.MarkCodeAsCurated,
            new { CountryId = countryUpper, ZpCode = zip }, tx);

        return newNameInserted;
    }

    // ── Upsert admin level 1 for a reference row ──────────────────────────────

    public static async Task UpsertReferenceAdminAsync(
        SqlConnection conn, long referenceId, int adminLevelId,
        string stateName, string stateCode, SqlTransaction tx)
    {
        var existing = await conn.QueryFirstOrDefaultAsync<DataReferenceAdmin>(
            CommonQueries.GetReferenceAdmin,
            new { ReferenceId = referenceId, AdminLevelId = adminLevelId }, tx);

        if (existing == null)
        {
            await conn.InsertAsync(new DataReferenceAdmin
            {
                ReferenceId  = referenceId,
                AdminLevelId = adminLevelId,
                Value        = stateName,
                Code         = stateCode,
                CreatedAt    = DateTimeOffset.UtcNow,
            }, tx);
        }
        else if (existing.Value != stateName || existing.Code != stateCode)
        {
            existing.Value = stateName;
            existing.Code  = stateCode;
            await conn.UpdateAsync(existing, tx);
        }
    }
}
