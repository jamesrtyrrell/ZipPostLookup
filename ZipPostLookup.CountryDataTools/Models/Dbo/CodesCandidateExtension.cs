using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Models.Dbo;

/// <summary>
/// Helpers for <see cref="CodesCandidate"/> — translating the flat Admin1..Admin5 /
/// Admin1Code..Admin5Code projection (the shape used by the column-mapping widget and the
/// <c>CsvRow</c> constructor) into the <see cref="CodesCandidate.AdminCandidateList"/> rows that
/// actually persist, and building a candidate from a column-mapped delimited row.
/// </summary>
public static class CodesCandidateExtension
{
    // Flat (value, code) accessors per admin level, in order. GeoNames carries up to five levels.
    private static readonly (int Level, Func<CodesCandidate, string> Value, Func<CodesCandidate, string> Code)[] FlatLevels =
    [
        (1, c => c.Admin1, c => c.Admin1Code),
        (2, c => c.Admin2, c => c.Admin2Code),
        (3, c => c.Admin3, c => c.Admin3Code),
        (4, c => c.Admin4, c => c.Admin4Code),
        (5, c => c.Admin5, c => c.Admin5Code),
    ];

    /// <summary>
    /// Rebuilds <see cref="CodesCandidate.AdminCandidateList"/> from the candidate's flat
    /// Admin1..Admin5 / Admin1Code..Admin5Code fields — one entry per level whose value AND code
    /// are both non-blank. The list is reset first, so the call is idempotent. Entries are keyed
    /// by level number (1..5); <see cref="CodesCandidate.RemapCandidatesList"/> later swaps each
    /// level number for the country's real <c>AdminLevelId</c> FK at import time.
    ///
    /// Replaces the former level-1-only <c>BuildAdmin1Level</c>.
    /// </summary>
    /// <returns>The same <paramref name="candidate"/> instance, for chaining.</returns>
    public static CodesCandidate BuildAdminLevels(this CodesCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        candidate.AdminCandidateList = new List<CodesCandidateAdmin>();
        foreach (var (level, value, code) in FlatLevels)
        {
            var v = value(candidate);
            var c = code(candidate);
            if (!string.IsNullOrWhiteSpace(v) && !string.IsNullOrWhiteSpace(c))
            {
                candidate.AdminCandidateList.Add(new CodesCandidateAdmin(level, v, c));
            }
        }

        return candidate;
    }

    /// <summary>
    /// Builds a <see cref="CodesCandidate"/> from one parsed delimited row using a
    /// field-name → column-index map (from <c>ColumnMapping.ToColumnMap()</c>). Each mapped field
    /// is read by index — an unmapped or out-of-range column yields blank; Timezone/Lat/Lng fall
    /// back to the "---" placeholder when unmapped — and the flat admin fields are fanned into
    /// <see cref="CodesCandidate.AdminCandidateList"/> via <see cref="BuildAdminLevels"/>.
    /// Status is Pending; the candidate-import pipeline assigns RunId and the AdminLevelId FKs.
    /// This is the accept-path projection for the column-mapping widget on the ingestion page.
    /// </summary>
    public static CodesCandidate BuildCandidate(
        string country, IReadOnlyDictionary<string, int> columnMap, string[] row)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(country);
        ArgumentNullException.ThrowIfNull(columnMap);
        ArgumentNullException.ThrowIfNull(row);

        string Field(string name) =>
            columnMap.TryGetValue(name, out var i) && i >= 0 && i < row.Length
                ? row[i].Trim()
                : "";

        string FieldOrPlaceholder(string name) =>
            Field(name) is { Length: > 0 } v ? v : "---";

        var candidate = new CodesCandidate
        {
            CountryId  = country.ToUpperInvariant(),
            ZpCode     = Field(nameof(CodesCandidate.ZpCode)),
            PlaceName  = Field(nameof(CodesCandidate.PlaceName)),
            Timezone   = FieldOrPlaceholder(nameof(CodesCandidate.Timezone)),
            Lat        = FieldOrPlaceholder(nameof(CodesCandidate.Lat)),
            Lng        = FieldOrPlaceholder(nameof(CodesCandidate.Lng)),
            IsDefault  = bool.TryParse(Field(nameof(CodesCandidate.IsDefault)), out var d) && d,
            Status     = nameof(CandidateStatus.Pending),
            Admin1     = Field(nameof(CodesCandidate.Admin1)),
            Admin1Code = Field(nameof(CodesCandidate.Admin1Code)),
            Admin2     = Field(nameof(CodesCandidate.Admin2)),
            Admin2Code = Field(nameof(CodesCandidate.Admin2Code)),
            Admin3     = Field(nameof(CodesCandidate.Admin3)),
            Admin3Code = Field(nameof(CodesCandidate.Admin3Code)),
            Admin4     = Field(nameof(CodesCandidate.Admin4)),
            Admin4Code = Field(nameof(CodesCandidate.Admin4Code)),
            Admin5     = Field(nameof(CodesCandidate.Admin5)),
            Admin5Code = Field(nameof(CodesCandidate.Admin5Code)),
        };

        return candidate.BuildAdminLevels();
    }
}
