using ZipPostLookup.CountryDataTools.Dsv;
using ZipPostLookup.CountryDataTools.Models.Enums;
using ZipPostLookup.CountryDataTools.Utilities;
using ZipPostLookup.Normalizers;

namespace ZipPostLookup.CountryDataTools.Validation.Csv;

/// <summary>
/// Runs all validation checks over a parsed CSV and returns the complete error list.
/// Checks are grouped: structural (header), per-row (field presence/format), and
/// cross-row (duplicates, IsDefault consistency).
/// </summary>
public static class ValidationRules
{
    public static IReadOnlyList<ValidationError> Validate(
        IReadOnlyList<CsvRow> rows,
        bool headerOk,
        IReadOnlyList<string> missingColumns,
        string countryCode)
    {
        var errors = new List<ValidationError>();

        // --- Structural: header ---
        foreach (var col in missingColumns)
        {
            errors.Add(new ValidationError(0, "", "header", ErrorType.MissingColumn, col));
        }

        // If the header is broken we can't reliably validate rows
        if (!headerOk) return errors;

        var rules = GetRules(countryCode);

        // Cross-row tracking
        // (zip, Name) → first record number seen — duplicate detection
        var seen = new Dictionary<(string zip, string city), int>(ZipCityComparer.Instance);
        // zip → list of record numbers that are IsDefault=true
        var defaults = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        // zip → whether any IsDefault=true was seen
        var hasDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var code  = row.ZpCode    ?? "";
            var name  = row.PlaceName ?? "";

            // --- Per-row: required fields present ---
            CheckMissing(errors, row, "ZpCode",   code);
            CheckMissing(errors, row, "PlaceName", name);
            CheckMissing(errors, row, "Timezone", row.Timezone ?? "");

            // --- Per-row: optional admin fields — warn if one is present but not the other ---
            var hasAdmin1     = !string.IsNullOrWhiteSpace(row.Admin1);
            var hasAdmin1Code = !string.IsNullOrWhiteSpace(row.Admin1Code);
            if (hasAdmin1 && !hasAdmin1Code)
            {
                errors.Add(new ValidationError(row.RecordNumber, code, "Admin1Code",
                    ErrorType.MissingValue, "admin1 present but admin1Code is missing"));
            }

            if (hasAdmin1Code && !hasAdmin1)
            {
                errors.Add(new ValidationError(row.RecordNumber, code, "Admin1",
                    ErrorType.MissingValue, "admin1Code present but admin1 is missing"));
            }

            // --- Per-row: lat/lng — must both be present or both absent/--- ---
            var lat = row.Lat ?? "";
            var lng = row.Lng ?? "";
            var latBlank = string.IsNullOrWhiteSpace(lat) || lat == "---";
            var lngBlank = string.IsNullOrWhiteSpace(lng) || lng == "---";
            if (latBlank != lngBlank)
            {
                errors.Add(new ValidationError(row.RecordNumber, code, latBlank ? "Lat" : "Lng",
                    ErrorType.MissingValue, "lat and lng must both be present or both absent"));
            }
            else if (!latBlank)
            {
                if (!double.TryParse(lat, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var latVal)
                    || latVal < -90 || latVal > 90)
                {
                    errors.Add(new ValidationError(row.RecordNumber, code, "Lat",
                        ErrorType.InvalidZipFormat, lat));
                }

                if (!double.TryParse(lng, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var lngVal)
                    || lngVal < -180 || lngVal > 180)
                {
                    errors.Add(new ValidationError(row.RecordNumber, code, "Lng",
                        ErrorType.InvalidZipFormat, lng));
                }
            }

            // --- Per-row: zip format ---
            if (!string.IsNullOrEmpty(code) && rules != null)
            {
                var normalized = rules.Normalize(code);
                if (!rules.Validate(normalized))
                {
                    errors.Add(new ValidationError(row.RecordNumber, code, "Code",
                        ErrorType.InvalidZipFormat, code));
                }
            }

            // --- Per-row: IANA timezone ---
            var tz = row.Timezone ?? "";
            if (!string.IsNullOrEmpty(tz))
            {
                var resolved = TimezoneResolver.TryResolve(tz, out var suggested, countryCode);
                if (resolved == null)
                {
                    // if lat and lng exist, use those to try and resolve 
                    if (!string.IsNullOrEmpty(row.Lat) && !string.IsNullOrEmpty(row.Lng))
                    {
                        resolved = TimezoneResolver.TryResolveWithCoordinates(
                            row.Lat, row.Lng);
                    }
                }
                
                
                if (resolved == null)
                {
                    errors.Add(new ValidationError(row.RecordNumber, code, "timezone",
                        ErrorType.UnresolvableTimezone, tz, suggested));
                }
                else if (!string.Equals(resolved, tz, StringComparison.Ordinal))
                {
                    var errorType = DeprecatedTimezones.IsDeprecated(tz)
                        ? ErrorType.DeprecatedTimezone
                        : ErrorType.NonIanaTimezone;
                    errors.Add(new ValidationError(row.RecordNumber, code, "timezone",
                        errorType, tz, resolved));
                }
                if(string.IsNullOrEmpty(row.Timezone))
                {
                    row.Timezone = resolved;
                }
            }

            // --- Per-row: IsDefault must be parseable as bool ---
            var isDefaultRaw = row.IsDefault ?? "";
            if (!string.IsNullOrEmpty(isDefaultRaw) && !TryParseBool(isDefaultRaw, out var isDefault))
            {
                errors.Add(new ValidationError(row.RecordNumber, code, "IsDefault",
                    ErrorType.InvalidBoolean, isDefaultRaw, "true or false"));
            }
            else if (!string.IsNullOrEmpty(isDefaultRaw))
            {
                TryParseBool(isDefaultRaw, out var isDefaultVal);

                // --- Cross-row: duplicate (zip, Name) pairs ---
                var key = (zip: code, city: name);
                if (seen.TryGetValue(key, out var firstRecord))
                {
                    errors.Add(new ValidationError(row.RecordNumber, code, "zip",
                        ErrorType.DuplicatePair, name, $"duplicate_of:record:{firstRecord}"));
                }
                else
                {
                    seen[key] = row.RecordNumber;
                }

                // --- Cross-row: track defaults ---
                if (isDefaultVal)
                {
                    if (!defaults.ContainsKey(code)) { defaults[code] = []; }
                    defaults[code].Add(row.RecordNumber);
                    hasDefault.Add(code);
                }
            }
        }

        // --- Cross-row: multiple IsDefault=true for the same zip ---
        foreach (var (zip, recordNums) in defaults)
        {
            if (recordNums.Count > 1)
            {
                // Flag all but the first
                foreach (var recNum in recordNums.Skip(1))
                {
                    errors.Add(new ValidationError(recNum, zip, "IsDefault",
                        ErrorType.MultipleDefaults, "true",
                        $"first_default:record:{recordNums[0]}"));
                }
            }
        }

        // --- Cross-row: zip with entries but no IsDefault=true ---
        var zipsWithNoDefault = seen.Keys
            .Select(k => k.zip)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(z => !hasDefault.Contains(z))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var zip = row.ZpCode ?? "";
            if (!string.IsNullOrEmpty(zip) && zipsWithNoDefault.Contains(zip))
            {
                // Warn on the first record for this zip only
                zipsWithNoDefault.Remove(zip);
                errors.Add(new ValidationError(row.RecordNumber, zip, "IsDefault",
                    ErrorType.NoDefaultForZip, "", ""));
            }
        }

        return errors;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void CheckMissing(List<ValidationError> errors, CsvRow row, string field, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationError(row.RecordNumber, row.ZpCode ?? "", field,
                ErrorType.MissingValue, ""));
        }
    }

    private static bool TryParseBool(string raw, out bool value)
    {
        if (raw.Equals("true",  StringComparison.OrdinalIgnoreCase)) { value = true;  return true; }
        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
        if (raw == "1") { value = true;  return true; }
        if (raw == "0") { value = false; return true; }
        if (raw.Equals("yes", StringComparison.OrdinalIgnoreCase)) { value = true;  return true; }
        if (raw.Equals("no",  StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
        value = false;
        return false;
    }

    private static ICountryCodeRules? GetRules(string countryCode) =>
        countryCode.ToUpperInvariant() switch
        {
            "US" => new UsCountryCodeRules(),
            "CA" => new CaCountryCodeRules(),
            "MX" => new MxCountryCodeRules(),
            _    => null   // Unknown country: skip zip-format validation
        };
}
