using System.Data;
using Dapper;
using Spectre.Console;

namespace ZipPostLookup.CountryDataTools.Validation.Export;

/// <summary>
/// Pre-export check: detects pairs of names for the same code that are
/// abbreviation-equivalent (e.g. "St. Martin" / "Saint Martin") and links
/// the abbreviated form to the canonical by setting AltNameOf on the alternate row.
/// Runs before every --target ref export so the committed CSV always reflects
/// known alternate-name relationships.
/// </summary>
public sealed class NormalizePlaceNamesCheck : IExportPreCheck
{
    public string Name => "Normalise place-name abbreviation alternates";

    public async Task<int> RunAsync(IDbConnection conn, string ccUpper, string repoRoot)
    {
        var rules   = CountryRulesFactory.For(ccUpper);
        var langs   = rules.PlaceNameLanguages;

        // Reset any AltNameOf that was incorrectly placed on an IsDefault=1 row. IsDefault=1
        // means the postal authority considers it the official name — it must stay canonical
        // (AltNameOf IS NULL). Any prior run that inverted this is corrected here before
        // re-processing, so the next pass can set AltNameOf on the right (non-default) row.
        await conn.ExecuteAsync(
            @"UPDATE data.Reference SET AltNameOf = NULL
              WHERE CountryId = @CountryId AND IsDefault = 1 AND AltNameOf IS NOT NULL",
            new { CountryId = ccUpper });

        // Load all unlinked rows grouped by code
        var rows = (await conn.QueryAsync<(long ReferenceId, string ZpCode, string PlaceName, bool IsDefault, string Lat, string Lng)>(
            @"SELECT ReferenceId, ZpCode, PlaceName, CAST(IsDefault AS BIT) AS IsDefault, Lat, Lng
              FROM data.Reference
              WHERE CountryId = @CountryId AND AltNameOf IS NULL
              ORDER BY ZpCode, PlaceName",
            new { CountryId = ccUpper })).ToList();

        var updates = new List<(long AltRefId, string CanonicalName)>();

        foreach (var group in rows.GroupBy(r => r.ZpCode))
        {
            var names = group.ToList();
            if (names.Count < 2) continue;

            // Find pairs that are abbreviation-equivalent
            for (int i = 0; i < names.Count; i++)
            {
                for (int j = i + 1; j < names.Count; j++)
                {
                    var a = names[i];
                    var b = names[j];

                    if (!PlaceNameNormalizer.AreEquivalent(a.PlaceName, b.PlaceName, langs))
                        continue;

                    // Require lat/lon to match (both ---) or within ~0.01° (~1 km)
                    if (!LatLonMatch(a.Lat, a.Lng, b.Lat, b.Lng))
                        continue;

                    // Determine which is canonical. IsDefault=1 always wins — it is the
                    // authoritative postal name. Only when both have the same IsDefault do
                    // we fall back to preferring the fully-spelled (non-abbreviated) form.
                    var aIsCanonical = IsCanonical(a.PlaceName, langs);
                    var bIsCanonical = IsCanonical(b.PlaceName, langs);

                    (long altId, string canonicalName) = (a.IsDefault, b.IsDefault, aIsCanonical, bIsCanonical) switch
                    {
                        // IsDefault is the primary criterion: a is the official name
                        (true,  false, _,    _   ) => (b.ReferenceId, a.PlaceName),
                        // b is the official name
                        (false, true,  _,    _   ) => (a.ReferenceId, b.PlaceName),
                        // Same IsDefault — fall back to fully-spelled form as canonical
                        (_,     _,     true, false) => (b.ReferenceId, a.PlaceName),
                        (_,     _,     false, true ) => (a.ReferenceId, b.PlaceName),
                        // Tie: prefer the longer name as canonical
                        _                           => a.PlaceName.Length >= b.PlaceName.Length
                            ? (b.ReferenceId, a.PlaceName)
                            : (a.ReferenceId, b.PlaceName),
                    };

                    updates.Add((altId, canonicalName));
                }
            }
        }

        if (updates.Count == 0) return 0;

        foreach (var (altId, canonicalName) in updates)
        {
            await conn.ExecuteAsync(
                "UPDATE data.Reference SET AltNameOf = @Canonical WHERE ReferenceId = @Id",
                new { Canonical = canonicalName, Id = altId });
        }

        AnsiConsole.MarkupLine(
            $"    [yellow]{updates.Count:N0} abbreviation-equivalent name pair(s) linked — " +
            $"non-default alternates marked with AltNameOf pointing to the official postal name " +
            $"(e.g. 'E'↔'East', 'N'↔'North', 'St'↔'Saint', 'Mt'↔'Mount').[/]");

        return updates.Count;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool IsCanonical(string name, IEnumerable<string> langs)
    {
        var normalized = PlaceNameNormalizer.Normalize(name, langs);
        return string.Equals(normalized, name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LatLonMatch(string lat1, string lng1, string lat2, string lng2)
    {
        const double Threshold = 0.01; // ~1 km
        var both1Missing = lat1 == "---" || string.IsNullOrEmpty(lat1);
        var both2Missing = lat2 == "---" || string.IsNullOrEmpty(lat2);

        if (both1Missing && both2Missing) return true;
        if (both1Missing != both2Missing)  return false;

        return double.TryParse(lat1, System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var la1)
            && double.TryParse(lat2, System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var la2)
            && double.TryParse(lng1, System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var lo1)
            && double.TryParse(lng2, System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var lo2)
            && Math.Abs(la1 - la2) < Threshold
            && Math.Abs(lo1 - lo2) < Threshold;
    }
}
