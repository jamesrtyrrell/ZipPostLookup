using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.SqlClient;
using ZipPostLookup.CountryDataTools.Commands.Handlers;
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
    /// <summary>Placeholder written when a coordinate is absent.</summary>
    private const string CoordPlaceholder = "---";

    /// <summary>
    /// Enforces the lat/lng pairing rule: coordinates are persisted to data.Reference
    /// ONLY as a complete pair. The enrichment APIs use <c>0</c> as the "no value"
    /// sentinel for either axis; if either Lat or Lon is missing, BOTH come back as the
    /// <see cref="CoordPlaceholder"/> placeholder so a half-populated row (a real Lat with
    /// a blank Lng, or vice-versa) can never be written.
    ///
    /// This is the single formatting point for coordinates in the enrichment path — it
    /// exists to prevent recurrence of the data fault that left 735k CA rows with a
    /// latitude but no longitude. Do not format/write Lat or Lng independently of this.
    /// </summary>
    private static (string Lat, string Lng) CoordinatePair(double lat, double lon)
    {
        if (lat == 0 || lon == 0)
            return (CoordPlaceholder, CoordPlaceholder);

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return (lat.ToString("F6", ci), lon.ToString("F6", ci));
    }

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
        // INVARIANT: coordinates are written only as a complete pair (see CoordinatePair).
        var (latStr, lngStr) = CoordinatePair(result.Lat, result.Lon);

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
                CommonQueries.GetReferenceByCodeAndAltName,
                new { CountryId = countryUpper, ZpCode = zip, PlaceName = result.PlaceName }, tx);
        }

        // Still no match — reuse a "---" placeholder row left by a codes-only import
        // (ImportCodesOnlyCommand) so enrichment renames it in place instead of
        // inserting a second row and orphaning the placeholder. Rename here; the
        // existing-row branch below writes PlaceName via UpdateAsync.
        if (existing == null && !string.IsNullOrEmpty(result.PlaceName))
        {
            existing = await conn.QueryFirstOrDefaultAsync<DataReference>(
                CommonQueries.GetReferencePlaceholderByCode,
                new { CountryId = countryUpper, ZpCode = zip, Placeholder = ImportCodesOnlyCommand.PlaceNamePlaceholder }, tx);

            if (existing != null) { existing.PlaceName = result.PlaceName; }
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

        // Propagate the API timezone to any sibling rows that still have a blank
        // timezone, so MarkCodeAsCurated's TimezoneChecked guard passes for all rows.
        if (result.Timezone != null)
        {
            await conn.ExecuteAsync(
                CommonQueries.PropagateTimezoneToBlankSiblings,
                new { CountryId = countryUpper, ZpCode = zip, Timezone = result.Timezone }, tx);
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
