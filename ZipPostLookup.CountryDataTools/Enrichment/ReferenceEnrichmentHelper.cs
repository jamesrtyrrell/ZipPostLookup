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
    // ── Armed Forces territory routing ────────────────────────────────────────

    private static readonly HashSet<string> _armedForcesStates =
        new(StringComparer.OrdinalIgnoreCase) { "AA", "AE", "AP" };

    public static bool IsArmedForcesState(string? state) =>
        !string.IsNullOrWhiteSpace(state) && _armedForcesStates.Contains(state);

    public static ApiLookupResult GetArmedForcesResult(string state) =>
        state.ToUpperInvariant() switch
        {
            "AA" => new ApiLookupResult { Admin1Code = "AA", Admin1Name = "U.S. Armed Forces - Americas", Timezone = "Etc/Unknown" },
            "AE" => new ApiLookupResult { Admin1Code = "AE", Admin1Name = "U.S. Armed Forces - Europe",   Timezone = "Etc/Unknown" },
            _    => new ApiLookupResult { Admin1Code = "AP", Admin1Name = "U.S. Armed Forces - Pacific",  Timezone = "Etc/Unknown" },
        };

    // ── Update data.Reference with API result ─────────────────────────────────

    /// <summary>
    /// Applies an <see cref="ApiLookupResult"/> to all <c>data.Reference</c> rows for
    /// the given zip code. Sets TimezoneChecked, NameChecked, lat/lng, and admin level 1.
    /// Returns true if a new name row was inserted.
    /// </summary>
    public static async Task<bool> UpdateReferenceAsync(
        SqlConnection conn, string country, string zip, ApiLookupResult result, SqlTransaction tx)
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

            if (adminLevelId.HasValue && !string.IsNullOrEmpty(result.Admin1Code))
            {
                await UpsertReferenceAdminAsync(
                    conn, existing.ReferenceId, adminLevelId.Value,
                    result.Admin1Name, result.Admin1Code, tx);
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

            if (adminLevelId.HasValue && !string.IsNullOrEmpty(result.Admin1Code))
            {
                await UpsertReferenceAdminAsync(
                    conn, newId, adminLevelId.Value,
                    result.Admin1Name, result.Admin1Code, tx);
            }
        }

        // Propagate NameChecked=1 to all timezone-verified rows for this zip.
        if (!string.IsNullOrEmpty(result.PlaceName))
        {
            await conn.ExecuteAsync(
                CommonQueries.UpdateReferenceNameChecked,
                new { CountryId = countryUpper, ZpCode = zip }, tx);
        }

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
