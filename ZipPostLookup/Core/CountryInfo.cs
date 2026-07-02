namespace ZipPostLookup.Core;

/// <summary>
/// Describes a country's postal code system — its format rules, curation status,
/// and the number of entries currently held in the built-in registry.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CountryInfo"/> is populated from the embedded <c>countries.json</c> resource.
/// Access it via <c>ZipPostLookup.GetCountryInfo(CountryCode)</c> for a single country,
/// or <see cref="CountryInfoRegistry.All"/> for the full set.
/// </para>
/// <para>
/// The <see cref="DataCurated"/> flag indicates whether the reference data for this
/// country has been through a full validate → fix → fullscan → spottest cycle and
/// manually signed off. When <c>false</c>, the data is a working reference and may
/// contain inaccuracies.
/// </para>
/// <example>
/// <code>
/// var info = ZipPostLookup.GetCountryInfo(CountryCode.US);
/// Console.WriteLine($"{info.CountryName}: {info.CodeCount:N0} codes, curated: {info.DataCurated}");
/// </code>
/// </example>
/// </remarks>
/// <param name="CountryId">ISO 3166-1 alpha-2 code, e.g. <c>"US"</c>.</param>
/// <param name="CountryName">Full English country name, e.g. <c>"United States"</c>.</param>
/// <param name="Enabled">
/// <c>true</c> if the country is enabled for import/export operations in the system.
/// Controls which countries appear in active workflows.
/// </param>
/// <param name="HasPostalCodes">
/// <c>true</c> if the country uses a postal code system;
/// <c>false</c> for countries like the UAE that do not.
/// </param>
/// <param name="CodeRegex">
/// Input-time regular expression for validating raw postal code strings before
/// normalisation, e.g. <c>^\d{5}(?:-\d{4})?$</c> for the US (accepts ZIP+4).
/// <c>null</c> when no regex is defined.
/// </param>
/// <param name="ConstrainedRegex">
/// A stricter variant of <see cref="CodeRegex"/> that excludes known-invalid patterns,
/// e.g. the Canadian regex that omits letters D, F, I, O, Q, U.
/// <c>null</c> when no constraint exists.
/// </param>
/// <param name="ConstraintNotes">
/// Human-readable description of what the constrained regex excludes, e.g.
/// <c>"Letters D, F, I, O, Q, U not used"</c>. <c>null</c> when not applicable.
/// </param>
/// <param name="CodeCount">
/// Number of postal code entries currently in the built-in registry for this country.
/// <c>0</c> for countries whose data has not yet been added.
/// </param>
/// <param name="DataCurated">
/// <c>true</c> when the reference data has been fully validated and signed off.
/// <c>false</c> means the data is a working reference that may contain inaccuracies.
/// </param>
/// <param name="CurationStatus">
/// Current stage in the curation lifecycle. See <see cref="CurationStatus"/> for values.
/// </param>
/// <param name="Notes">
/// Free-text notes about this country's postal code system or the current state
/// of the reference data. May be <c>null</c>.
/// </param>
public sealed record CountryInfo(
    string         CountryId,
    string         CountryName,
    bool           Enabled,
    bool           HasPostalCodes,
    string?        CodeRegex,
    string?        ConstrainedRegex,
    string?        ConstraintNotes,
    int            CodeCount,
    bool           DataCurated,
    CurationStatus CurationStatus,
    string?        Notes
)
{
    /// <summary>
    /// Returns a short human-readable summary, e.g.
    /// <c>"US — United States (41 683 codes, curated)"</c>.
    /// </summary>
    public override string ToString()
    {
        var status = DataCurated ? "curated" : CurationStatus.ToString().ToLowerInvariant().Replace('_', ' ');
        return $"{CountryId} — {CountryName} ({CodeCount:N0} codes, {status})";
    }
}

/// <summary>
/// The stage in the data curation lifecycle for a country's reference data.
/// </summary>
public enum CurationStatus
{
    /// <summary>No reference CSV has been loaded yet.</summary>
    NoData,

    /// <summary>Data exists but has not been through a full validation cycle.</summary>
    UnderReview,

    /// <summary>Validation pipeline completed and issues documented; not yet signed off.</summary>
    Reviewed,

    /// <summary>Fully validated, signed off, and safe to treat as a source of truth.</summary>
    Curated,
}
