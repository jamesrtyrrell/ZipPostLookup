namespace ZipPostLookup.CountryDataTools.Models.Dbo;

/// <summary>
/// Helpers for <see cref="CodesCandidate"/> — chiefly translating the flat
/// <see cref="CodesCandidate.Admin1"/> / <see cref="CodesCandidate.Admin1Code"/> projection
/// (the shape used by the column-mapping widget and the <c>CsvRow</c> constructor) back into
/// the <see cref="CodesCandidate.AdminCandidateList"/> rows that actually persist.
/// </summary>
public static class CodesCandidateExtension
{
    /// <summary>Admin level number for state/province. Level 1 = the only level built here.</summary>
    public const int Admin1LevelNumber = 1;

    /// <summary>
    /// Rebuilds <see cref="CodesCandidate.AdminCandidateList"/> from the candidate's flat
    /// <see cref="CodesCandidate.Admin1"/> / <see cref="CodesCandidate.Admin1Code"/> fields,
    /// producing a single admin-level-1 entry. The list is reset first, so the call is
    /// idempotent. When either field is blank no entry is added — the list is left empty
    /// (mirrors the <c>CodesCandidate(string, CsvRow)</c> constructor's behaviour).
    ///
    /// LIMITATION: this handles admin level 1 only. GeoNames source data carries up to five
    /// admin levels (admin1–admin3 names + codes). When the template/pipeline grows past a
    /// single level this must expand to emit one entry per populated level. See the
    /// "CDT Column Picker" project in .claude/PROJECTS.md.
    /// </summary>
    /// <returns>The same <paramref name="candidate"/> instance, for chaining.</returns>
    public static CodesCandidate BuildAdmin1Level(this CodesCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        candidate.AdminCandidateList = new List<CodesCandidateAdmin>();

        if (!string.IsNullOrWhiteSpace(candidate.Admin1) &&
            !string.IsNullOrWhiteSpace(candidate.Admin1Code))
        {
            candidate.AdminCandidateList.Add(
                new CodesCandidateAdmin(Admin1LevelNumber, candidate.Admin1, candidate.Admin1Code));
        }

        return candidate;
    }
}
