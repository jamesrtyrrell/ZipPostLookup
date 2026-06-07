#if NET8_0_OR_GREATER
using System.Reflection;
using System.Security.Cryptography;
using ZipPostLookup.Core;
using ZipPostLookup.Normalizers;

namespace ZipPostLookup.ZPImage;

/// <summary>
/// An <see cref="IZipPostLookup"/> backed by a <see cref="ZpImageReader">ZP frozen image</see>
/// instead of the CSV-built <see cref="ZipPostRegistry"/>. Loading is a decompress-and-map step
/// with no per-row object construction; exact code lookups resolve in O(1) through the embedded
/// minimal perfect hash and are allocation-free once a code's entry has been materialised.
///
/// <para>Lookup semantics mirror <see cref="ZipPostRegistry"/>: a raw exact match is tried first,
/// then each country normalisation rule, then the range fallback — so results match the
/// CSV-backed registry for the same data.</para>
///
/// <para>Available on .NET 8.0+ only.</para>
/// </summary>
public sealed class ZpImageLookup : IZipPostLookup
{
    private readonly ZpImageReader _reader;
    private readonly ICountryCodeRules[] _rules;

    // The executing assembly's manifest resource names never change for the process lifetime;
    // cache them so each FromBuiltIn call doesn't re-run reflection (it previously enumerated the
    // manifest up to three times per call).
    private static string[]? _manifestResourceNames;

    internal ZpImageLookup(ZpImageReader reader, IEnumerable<ICountryCodeRules> rules)
    {
        _reader = reader;
        _rules  = rules.ToArray();
    }

    // =========================================================================
    // Factories
    // =========================================================================

    /// <summary>
    /// Loads the built-in frozen image for <paramref name="country"/> from the embedded
    /// <c>Data/{cc}/{cc}.zpi.br</c> resource.
    /// </summary>
    /// <exception cref="NotSupportedException">No embedded image exists for the country.</exception>
    public static ZpImageLookup FromBuiltIn(CountryCode country)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var cc       = ((string)country).ToLowerInvariant();

        var resourceNames = _manifestResourceNames ??= assembly.GetManifestResourceNames();

        // Prefer the filename-tagged v2 formats (.u16 / .u32), then fall back to legacy .zpi.br.
        // The suffix encodes the nameIndex width so the reader can choose the right record stride.
        string? resourceName = null;
        var u32NameIndex = false;

        foreach (var (suffix, isU32) in new[] { ($"{cc}.u16.zpi.br", false), ($"{cc}.u32.zpi.br", true) })
        {
            resourceName = resourceNames
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (resourceName != null) { u32NameIndex = isU32; break; }
        }

        // Legacy fallback for images built before the filename-tagged format.
        resourceName ??= resourceNames
            .FirstOrDefault(n => n.EndsWith($"{cc}.zpi.br", StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
        {
            throw new NotSupportedException(
                $"No built-in frozen image exists for country '{country}'. " +
                $"Generate one with 'countrydatatools export --country {(string)country} --target zpi' " +
                $"and embed it at Data/{cc}/{cc}.u16.zpi.br.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        var reader = ZpImageReader.LoadCompressed(stream, LoadLevelName(assembly, cc), u32NameIndex);
        return new ZpImageLookup(reader, DefaultRules());
    }

    /// <summary>
    /// Loads a lookup from a Brotli-compressed image file on disk.
    /// </summary>
    /// <param name="path">Path to the <c>.zpi.br</c> file.</param>
    /// <param name="levelName">Admin-1 level name for this country (e.g. "State").</param>
    /// <param name="validate">
    /// When <c>true</c>, runs a structural integrity pass at load (bounds-checks all sections and
    /// on-disk indices). Recommended for images from an untrusted source; off by default to keep
    /// the load path fast for trusted files.
    /// </param>
    /// <param name="verifyHash">
    /// When <c>true</c>, verifies the file against its <c>{path}.sha256</c> sidecar before loading
    /// and throws if it is missing or does not match.
    /// </param>
    public static ZpImageLookup FromFile(string path, string levelName = "Admin1",
        bool validate = false, bool verifyHash = false)
    {
        if (verifyHash) { VerifySidecarHash(path); }

        // Detect nameIndex width from filename: .u32. → u32, otherwise → u16 (default).
        var u32NameIndex = path.Contains(".u32.", StringComparison.OrdinalIgnoreCase);
        using var stream = File.OpenRead(path);
        var reader = ZpImageReader.LoadCompressed(stream, levelName, u32NameIndex, validate);
        return new ZpImageLookup(reader, DefaultRules());
    }

    /// <summary>
    /// Wraps an already-loaded reader (raw bytes), for tests and in-memory use. Pass
    /// <paramref name="validate"/> <c>true</c> to bounds-check an image from an untrusted source.
    /// </summary>
    public static ZpImageLookup FromImage(byte[] image, string levelName = "Admin1",
        bool validate = false) =>
        new(ZpImageReader.Load(image, levelName, validate: validate), DefaultRules());

    private static void VerifySidecarHash(string path)
    {
        var sidecar = path + ".sha256";
        if (!File.Exists(sidecar))
        {
            throw new FileNotFoundException(
                $"Hash verification requested but the sidecar '{Path.GetFileName(sidecar)}' was not found.",
                sidecar);
        }

        var expected = File.ReadAllText(sidecar).Trim();
        var actual   = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"ZP image hash mismatch for '{Path.GetFileName(path)}': expected {expected}, got {actual}. " +
                "The file is corrupt or has been modified.");
        }
    }

    private static IEnumerable<ICountryCodeRules> DefaultRules() =>
        new ICountryCodeRules[]
        {
            new UsCountryCodeRules(),
            new CaCountryCodeRules(),
            new MxCountryCodeRules(),
        };

    // =========================================================================
    // Code lookups
    // =========================================================================

    /// <inheritdoc/>
    public CodeEntry? GetByCode(string? code)
    {
        if (code == null) { return null;}

        var hit = _reader.GetByCodeExact(code);
        if (hit != null) { return hit; }

        foreach (var rule in _rules)
        {
            var normalized = rule.Normalize(code);
            if (!string.Equals(normalized, code, StringComparison.OrdinalIgnoreCase) &&
                rule.Validate(normalized))
            {
                hit = _reader.GetByCodeExact(normalized);
                if (hit != null) { return hit; }
            }
        }

        return _reader.GetByRange(code);
    }

    /// <inheritdoc/>
    public bool TryGetByCode(string? code, out CodeEntry? entry)
    {
        entry = GetByCode(code);
        return entry is not null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetAllByCode(string code)
    {
        if (code == null) { throw new ArgumentNullException(nameof(code)); }

        var list = _reader.GetAllByCodeExact(code);
        if (list != null) { return list; }

        foreach (var rule in _rules)
        {
            var normalized = rule.Normalize(code);
            if (!string.Equals(normalized, code, StringComparison.OrdinalIgnoreCase) &&
                rule.Validate(normalized))
            {
                list = _reader.GetAllByCodeExact(normalized);
                if (list != null) { return list; }
            }
        }

        return _reader.GetAllByRange(code) ?? (IReadOnlyList<CodeEntry>)Array.Empty<CodeEntry>();
    }

    /// <inheritdoc/>
    public CodeEntry? GetByZip(string zip) => GetByCode(zip);

    /// <inheritdoc/>
    public bool TryGetByZip(string zip, out CodeEntry? entry) => TryGetByCode(zip, out entry);

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetAllByZip(string zip) => GetAllByCode(zip);

    // =========================================================================
    // Closest (US)
    // =========================================================================

    /// <inheritdoc/>
    public CodeEntry? GetClosest(string zip)
    {
        if (zip == null) { throw new ArgumentNullException(nameof(zip)); }

        if (zip.Length != 5 || !IsAllDigits(zip))
        {
            throw new ArgumentException(
                $"GetClosest only supports 5-digit US zip codes. '{zip}' is not valid.",
                nameof(zip));
        }

        return _reader.GetClosest(zip, out _);
    }

    // =========================================================================
    // Reverse lookups
    // =========================================================================

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetByName(string name)
    {
        if (name == null) { throw new ArgumentNullException(nameof(name)); }
        return _reader.GetByName(name);
    }

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetByCity(string city) => GetByName(city);

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetByState(string admin1Code)
    {
        if (admin1Code == null) { throw new ArgumentNullException(nameof(admin1Code)); }
        return _reader.GetByState(admin1Code);
    }

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetByStateName(string admin1Name)
    {
        if (admin1Name == null) { throw new ArgumentNullException(nameof(admin1Name)); }
        return _reader.GetByStateName(admin1Name);
    }

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetByAdmin(string value, string levelName)
    {
        if (value == null) { throw new ArgumentNullException(nameof(value)); }
        if (levelName == null) { throw new ArgumentNullException(nameof(levelName)); }
        return _reader.GetByAdmin(value, levelName);
    }

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetByTimeZone(string timezone)
    {
        if (timezone == null) { throw new ArgumentNullException(nameof(timezone)); }
        return _reader.GetByTimeZone(timezone);
    }

    // =========================================================================
    // Enumeration / random / batch
    // =========================================================================

    /// <inheritdoc/>
    public CodeEntry GetRandom() => All[Random.Shared.Next(All.Count)];

    /// <inheritdoc/>
    public CodeEntry GetRandom(Random random)
    {
        if (random == null) { throw new ArgumentNullException(nameof(random)); }
        return All[random.Next(All.Count)];
    }

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetAll() => All;

    private IReadOnlyList<CodeEntry> All => _reader.All;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, CodeEntry?> GetBatch(CodeBatch batch)
    {
        if (batch == null) { throw new ArgumentNullException(nameof(batch)); }
        return ExecuteBatch(batch);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, CodeEntry?> GetBatch(IEnumerable<string> codes)
    {
        if (codes == null) { throw new ArgumentNullException(nameof(codes)); }
        return ExecuteBatch(new CodeBatch(codes));
    }

    private IReadOnlyDictionary<string, CodeEntry?> ExecuteBatch(CodeBatch batch)
    {
        if (batch.IsEmpty)
        {
            return new Dictionary<string, CodeEntry?>(0);
        }

        var results = new Dictionary<string, CodeEntry?>(batch.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var code in batch)
        {
            results[code] = GetByCode(code);
        }

        return results;
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static string LoadLevelName(Assembly assembly, string cc) =>
        CountryInfoSource.GetAdminLevelNames(cc).FirstOrDefault() ?? "Admin1";

    private static bool IsAllDigits(string value)
    {
        foreach (var c in value)
        {
            if (c is < '0' or > '9') { return false; }
        }

        return true;
    }
}
#endif
