using ZipPostLookup.CountryDataTools.Validation.Csv;
using ZipPostLookup.CountryDataTools.Utilities;
using ZipPostLookup.Normalizers;

namespace ZipPostLookup.CountryDataTools.Dsv;

/// <summary>
/// Applies all auto-fixable transforms to a list of <see cref="CsvRow"/> instances.
/// Each fix is logged so the caller can report what changed.
/// </summary>
public static class Fixer
{
    public sealed record FixRecord(int RecordNumber, string Zip, string Field, string Was, string Now);

    /// <summary>
    /// Applies all fixes and returns the cleaned row list together with a log of
    /// every change made. The returned row list may be shorter than the input if
    /// duplicate (zip, Name) pairs were removed.
    ///
    /// Fix passes (in order):
    ///   1. Trim whitespace on all fields
    ///   2. Normalise IsDefault to lowercase true/false
    ///   3. Normalise zip via country rules
    ///   4. Convert non-IANA timezones to IANA
    ///   5. Guess timezone from Admin1Code when field is blank
    ///   6. Fix casing (Admin1Code → uppercase, admin1 → Title Case)
    ///   7. Normalise lat/lng — blank/whitespace becomes "---"
    ///   8. Remove duplicate (zip, Name) rows — keep first occurrence
    ///   9. Enforce exactly one IsDefault=true per zip group
    ///
    /// Pass 9 detail — which row becomes the single default:
    ///   ZipPostRegistry uses a last-write-wins strategy when loading the CSV: the
    ///   last is_default=true row for a zip (in CSV order, which is sorted by Name asc)
    ///   overwrites earlier ones in the internal index. The row that GetByCode returns
    ///   is therefore the alphabetically last name among all is_default=true rows.
    ///   To keep the fix result consistent with actual library behaviour, pass 9
    ///   retains is_default=true only on the alphabetically last name in each group
    ///   and demotes all others to false.
    /// </summary>
    public static (List<CsvRow> Rows, IReadOnlyList<FixRecord> Log) Fix(
        IReadOnlyList<CsvRow> rows, string countryCode)
    {
        var log = new List<FixRecord>();

        // Passes 1–7: per-row fixes
        foreach (var row in rows)
        {
            ApplyTrim(row, log);
            ApplyBoolNormalisation(row, log);
            ApplyZipNormalisation(row, countryCode, log);
            ApplyTimezoneConversion(row, countryCode, log);
            ApplyTimezoneGuess(row, countryCode, log);
            ApplyCasing(row, log);
            ApplyLatLngNormalisation(row, log);
            if (row.Lat != "---" && row.Lng != "---")
            {
                ApplyTimezoneCalculation(row, countryCode, log);
            }
        }

        // Pass 8: remove duplicate (zip, Name) pairs — keep first occurrence
        var deduped = RemoveDuplicateRows(rows, log);

        // Pass 9: enforce exactly one IsDefault=true per zip group
        ApplyDefaultDeduplication(deduped, log);

        return (deduped, log);
    }

    private static void ApplyTimezoneCalculation(CsvRow row, string countryCode, List<FixRecord> log)
    {
        var resolved = TimezoneResolver.TryResolveWithCoordinates(row.Lat!, row.Lng!);
        if (resolved != null && !string.Equals(resolved, row.Timezone, StringComparison.Ordinal))
        {
            Patch(row, log, "timezone", row.Timezone, resolved);
        }
        
    }

    // -------------------------------------------------------------------------
    // Per-row fixers
    // -------------------------------------------------------------------------

    private static void ApplyTrim(CsvRow row, List<FixRecord> log)
    {
        Patch(row, log, "ZpCode",       row.ZpCode,      row.ZpCode?.Trim());
        Patch(row, log, "PlaceName",    row.PlaceName,   row.PlaceName?.Trim());
        Patch(row, log, "Timezone",    row.Timezone,   row.Timezone?.Trim());
        Patch(row, log, "IsDefault",   row.IsDefault,  row.IsDefault?.Trim());
        Patch(row, log, "Lat",         row.Lat,        row.Lat?.Trim());
        Patch(row, log, "Lng",         row.Lng,        row.Lng?.Trim());
        Patch(row, log, "Admin1",      row.Admin1,     row.Admin1?.Trim());
        Patch(row, log, "Admin1Code", row.Admin1Code, row.Admin1Code?.Trim());
    }

    private static void ApplyBoolNormalisation(CsvRow row, List<FixRecord> log)
    {
        if (row.IsDefault is null) return;

        string? canonical = row.IsDefault.ToLowerInvariant() switch
        {
            "1" or "yes" or "y" => "true",
            "0" or "no"  or "n" => "false",
            _                   => null
        };

        if (canonical != null)
        {
            Patch(row, log, "IsDefault", row.IsDefault, canonical);
        }
    }

    private static void ApplyZipNormalisation(CsvRow row, string countryCode, List<FixRecord> log)
    {
        if (string.IsNullOrEmpty(row.ZpCode)) return;

        ICountryCodeRules? rules = countryCode.ToUpperInvariant() switch
        {
            "US" => new UsCountryCodeRules(),
            "CA" => new CaCountryCodeRules(),
            "MX" => new MxCountryCodeRules(),
            _    => null
        };

        if (rules == null) return;

        var normalized = rules.Normalize(row.ZpCode);
        Patch(row, log, "ZpCode", row.ZpCode, normalized);
    }

    private static void ApplyTimezoneConversion(CsvRow row, string countryCode, List<FixRecord> log)
    {
        if (string.IsNullOrEmpty(row.Timezone)) return;

        var resolved = TimezoneResolver.TryResolve(row.Timezone, out _, countryCode);
        if (resolved != null && !string.Equals(resolved, row.Timezone, StringComparison.Ordinal))
        {
            Patch(row, log, "Timezone", row.Timezone, resolved);
        }
    }

    private static void ApplyTimezoneGuess(CsvRow row, string countryCode, List<FixRecord> log)
    {
        if (!string.IsNullOrEmpty(row.Timezone)) return;
        if (string.IsNullOrEmpty(row.Admin1Code)) return;

        var guess = TimezoneResolver.GuessFromState(row.Admin1Code, countryCode);
        if (guess != null)
        {
            Patch(row, log, "timezone", row.Timezone ?? "", guess);
        }
    }

    private static void ApplyCasing(CsvRow row, List<FixRecord> log)
    {
        if (!string.IsNullOrEmpty(row.Admin1Code))
        {
            var upper = row.Admin1Code.ToUpperInvariant();
            Patch(row, log, "Admin1Code", row.Admin1Code, upper);
        }

        if (!string.IsNullOrEmpty(row.Admin1))
        {
            var titled = ToTitleCase(row.Admin1);
            Patch(row, log, "Admin1", row.Admin1, titled);
        }
    }

    private static void ApplyLatLngNormalisation(CsvRow row, List<FixRecord> log)
    {
        // Blank or whitespace-only lat/lng → "---" to match the embedded CSV convention
        if (string.IsNullOrWhiteSpace(row.Lat))
        {
            Patch(row, log, "Lat", row.Lat ?? "", "---");
        }

        if (string.IsNullOrWhiteSpace(row.Lng))
        {
            Patch(row, log, "Lng", row.Lng ?? "", "---");
        }
    }

    // -------------------------------------------------------------------------
    // Cross-row: duplicate (zip, Name) removal
    // -------------------------------------------------------------------------

    private static List<CsvRow> RemoveDuplicateRows(IReadOnlyList<CsvRow> rows, List<FixRecord> log)
    {
        var seen   = new HashSet<(string, string)>(ZipCityComparer.Instance);
        var result = new List<CsvRow>(rows.Count);

        foreach (var row in rows)
        {
            var key = (row.ZpCode ?? "", row.PlaceName ?? "");

            if (!seen.Add(key))
            {
                // Duplicate — log as a removed row and skip it
                log.Add(new FixRecord(
                    row.RecordNumber,
                    row.ZpCode ?? "",
                    "row",
                    $"PlaceName:{row.PlaceName} Admin1Code:{row.Admin1Code} tz:{row.Timezone}",
                    "REMOVED (duplicate ZpCode+PlaceName)"));
            }
            else
            {
                result.Add(row);
            }
        }

        if (result.Count < rows.Count)
        {
            Console.WriteLine($"  Removed {rows.Count - result.Count} duplicate row(s).");
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Cross-row: IsDefault deduplication
    // -------------------------------------------------------------------------

    /// <summary>
    /// Enforces exactly one <c>IsDefault=true</c> row per zip group.
    ///
    /// When multiple rows share the same zip and all are marked true, the one to
    /// keep as true is the <strong>alphabetically last name</strong> among the true
    /// rows. This matches <c>ZipPostRegistry</c>'s last-write-wins index behaviour:
    /// the CSV is sorted by zip then name ascending, so the last true row processed
    /// is the one whose name is alphabetically last — and that is the entry
    /// <c>GetByCode</c> / <c>GetByZip</c> will return.
    ///
    /// Demoting any other row to <c>false</c> makes the CSV authoritative and removes
    /// the ambiguity that causes integrity check failures.
    ///
    /// If no row in a group has <c>IsDefault=true</c>, the alphabetically first name
    /// is promoted (consistent with how the library would treat the first loaded row).
    /// </summary>
    private static void ApplyDefaultDeduplication(List<CsvRow> rows, List<FixRecord> log)
    {
        var byZip = new Dictionary<string, List<CsvRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var zip = row.ZpCode ?? "";
            if (string.IsNullOrEmpty(zip)) { continue; }
            if (!byZip.ContainsKey(zip)) { byZip[zip] = []; }
            byZip[zip].Add(row);
        }

        foreach (var (_, group) in byZip)
        {
            var trueRows = group
                .Where(r => "true".Equals(r.IsDefault, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (trueRows.Count > 1)
            {
                // Keep the alphabetically last name as the single default — this is the
                // row ZipPostRegistry will return from GetByCode (last-write-wins on load).
                // Demote every other true row to false.
                var keeper = trueRows
                    .OrderBy(r => r.PlaceName ?? "", StringComparer.OrdinalIgnoreCase)
                    .Last();

                foreach (var dup in trueRows)
                {
                    if (!ReferenceEquals(dup, keeper))
                    {
                        Patch(dup, log, "IsDefault", dup.IsDefault, "false");
                    }
                }
            }
            else if (trueRows.Count == 0 && group.Count > 0)
            {
                // No default at all — promote the alphabetically first name.
                var first = group
                    .OrderBy(r => r.PlaceName ?? "", StringComparer.OrdinalIgnoreCase)
                    .First();
                Patch(first, log, "IsDefault", first.IsDefault, "true");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void Patch(CsvRow row, List<FixRecord> log, string field, string? was, string? now)
    {
        if (string.Equals(was, now, StringComparison.Ordinal)) return;

        log.Add(new FixRecord(row.RecordNumber, row.ZpCode ?? "", field, was ?? "", now ?? ""));

        switch (field)
        {
            case "ZpCode":      row.ZpCode      = now; break;
            case "PlaceName":   row.PlaceName   = now; break;
            case "Timezone":    row.Timezone    = now; break;
            case "IsDefault":   row.IsDefault   = now; break;
            case "Lat":         row.Lat         = now; break;
            case "Lng":         row.Lng         = now; break;
            case "Admin1":      row.Admin1      = now; break;
            case "Admin1Code":  row.Admin1Code  = now; break;
        }
    }

    private static string ToTitleCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var words = input.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length == 0) continue;
            words[i] = char.ToUpperInvariant(words[i][0]) +
                       words[i][1..].ToLowerInvariant();
        }
        return string.Join(' ', words);
    }
}