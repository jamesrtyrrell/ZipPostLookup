namespace ZipPostLookup.CountryDataTools.Models.Dsv;

/// <summary>
/// Minimum contract shared by all external TSV input models:
/// <see cref="GeoNamesDataTSV"/>, <see cref="OsmStreetsDataTSV"/>,
/// <see cref="OsmAddressesDataTSV"/>, <see cref="OsmHousesDataTSV"/>.
/// <para>
/// <see cref="City"/> unifies different field names across sources:
/// GeoNames exposes <c>PlaceName</c> and implements this via an explicit interface member;
/// OSM models already have a <c>City</c> property and satisfy it directly.
/// </para>
/// <para>
/// Each implementing type also provides static members <see cref="Detect"/> and
/// <see cref="FormatName"/> so callers can discover the right format without a
/// central switch — see <c>ConvertKnownFormatsCommand._formats</c>.
/// </para>
/// </summary>
public interface ITsvDsvModel : IDsvModel
{
    string PostalCode { get; }

    /// <summary>
    /// Place / locality name. Implemented as <c>PlaceName</c> on GeoNamesDataTSV
    /// and as <c>City</c> on the OSM variants.
    /// </summary>
    string City { get; }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="firstLine"/> (the first line of an
    /// input file) matches the heuristic for this format.
    /// </summary>
    static abstract bool Detect(string firstLine);

    /// <summary>Human-readable format name used in console prompts and error messages.</summary>
    static abstract string FormatName { get; }
}
