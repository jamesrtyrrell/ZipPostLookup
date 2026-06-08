using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Validation.Mx;

/// <summary>
/// MX-specific domain rules.
///
/// Mexican postal codes are partitioned deterministically by estado — the first
/// two digits of the 5-digit ZIP identify the state. <see cref="ResolveAdmin1"/>
/// uses this to return a canonical (Code, Name) pair without any API call.
/// Ranges sourced from SEPOMEX / Wikipedia "Postal codes in Mexico".
///
/// CMX covers two discontiguous bands:
///   00001–16999  (01xxx–16xxx; 00xxx parse to 1–999 after leading-zero strip)
///   17000–19999  (CDMX overflow — previously documented as "unassigned" but
///                 all confirmed SEPOMEX CDMX codes)
/// </summary>
public sealed class MxCountryRules : ICountryRules
{
    public PipelineCountry Country                       => PipelineCountry.MX;
    public bool IsKnownSpecialCode(string code)          => false;
    public bool IsKnownSpecialName(string name)          => false;
    public string? GetDomainLabel(string code)           => null;
    public bool IsEnrichmentSkipped(string code)         => false;
    public bool IsCoordResolutionSkipped(string code)    => false;

    public IReadOnlyList<string> PlaceNameLanguages => ["Spanish"];

    public (string Code, string Name)? ResolveAdmin1(string zpCode)
    {
        if (!int.TryParse(zpCode, out var n)) return null;
        return n switch
        {
            // 00001–16999: leading zeros parsed away (00xxx → 1–999), all CMX
            >= 1     and <= 16999 => ("CMX", "Ciudad de México"),
            // 17000–19999: CDMX overflow codes (SEPOMEX confirmed)
            >= 17000 and <= 19999 => ("CMX", "Ciudad de México"),
            >= 20000 and <= 20999 => ("AGS", "Aguascalientes"),
            >= 21000 and <= 22999 => ("BC",  "Baja California"),
            >= 23000 and <= 23999 => ("BCS", "Baja California Sur"),
            >= 24000 and <= 24999 => ("CAM", "Campeche"),
            >= 25000 and <= 27999 => ("COA", "Coahuila de Zaragoza"),
            >= 28000 and <= 28999 => ("COL", "Colima"),
            >= 29000 and <= 30999 => ("CHP", "Chiapas"),
            >= 31000 and <= 33999 => ("CHH", "Chihuahua"),
            >= 34000 and <= 35999 => ("DUR", "Durango"),
            >= 36000 and <= 38999 => ("GTO", "Guanajuato"),
            >= 39000 and <= 41999 => ("GRO", "Guerrero"),
            >= 42000 and <= 43999 => ("HID", "Hidalgo"),
            >= 44000 and <= 49999 => ("JAL", "Jalisco"),
            >= 50000 and <= 57999 => ("MEX", "Estado de México"),
            >= 58000 and <= 61999 => ("MIC", "Michoacán de Ocampo"),
            >= 62000 and <= 62999 => ("MOR", "Morelos"),
            >= 63000 and <= 63999 => ("NAY", "Nayarit"),
            >= 64000 and <= 67999 => ("NLE", "Nuevo León"),
            >= 68000 and <= 71999 => ("OAX", "Oaxaca"),
            >= 72000 and <= 75999 => ("PUE", "Puebla"),
            >= 76000 and <= 76999 => ("QRO", "Querétaro"),
            >= 77000 and <= 77999 => ("ROO", "Quintana Roo"),
            >= 78000 and <= 79999 => ("SLP", "San Luis Potosí"),
            >= 80000 and <= 82999 => ("SIN", "Sinaloa"),
            >= 83000 and <= 85999 => ("SON", "Sonora"),
            >= 86000 and <= 86999 => ("TAB", "Tabasco"),
            >= 87000 and <= 89999 => ("TAM", "Tamaulipas"),
            >= 90000 and <= 90999 => ("TLA", "Tlaxcala"),
            >= 91000 and <= 96999 => ("VER", "Veracruz"),
            >= 97000 and <= 97999 => ("YUC", "Yucatán"),
            >= 98000 and <= 99999 => ("ZAC", "Zacatecas"),
            _                     => null,
        };
    }
}
