using GeoTimeZone;
using TimeZoneConverter;

namespace ZipPostLookup.CountryDataTools.Utilities;

/// <summary>
/// Resolves timezone strings to IANA identifiers.
///
/// Resolution order:
///   1. Known Windows timezone name (checked via TZConvert.KnownWindowsTimeZoneIds FIRST,
///      before any IANA check) → convert via TZConvert with territory hint when available.
///      This must come first because on Windows, TimeZoneInfo.FindSystemTimeZoneById
///      accepts both Windows IDs and IANA IDs, so "Atlantic Standard Time" would pass
///      the IANA check and be returned unchanged without this guard.
///   2. Already a valid IANA id → return as-is.
///   3. IANA-ish name with spaces → replace spaces with underscores and re-check.
///   4. Last-resort TZConvert attempt for non-standard names.
///   5. Unresolvable → return null.
///
/// The country code should always be passed when known — it comes from the
/// filename (us.csv → "US", mx.csv → "MX") per the ZipPostLookup convention.
/// Territory-aware examples:
///   "Atlantic Standard Time" + "PR" → "America/Puerto_Rico"
///   "Central Standard Time"  + "US" → "America/Chicago"
///   "Central Standard Time"  + "MX" → "America/Mexico_City"
///   "Central Standard Time"  + "CA" → "America/Winnipeg"
/// </summary>
public static class TimezoneResolver
{
    /// <summary>
    /// Returns the IANA identifier for <paramref name="raw"/>, or null if unresolvable.
    /// <paramref name="suggested"/> is populated whenever a conversion succeeds.
    /// <paramref name="countryCode"/> is the ISO 3166-1 alpha-2 territory hint (e.g. "US").
    /// </summary>
    public static string? TryResolve(string raw, out string suggested, string? countryCode = null)
    {
        suggested = "";
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var trimmed = raw.Trim();

        // 1. Windows timezone name — must be checked BEFORE IsValidIana.
        //    On Windows, FindSystemTimeZoneById accepts Windows IDs too, so
        //    "Atlantic Standard Time" would falsely pass step 2 and be left unconverted.
        if (TZConvert.KnownWindowsTimeZoneIds.Contains(trimmed))
        {
            // 1a. Territory-aware for regionally correct mapping.
            if (!string.IsNullOrEmpty(countryCode))
            {
                try
                {
                    var iana = TZConvert.WindowsToIana(trimmed, countryCode.ToUpperInvariant());
                    if (!string.IsNullOrEmpty(iana)) { suggested = iana; return iana; }
                }
                catch { /* no territory-specific mapping — fall through */ }
            }

            // 1b. Default world mapping (CLDR territory "001").
            try
            {
                var iana = TZConvert.WindowsToIana(trimmed);
                if (!string.IsNullOrEmpty(iana)) { suggested = iana; return iana; }
            }
            catch { }
        }

        // 2a. Etc/Unknown — legitimate sentinel for codes with no geographic location
        //     (e.g. APO/FPO/DPO armed forces codes). Pass through as-is; IsValidIana
        //     rejects it on Windows because it's not in the Windows timezone registry.
        if (trimmed == "Etc/Unknown") { suggested = trimmed; return trimmed; }

        // 2b. Already a valid IANA id.
        if (IsValidIana(trimmed))
        {
            // 2b. Normalise deprecated aliases to their canonical replacement.
            if (DeprecatedTimezones.GetCanonical(trimmed) is { } canonical)
            {
                suggested = canonical;
                return canonical;
            }
            suggested = trimmed;
            return trimmed;
        }

        // 3. IANA-ish with spaces instead of underscores.
        //    e.g. "America/New York" → "America/New_York"
        var normalised = trimmed.Replace(' ', '_');
        if (!string.Equals(normalised, trimmed) && IsValidIana(normalised))
        {
            suggested = normalised;
            return normalised;
        }

        // 4. Last-resort TZConvert for non-standard or locale-specific names
        //    not in KnownWindowsTimeZoneIds.
        try
        {
            var iana = string.IsNullOrEmpty(countryCode)
                ? TZConvert.WindowsToIana(trimmed)
                : TZConvert.WindowsToIana(trimmed, countryCode.ToUpperInvariant());
            if (!string.IsNullOrEmpty(iana)) { suggested = iana; return iana; }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Derives the country code from a CSV filename following the ZipPostLookup
    /// convention: the filename stem is the lowercase ISO 3166-1 alpha-2 code.
    ///   us.csv  → "US"
    ///   mx.csv  → "MX"
    ///   gb.csv  → "GB"
    ///   us-extracted-records.csv → "US"
    /// Returns null if the stem does not start with a two-letter code.
    /// </summary>
    public static string? CountryCodeFromFileName(string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);
        var cc = stem.Split('-')[0];
        return cc.Length == 2 && cc.All(char.IsLetter)
            ? cc.ToUpperInvariant()
            : null;
    }

    /// <summary>
    /// Given a state/province abbreviation and country code, returns the most likely
    /// IANA timezone. Used as a last-resort fallback when the timezone field is blank.
    /// </summary>
    public static string? GuessFromState(string stateAbbr, string countryCode)
    {
        var key = $"{countryCode.ToUpperInvariant()}:{stateAbbr.ToUpperInvariant()}";
        return StateToIana.TryGetValue(key, out var iana) ? iana : null;
    }

    // -------------------------------------------------------------------------

    private static bool IsValidIana(string value)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(value);
            return true;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }

    // -------------------------------------------------------------------------
    // State / province → primary IANA timezone
    // -------------------------------------------------------------------------

    private static readonly Dictionary<string, string> StateToIana =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // United States
            ["US:AL"] = "America/Chicago",
            ["US:AK"] = "America/Anchorage",
            ["US:AZ"] = "America/Phoenix",
            ["US:AR"] = "America/Chicago",
            ["US:CA"] = "America/Los_Angeles",
            ["US:CO"] = "America/Denver",
            ["US:CT"] = "America/New_York",
            ["US:DC"] = "America/New_York",
            ["US:DE"] = "America/New_York",
            ["US:FL"] = "America/New_York",
            ["US:GA"] = "America/New_York",
            ["US:GU"] = "Pacific/Guam",
            ["US:HI"] = "Pacific/Honolulu",
            ["US:ID"] = "America/Boise",
            ["US:IL"] = "America/Chicago",
            ["US:IN"] = "America/Indiana/Indianapolis",
            ["US:IA"] = "America/Chicago",
            ["US:KS"] = "America/Chicago",
            ["US:KY"] = "America/New_York",
            ["US:LA"] = "America/Chicago",
            ["US:ME"] = "America/New_York",
            ["US:MD"] = "America/New_York",
            ["US:MA"] = "America/New_York",
            ["US:MI"] = "America/Detroit",
            ["US:MN"] = "America/Chicago",
            ["US:MS"] = "America/Chicago",
            ["US:MO"] = "America/Chicago",
            ["US:MT"] = "America/Denver",
            ["US:NE"] = "America/Chicago",
            ["US:NV"] = "America/Los_Angeles",
            ["US:NH"] = "America/New_York",
            ["US:NJ"] = "America/New_York",
            ["US:NM"] = "America/Denver",
            ["US:NY"] = "America/New_York",
            ["US:NC"] = "America/New_York",
            ["US:ND"] = "America/Chicago",
            ["US:OH"] = "America/New_York",
            ["US:OK"] = "America/Chicago",
            ["US:OR"] = "America/Los_Angeles",
            ["US:PA"] = "America/New_York",
            ["US:PR"] = "America/Puerto_Rico",
            ["US:RI"] = "America/New_York",
            ["US:SC"] = "America/New_York",
            ["US:SD"] = "America/Chicago",
            ["US:TN"] = "America/Chicago",
            ["US:TX"] = "America/Chicago",
            ["US:UT"] = "America/Denver",
            ["US:VT"] = "America/New_York",
            ["US:VA"] = "America/New_York",
            ["US:VI"] = "America/St_Thomas",
            ["US:WA"] = "America/Los_Angeles",
            ["US:WV"] = "America/New_York",
            ["US:WI"] = "America/Chicago",
            ["US:WY"] = "America/Denver",

            // Canada
            ["CA:AB"] = "America/Edmonton",
            ["CA:BC"] = "America/Vancouver",
            ["CA:MB"] = "America/Winnipeg",
            ["CA:NB"] = "America/Moncton",
            ["CA:NL"] = "America/St_Johns",
            ["CA:NS"] = "America/Halifax",
            ["CA:NT"] = "America/Yellowknife",
            ["CA:NU"] = "America/Iqaluit",
            ["CA:ON"] = "America/Toronto",
            ["CA:PE"] = "America/Halifax",
            ["CA:QC"] = "America/Toronto",
            ["CA:SK"] = "America/Regina",
            ["CA:YT"] = "America/Whitehorse",

            // Mexico
            ["MX:AG"] = "America/Mexico_City",
            ["MX:BC"] = "America/Tijuana",
            ["MX:BS"] = "America/Mazatlan",
            ["MX:CM"] = "America/Mexico_City",
            ["MX:CS"] = "America/Mexico_City",
            ["MX:CH"] = "America/Chihuahua",
            ["MX:CO"] = "America/Mexico_City",
            ["MX:CL"] = "America/Mexico_City",
            ["MX:DF"] = "America/Mexico_City",
            ["MX:DG"] = "America/Mazatlan",
            ["MX:GT"] = "America/Mexico_City",
            ["MX:GR"] = "America/Mexico_City",
            ["MX:HG"] = "America/Mexico_City",
            ["MX:JA"] = "America/Mexico_City",
            ["MX:EM"] = "America/Mexico_City",
            ["MX:MI"] = "America/Mexico_City",
            ["MX:MO"] = "America/Mexico_City",
            ["MX:NA"] = "America/Mazatlan",
            ["MX:NL"] = "America/Monterrey",
            ["MX:OA"] = "America/Mexico_City",
            ["MX:PU"] = "America/Mexico_City",
            ["MX:QT"] = "America/Mexico_City",
            ["MX:QR"] = "America/Cancun",
            ["MX:SL"] = "America/Mexico_City",
            ["MX:SI"] = "America/Mazatlan",
            ["MX:SO"] = "America/Hermosillo",
            ["MX:TB"] = "America/Mexico_City",
            ["MX:TM"] = "America/Monterrey",
            ["MX:TL"] = "America/Mexico_City",
            ["MX:VE"] = "America/Mexico_City",
            ["MX:YU"] = "America/Merida",
            ["MX:ZA"] = "America/Mexico_City",
        };

    public static string? TryResolveWithCoordinates(
        string latitude, 
        string longitude)
    {
        // try to parse latitude/longitude to doubles or return null
        if (!double.TryParse(latitude, out var lat) 
            || !double.TryParse(longitude, out var lng))
        {
            return null;
        }
        try
        {
            var iana = TimeZoneLookup.GetTimeZone(lat, lng).Result;
            if (string.IsNullOrWhiteSpace(iana) || !iana.Contains('/') || iana == "Etc/Unknown")
                return null;
            return iana;
        }
        catch
        {
            return null;
        }
    }
}