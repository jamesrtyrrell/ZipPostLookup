namespace ZipPostLookup.Core;

/// <summary>
/// Static source of <see cref="CountryInfo"/> for the three countries with
/// built-in data. Replaces the old JSON-backed implementation — the data is
/// stable enough to hardcode and the JSON loading was the only embedded-resource
/// dependency that wasn't also covered by the CSV files themselves.
/// </summary>
internal static class CountryInfoSource
{
    private static readonly IReadOnlyDictionary<string, (CountryInfo Info, IReadOnlyList<string> AdminLevelNames)> _data
        = Build();

    public static IReadOnlyDictionary<string, CountryInfo> All =>
        _data.ToDictionary(kv => kv.Key, kv => kv.Value.Info);

    public static CountryInfo? TryGet(string countryId) =>
        _data.TryGetValue(countryId.ToUpperInvariant(), out var e) ? e.Info : null;

    public static IReadOnlyList<string> GetAdminLevelNames(string countryId) =>
        _data.TryGetValue(countryId.ToUpperInvariant(), out var e)
            ? e.AdminLevelNames
            : Array.Empty<string>();

    // -------------------------------------------------------------------------

    private static Dictionary<string, (CountryInfo, IReadOnlyList<string>)> Build() => new()
    {
        ["US"] = (new CountryInfo(
            CountryId:        "US",
            CountryName:      "United States",
            HasPostalCodes:   true,
            CodeRegex:        @"^\d{5}([- ]\d{4})?$",
            ConstrainedRegex: null,
            ConstraintNotes:  null,
            CodeCount:        0,
            DataCurated:      true,
            CurationStatus:   CurationStatus.Curated,
            Notes:            "Known as the ZIP Code (5 digits) or ZIP+4 (9 digits, normalised to 5 on lookup). " +
                              "Also used by US territories including Puerto Rico (PR), Guam (GU), American Samoa (AS), " +
                              "the US Virgin Islands (VI), and the Northern Mariana Islands (MP). " +
                              "Armed Forces postal codes (APO/FPO/DPO) use state codes AA (Americas), AE (Europe), " +
                              "and AP (Pacific) — timezones assigned by theatre. " +
                              "Parcel Return Service (PRS) zips use DC as the state code."
        ), new[] { "State" }),

        ["CA"] = (new CountryInfo(
            CountryId:        "CA",
            CountryName:      "Canada",
            HasPostalCodes:   true,
            CodeRegex:        @"^[A-Z]\d[A-Z] \d[A-Z]\d$",
            ConstrainedRegex: @"^[ABCEGHJ-NPR-TV-Z]\d[ABCEGHJ-NPR-TV-Z] \d[ABCEGHJ-NPR-TV-Z]\d$",
            ConstraintNotes:  "Letters D, F, I, O, Q, U not used",
            CodeCount:        0,
            DataCurated:      true,
            CurationStatus:   CurationStatus.Curated,
            Notes:            "The Forward Sortation Area (FSA) + Local Delivery Unit (LDU) system. " +
                              "Gradually introduced from April 1971. " +
                              "The letters D, F, I, O, Q, and U are excluded to avoid visual confusion."
        ), new[] { "Province" }),

        ["MX"] = (new CountryInfo(
            CountryId:        "MX",
            CountryName:      "Mexico",
            HasPostalCodes:   true,
            CodeRegex:        @"^\d{5}$",
            ConstrainedRegex: null,
            ConstraintNotes:  null,
            CodeCount:        0,
            DataCurated:      true,
            CurationStatus:   CurationStatus.Curated,
            Notes:            "SEPOMEX 5-digit Código Postal system. " +
                              "The first two digits broadly identify the state. " +
                              "Code 00000 is not valid. " +
                              "Each zip may map to multiple place names (colonias, barrios, rancherías)."
        ), new[] { "Estado" }),
    };
}
