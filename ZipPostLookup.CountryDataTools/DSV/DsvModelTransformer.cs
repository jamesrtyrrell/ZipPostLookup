using System.Globalization;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ZipPostLookup.CountryDataTools.Models.Dsv;

namespace ZipPostLookup.CountryDataTools.DSV;

/// <summary>
/// Maps any <see cref="IDsvModel"/> to any other <see cref="IDsvModel"/> using AutoMapper.
/// <para>
/// Use <see cref="Create"/> for a self-contained instance.
/// Pass an externally-configured <see cref="IMapper"/> (registered with
/// <see cref="DsvMappingProfile"/>) via the constructor when using a shared mapper.
/// </para>
/// </summary>
public sealed class DsvModelTransformer
{
    private readonly IMapper _mapper;

    public DsvModelTransformer(IMapper mapper) => _mapper = mapper;

    /// <summary>
    /// Creates a self-contained transformer with <see cref="DsvMappingProfile"/> pre-loaded
    /// and configuration validated at construction time.
    /// </summary>
    public static DsvModelTransformer Create()
    {
        // AutoMapper 16 requires an ILoggerFactory; NullLoggerFactory suppresses all output.
        var config = new MapperConfiguration(
            cfg => cfg.AddProfile<DsvMappingProfile>(),
            NullLoggerFactory.Instance);
        config.AssertConfigurationIsValid();
        return new(new Mapper(config));
    }

    /// <summary>Maps <paramref name="source"/> to <typeparamref name="TDest"/>.</summary>
    public TDest Map<TSource, TDest>(TSource source)
        where TSource : IDsvModel
        where TDest   : IDsvModel
        => _mapper.Map<TDest>(source);

    /// <summary>
    /// Convenience overload — maps any TSV input model to <see cref="CandidateDataCSV"/>.
    /// This is the primary path for the <c>convert</c> command output.
    /// </summary>
    public CandidateDataCSV ToCandidateData<T>(T source) where T : ITsvDsvModel
        => _mapper.Map<CandidateDataCSV>(source);
}

/// <summary>
/// AutoMapper profile covering all DSV-model-to-DSV-model mappings.
/// Register this profile when sharing an <see cref="IMapper"/> instance across the app.
/// </summary>
public sealed class DsvMappingProfile : Profile
{
    public DsvMappingProfile()
    {
        // ── External TSV → CandidateDataCSV ──────────────────────────────────

        CreateMap<GeoNamesDataTSV, CandidateDataCSV>()
            .ForMember(d => d.ZpCode,     o => o.MapFrom(s => s.PostalCode))
            .ForMember(d => d.PlaceName,  o => o.MapFrom(s => s.PlaceName))
            .ForMember(d => d.Timezone,   o => o.MapFrom(_ => "---"))
            .ForMember(d => d.Lat,        o => o.MapFrom(s => s.Latitude))
            .ForMember(d => d.Lng,        o => o.MapFrom(s => s.Longitude))
            .ForMember(d => d.Admin1,     o => o.MapFrom(s => NullIfDash(s.AdminName1)))
            .ForMember(d => d.Admin1Code, o => o.MapFrom(s => NullIfDash(s.AdminCode1)))
            .ForMember(d => d.Admin2,     o => o.MapFrom(s => NullIfDash(s.AdminName2)))
            .ForMember(d => d.Admin2Code, o => o.MapFrom(s => NullIfDash(s.AdminCode2)))
            .ForMember(d => d.Admin3,     o => o.MapFrom(s => NullIfDash(s.AdminName3)))
            .ForMember(d => d.Admin3Code, o => o.MapFrom(s => NullIfDash(s.AdminCode3)))
            .ForMember(d => d.Admin4,     o => o.Ignore())
            .ForMember(d => d.Admin4Code, o => o.Ignore())
            .ForMember(d => d.Admin5,     o => o.Ignore())
            .ForMember(d => d.Admin5Code, o => o.Ignore());

        CreateMap<OsmStreetsDataTSV, CandidateDataCSV>()
            .ForMember(d => d.ZpCode,     o => o.MapFrom(s => s.PostalCode))
            .ForMember(d => d.PlaceName,  o => o.MapFrom(s => s.City))
            .ForMember(d => d.Timezone,   o => o.MapFrom(_ => "---"))
            .ForMember(d => d.Lat,        o => o.MapFrom(_ => "---"))
            .ForMember(d => d.Lng,        o => o.MapFrom(_ => "---"))
            // State takes priority; Province is the CA equivalent
            .ForMember(d => d.Admin1,     o => o.MapFrom(s => s.State ?? s.Province))
            .ForMember(d => d.Admin1Code, o => o.MapFrom(_ => "---"))
            .ForMember(d => d.Admin2,     o => o.Ignore())
            .ForMember(d => d.Admin2Code, o => o.Ignore())
            .ForMember(d => d.Admin3,     o => o.Ignore())
            .ForMember(d => d.Admin3Code, o => o.Ignore())
            .ForMember(d => d.Admin4,     o => o.Ignore())
            .ForMember(d => d.Admin4Code, o => o.Ignore())
            .ForMember(d => d.Admin5,     o => o.Ignore())
            .ForMember(d => d.Admin5Code, o => o.Ignore());

        // Lat/Lng are midpoints of the bounding box for a single address row.
        // Multi-row averaging (as done in ConvertOsmAddressesAsync) requires
        // grouping before mapping and is outside the scope of per-row transformation.
        CreateMap<OsmAddressesDataTSV, CandidateDataCSV>()
            .ForMember(d => d.ZpCode,     o => o.MapFrom(s => s.PostalCode))
            .ForMember(d => d.PlaceName,  o => o.MapFrom(s => s.City))
            .ForMember(d => d.Timezone,   o => o.MapFrom(_ => "---"))
            .ForMember(d => d.Lat,        o => o.MapFrom(s => MidpointString(s.YMin, s.YMax)))
            .ForMember(d => d.Lng,        o => o.MapFrom(s => MidpointString(s.XMin, s.XMax)))
            .ForMember(d => d.Admin1,     o => o.MapFrom(_ => "---"))
            .ForMember(d => d.Admin1Code, o => o.MapFrom(_ => "---"))
            .ForMember(d => d.Admin2,     o => o.Ignore())
            .ForMember(d => d.Admin2Code, o => o.Ignore())
            .ForMember(d => d.Admin3,     o => o.Ignore())
            .ForMember(d => d.Admin3Code, o => o.Ignore())
            .ForMember(d => d.Admin4,     o => o.Ignore())
            .ForMember(d => d.Admin4Code, o => o.Ignore())
            .ForMember(d => d.Admin5,     o => o.Ignore())
            .ForMember(d => d.Admin5Code, o => o.Ignore());

        // X/Y are per-house coordinates; caller should average across a (code, city) group
        // before mapping if a representative centroid is needed.
        CreateMap<OsmHousesDataTSV, CandidateDataCSV>()
            .ForMember(d => d.ZpCode,     o => o.MapFrom(s => s.PostalCode))
            .ForMember(d => d.PlaceName,  o => o.MapFrom(s => s.City))
            .ForMember(d => d.Timezone,   o => o.MapFrom(_ => "---"))
            .ForMember(d => d.Lat,        o => o.MapFrom(s => s.Y ?? "---"))
            .ForMember(d => d.Lng,        o => o.MapFrom(s => s.X ?? "---"))
            .ForMember(d => d.Admin1,     o => o.MapFrom(_ => "---"))
            .ForMember(d => d.Admin1Code, o => o.MapFrom(_ => "---"))
            .ForMember(d => d.Admin2,     o => o.Ignore())
            .ForMember(d => d.Admin2Code, o => o.Ignore())
            .ForMember(d => d.Admin3,     o => o.Ignore())
            .ForMember(d => d.Admin3Code, o => o.Ignore())
            .ForMember(d => d.Admin4,     o => o.Ignore())
            .ForMember(d => d.Admin4Code, o => o.Ignore())
            .ForMember(d => d.Admin5,     o => o.Ignore())
            .ForMember(d => d.Admin5Code, o => o.Ignore());

        // ── CandidateDataCSV ↔ ReferenceDataCSV ──────────────────────────────

        // ReferenceDataCSV has non-nullable Lat/Lng/Admin1/Admin1Code plus
        // three bool fields absent from CandidateDataCSV.
        CreateMap<CandidateDataCSV, ReferenceDataCSV>()
            .ForMember(d => d.Lat,             o => o.MapFrom(s => s.Lat       ?? "---"))
            .ForMember(d => d.Lng,             o => o.MapFrom(s => s.Lng       ?? "---"))
            .ForMember(d => d.Admin1,          o => o.MapFrom(s => s.Admin1    ?? "---"))
            .ForMember(d => d.Admin1Code,      o => o.MapFrom(s => s.Admin1Code ?? "---"))
            .ForMember(d => d.IsDefault,       o => o.MapFrom(_ => false))
            .ForMember(d => d.TimezoneChecked, o => o.MapFrom(_ => false))
            .ForMember(d => d.NameChecked,     o => o.MapFrom(_ => false));
        // ZpCode, PlaceName, Timezone, Admin2-5 auto-map by name.

        // ReferenceDataCSV.Lat/Admin1 etc. are non-nullable strings → string? widens safely.
        CreateMap<ReferenceDataCSV, CandidateDataCSV>();
        // IsDefault / TimezoneChecked / NameChecked are source-only extras; AutoMapper ignores them.

        // ── CandidateDataCSV ↔ ZipPostLookupDataCSV ──────────────────────────

        CreateMap<CandidateDataCSV, ZipPostLookupDataCSV>()
            .ForMember(d => d.IsDefault,  o => o.MapFrom(_ => false))
            .ForMember(d => d.Admin1,     o => o.MapFrom(s => s.Admin1    ?? "---"))
            .ForMember(d => d.Admin1Code, o => o.MapFrom(s => s.Admin1Code ?? "---"));
        // ZpCode, PlaceName, Timezone auto-map. Lat/Lng/Admin2-5 are source-only extras.

        CreateMap<ZipPostLookupDataCSV, CandidateDataCSV>()
            .ForMember(d => d.Lat,        o => o.MapFrom(_ => "---"))
            .ForMember(d => d.Lng,        o => o.MapFrom(_ => "---"))
            .ForMember(d => d.Admin2,     o => o.Ignore())
            .ForMember(d => d.Admin2Code, o => o.Ignore())
            .ForMember(d => d.Admin3,     o => o.Ignore())
            .ForMember(d => d.Admin3Code, o => o.Ignore())
            .ForMember(d => d.Admin4,     o => o.Ignore())
            .ForMember(d => d.Admin4Code, o => o.Ignore())
            .ForMember(d => d.Admin5,     o => o.Ignore())
            .ForMember(d => d.Admin5Code, o => o.Ignore());
        // ZpCode, PlaceName, Timezone, Admin1, Admin1Code auto-map. IsDefault is source-only.

        // ── ReferenceDataCSV ↔ ZipPostLookupDataCSV ──────────────────────────

        // All six ZipPostLookupDataCSV properties exist in ReferenceDataCSV by the same name.
        CreateMap<ReferenceDataCSV, ZipPostLookupDataCSV>();

        CreateMap<ZipPostLookupDataCSV, ReferenceDataCSV>()
            .ForMember(d => d.Lat,             o => o.MapFrom(_ => "---"))
            .ForMember(d => d.Lng,             o => o.MapFrom(_ => "---"))
            .ForMember(d => d.TimezoneChecked, o => o.MapFrom(_ => false))
            .ForMember(d => d.NameChecked,     o => o.MapFrom(_ => false))
            .ForMember(d => d.Admin2,          o => o.Ignore())
            .ForMember(d => d.Admin2Code,      o => o.Ignore())
            .ForMember(d => d.Admin3,          o => o.Ignore())
            .ForMember(d => d.Admin3Code,      o => o.Ignore())
            .ForMember(d => d.Admin4,          o => o.Ignore())
            .ForMember(d => d.Admin4Code,      o => o.Ignore())
            .ForMember(d => d.Admin5,          o => o.Ignore())
            .ForMember(d => d.Admin5Code,      o => o.Ignore());
        // ZpCode, PlaceName, Timezone, IsDefault, Admin1, Admin1Code auto-map.
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Returns null when <paramref name="s"/> is null, empty, or the sentinel "---".</summary>
    private static string? NullIfDash(string? s) =>
        string.IsNullOrWhiteSpace(s) || s == "---" ? null : s;

    /// <summary>
    /// Returns the midpoint of two coordinate strings as a 4-decimal string,
    /// or "---" if either value is missing or unparseable.
    /// </summary>
    private static string MidpointString(string? a, string? b)
    {
        if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var da) &&
            double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var db))
            return ((da + db) / 2.0).ToString("F4", CultureInfo.InvariantCulture);
        return "---";
    }
}
