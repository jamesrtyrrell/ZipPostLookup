namespace ZipPostLookup.CountryDataTools.Models.Dsv;

/// <summary>
/// A single row from the coordinate enrichment CSV (passed to <c>ingest coords --source</c>).
/// Accepted column layouts (header required):
///   ZIP, LAT, LNG
///   ZIP, CITY, STATE, LAT, LNG
/// <c>PlaceName</c> and <c>Timezone</c> are optional — present only in the extended layout.
/// </summary>
public sealed record CoordRow(
    string  ZpCode,
    double  Lat,
    double  Lng,
    string? PlaceName,
    string? Timezone
) : IDsvModel;
