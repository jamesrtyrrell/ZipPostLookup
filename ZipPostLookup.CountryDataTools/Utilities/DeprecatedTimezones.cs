namespace ZipPostLookup.CountryDataTools.Utilities;

/// <summary>
/// Authoritative map of deprecated IANA timezone identifiers to their canonical replacements.
///
/// Sources:
///   - tzdata 2022b: merged zones identical since 1970 into canonical representatives.
///   - Legacy posix-style US/ and Canada/ prefixes, which pre-date the IANA America/ scheme.
///
/// Use <see cref="GetCanonical"/> to normalise any incoming timezone value before storing it.
/// Any id listed here is safe to replace with its canonical — the UTC offset history is identical.
/// </summary>
public static class DeprecatedTimezones
{
    private static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // --- Canada — deprecated in tzdata 2022b ---
            ["America/Nipigon"]     = "America/Toronto",
            ["America/Thunder_Bay"] = "America/Toronto",
            ["America/Rainy_River"] = "America/Winnipeg",
            ["America/Pangnirtung"] = "America/Iqaluit",
            ["America/Glace_Bay"]   = "America/Halifax",   // matches Halifax since 1970
            ["America/Creston"]     = "America/Phoenix",   // BC, MST year-round, no DST

            // --- United States — deprecated in tzdata 2022b ---
            ["America/Shiprock"]    = "America/Denver",    // Navajo Nation, NM

            // --- Legacy posix-style aliases (US/) ---
            ["US/Eastern"]          = "America/New_York",
            ["US/Central"]          = "America/Chicago",
            ["US/Mountain"]         = "America/Denver",
            ["US/Pacific"]          = "America/Los_Angeles",
            ["US/Alaska"]           = "America/Anchorage",
            ["US/Hawaii"]           = "Pacific/Honolulu",
            ["US/Arizona"]          = "America/Phoenix",

            // --- Legacy posix-style aliases (Canada/) ---
            ["Canada/Eastern"]      = "America/Toronto",
            ["Canada/Central"]      = "America/Winnipeg",
            ["Canada/Mountain"]     = "America/Edmonton",
            ["Canada/Pacific"]      = "America/Vancouver",
            ["Canada/Atlantic"]     = "America/Halifax",
            ["Canada/Newfoundland"] = "America/St_Johns",
            ["Canada/Saskatchewan"] = "America/Regina",
            ["Canada/Yukon"]        = "America/Whitehorse",

            // --- Legacy posix-style aliases (Mexico/) ---
            ["Mexico/BajaNorte"]    = "America/Tijuana",
            ["Mexico/BajaSur"]      = "America/Mazatlan",
            ["Mexico/General"]      = "America/Mexico_City",
        };

    /// <summary>
    /// Returns the canonical IANA id for <paramref name="timezone"/> if it is deprecated,
    /// or <see langword="null"/> if it is already canonical or unknown.
    /// </summary>
    public static string? GetCanonical(string timezone) =>
        Map.TryGetValue(timezone, out var canonical) ? canonical : null;

    /// <summary>Returns true if <paramref name="timezone"/> is a known deprecated alias.</summary>
    public static bool IsDeprecated(string timezone) => Map.ContainsKey(timezone);
}
