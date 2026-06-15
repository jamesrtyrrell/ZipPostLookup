#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#endif

using System.Collections.ObjectModel;
using ZipPostLookup.Normalizers;
using ZipPostLookup.Sources;

namespace ZipPostLookup.Core;

/// <summary>
/// An in-memory postal code registry providing fast indexed lookups across one or more countries.
/// </summary>
/// <remarks>
/// <para>
/// On construction, all entries from the supplied data sources are loaded into five in-memory
/// indexes (by code, name, admin1 code, admin1 name, and timezone), enabling O(1) average
/// lookups for all query types.
/// </para>
/// <para>
/// For countries using range notation in their CSV (e.g. Canada), a secondary range index
/// is built at construction time. Range lookups fall back to this index when an exact code
/// match is not found, providing transparent O(1) FSA-level matching for Canadian codes.
/// </para>
/// <para>
/// <b>Thread safety:</b> All public members are thread-safe after construction.
/// The registry is immutable once built.
/// </para>
/// </remarks>
public sealed class ZipPostRegistry : IZipPostLookup
{
    private static readonly Lazy<ZipPostRegistry> _default =
        new(() => new ZipPostRegistry(CountryCode.US), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The default registry loaded from the built-in US data.
    /// Initialised lazily on first access and shared for the lifetime of the application.
    /// </summary>
    public static ZipPostRegistry Default => _default.Value;

    // ── Primary indexes ───────────────────────────────────────────────────────
#if NET8_0_OR_GREATER
    private readonly FrozenDictionary<string, List<CodeEntry>> _byCode;
    private readonly FrozenDictionary<string, List<CodeEntry>> _byName;
    private readonly FrozenDictionary<string, List<CodeEntry>> _byAdmin1Code;
    private readonly FrozenDictionary<string, List<CodeEntry>> _byAdmin1Name;
    private readonly FrozenDictionary<string, List<CodeEntry>> _byAdmin;   // key: "levelname\0value"
    private readonly FrozenDictionary<string, List<CodeEntry>> _byTimeZone;
#else
    private readonly Dictionary<string, List<CodeEntry>> _byCode;
    private readonly Dictionary<string, List<CodeEntry>> _byName;
    private readonly Dictionary<string, List<CodeEntry>> _byAdmin1Code;
    private readonly Dictionary<string, List<CodeEntry>> _byAdmin1Name;
    private readonly Dictionary<string, List<CodeEntry>> _byAdmin;
    private readonly Dictionary<string, List<CodeEntry>> _byTimeZone;
#endif

    // ── Range index for compressed codes (e.g. "B2G0**:B2G9**") ──────────────
    // Key: FSA prefix (first 3 chars), Value: list of (start4, end4, entry) tuples
    private readonly Dictionary<string, List<(string Start4, string End4, CodeEntry Entry)>> _rangeIndex;

    // ── Sorted int array for GetClosest (US only) ─────────────────────────────
    private readonly int[] _sortedUsZipInts;

    // ── Flat ordered list for GetAll and GetRandom ────────────────────────────
    private readonly ReadOnlyCollection<CodeEntry> _all;

    // ── Normalisation rules ───────────────────────────────────────────────────
    private readonly ReadOnlyCollection<ICountryCodeRules> _rules;

    private static readonly ICountryCodeRules _usRules = new UsCountryCodeRules();
    private static readonly ICountryCodeRules _caRules = new CaCountryCodeRules();
    private static readonly ICountryCodeRules _mxRules = new MxCountryCodeRules();

    // ── Opt-in reason exceptions (default off — behaviour unchanged) ───────────
    private volatile bool _throwReasonExceptions;

    /// <summary>
    /// Opts this registry in (or out) of throwing <see cref="CodeReasonException"/>s from the
    /// code-lookup path instead of returning a silent <c>null</c>/empty miss. Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// When enabled, <see cref="GetByCode"/> / <see cref="GetAllByCode"/> (and their
    /// <c>GetByZip</c> aliases) throw <see cref="BadlyFormattedCodeException"/> for input that is
    /// not a valid format for any supported country (US, CA, MX). A well-formed code that simply
    /// is not present still returns <c>null</c>/empty. Thread-safe; may be toggled at any time.
    /// </remarks>
    public void ThrowReasonExceptions(bool enabled) => _throwReasonExceptions = enabled;

    private static void ThrowIfFlagged(CodeEntry entry)
    {
        switch (entry.Reason)
        {
            case CodeReason.Obsolete:   throw new ObsoleteCodeException(entry.ZpCode);
            case CodeReason.CommonFake: throw new CommonFakeDataException(entry.ZpCode);
        }
    }

    /// <summary>
    /// True when <paramref name="code"/> is a valid format for any supported country rule
    /// (raw or normalised). Used to decide whether a miss is a malformed-input case.
    /// </summary>
    private bool IsWellFormedCode(string code)
    {
        foreach (var rule in _rules)
        {
            if (rule.Validate(code))
            {
                return true;
            }

            var normalized = rule.Normalize(code);

            if (rule.Validate(normalized))
            {
                return true;
            }
        }

        return false;
    }

    // =========================================================================
    // Constructors
    // =========================================================================

    /// <summary>
    /// Constructs a registry from one or more country codes using their built-in data.
    /// </summary>
    public ZipPostRegistry(params CountryCode[] countries)
        : this(
            sources: countries.Select(c => (ICodeDataSource)new BuiltInDataSource(c)),
            rules: BuildDefaultRules(countries))
    {
    }

    /// <summary>
    /// Constructs a registry from explicit data sources using default built-in rules.
    /// </summary>
    public ZipPostRegistry(IEnumerable<ICodeDataSource> sources)
        : this(sources, rules: null)
    {
    }

    /// <summary>
    /// Constructs a registry from explicit data sources and optional rule overrides.
    /// </summary>
    public ZipPostRegistry(IEnumerable<ICodeDataSource> sources, IEnumerable<ICountryCodeRules>? rules)
    {
        var byCode = new Dictionary<string, List<CodeEntry>>(120_000, StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, List<CodeEntry>>(100_000, StringComparer.OrdinalIgnoreCase);
        var byAdmin1Code = new Dictionary<string, List<CodeEntry>>(75, StringComparer.OrdinalIgnoreCase);
        var byAdmin1Name = new Dictionary<string, List<CodeEntry>>(75, StringComparer.OrdinalIgnoreCase);
        var byAdmin = new Dictionary<string, List<CodeEntry>>(200, StringComparer.OrdinalIgnoreCase);
        var byTimeZone = new Dictionary<string, List<CodeEntry>>(40, StringComparer.OrdinalIgnoreCase);
        var rangeIndex = new Dictionary<string, List<(string, string, CodeEntry)>>(1000, StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            LoadSource(source, byCode, byName, byAdmin1Code, byAdmin1Name, byAdmin, byTimeZone, rangeIndex);
        }

        // Entries were appended in load order; one linear pass puts defaults first in every
        // posting list (identical ordering to the old per-insert behaviour — see method doc).
        ReorderDefaultsFirst(byCode, byName, byAdmin1Code, byAdmin1Name, byAdmin, byTimeZone);

        _all = byCode.Values
            .SelectMany(e => e)
            .OrderBy(e => e.ZpCode)
            .ToList()
            .AsReadOnly();

#if NET8_0_OR_GREATER
        _byCode      = byCode.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _byName      = byName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _byAdmin1Code = byAdmin1Code.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _byAdmin1Name = byAdmin1Name.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _byAdmin     = byAdmin.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _byTimeZone  = byTimeZone.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
#else
        _byCode = byCode;
        _byName = byName;
        _byAdmin1Code = byAdmin1Code;
        _byAdmin1Name = byAdmin1Name;
        _byAdmin = byAdmin;
        _byTimeZone = byTimeZone;
#endif

        _rangeIndex = rangeIndex;
        _rules = MergeRules(rules);

        // Sorted int array of US zip codes for O(log n) GetClosest
        _sortedUsZipInts = byCode.Keys
            .Where(z => z.Length == 5 && IsAllDigits(z))
            .Select(z => int.Parse(z))
            .OrderBy(z => z)
            .ToArray();
    }

    // =========================================================================
    // Loading
    // =========================================================================

    private static void LoadSource(
        ICodeDataSource source,
        Dictionary<string, List<CodeEntry>> byCode,
        Dictionary<string, List<CodeEntry>> byName,
        Dictionary<string, List<CodeEntry>> byAdmin1Code,
        Dictionary<string, List<CodeEntry>> byAdmin1Name,
        Dictionary<string, List<CodeEntry>> byAdmin,
        Dictionary<string, List<CodeEntry>> byTimeZone,
        Dictionary<string, List<(string, string, CodeEntry)>> rangeIndex)
    {
        foreach (var entry in source.GetEntries())
        {
            var code = entry.ZpCode;

            // ── Range entry (e.g. "B2G0**:B2G9**") ───────────────────────────
            if (code.Contains(':'))
            {
                LoadRangeEntry(entry, rangeIndex, byName, byAdmin1Code, byAdmin1Name, byAdmin, byTimeZone);
                continue;
            }

            // ── Exact code entry ──────────────────────────────────────────────
            AddToIndex(byCode, code, entry);
            AddToIndex(byName, entry.PlaceName, entry);

            if (entry.Admin1Code is string a1c && a1c.Length > 0 && a1c != "---")
            {
                AddToIndex(byAdmin1Code, a1c, entry);
            }

            if (entry.Admin1 is string a1n && a1n.Length > 0 && a1n != "---")
            {
                AddToIndex(byAdmin1Name, a1n, entry);
            }

            foreach (var admin in entry.Admins)
            {
                if (!admin.IsEmpty)
                {
                    var keyByValue = $"{admin.LevelName}\0{admin.Value}";
                    var keyByCode = $"{admin.LevelName}\0{admin.Code}";
                    AddToIndex(byAdmin, keyByValue, entry);

                    if (admin.Code != admin.Value)
                    {
                        AddToIndex(byAdmin, keyByCode, entry);
                    }
                }
            }

            AddToIndex(byTimeZone, entry.Timezone, entry);
        }
    }

    private static void LoadRangeEntry(
        CodeEntry entry,
        Dictionary<string, List<(string, string, CodeEntry)>> rangeIndex,
        Dictionary<string, List<CodeEntry>> byName,
        Dictionary<string, List<CodeEntry>> byAdmin1Code,
        Dictionary<string, List<CodeEntry>> byAdmin1Name,
        Dictionary<string, List<CodeEntry>> byAdmin,
        Dictionary<string, List<CodeEntry>> byTimeZone)
    {
        // Parse "B2G0**:B2G9**" → Start4="B2G0", End4="B2G9", FSA="B2G"
        var colonIdx = entry.ZpCode.IndexOf(':');

        if (colonIdx < 0)
        {
            return;
        }

        var startPart = entry.ZpCode.Substring(0, colonIdx).Replace("*", "");
        var endPart = entry.ZpCode.Substring(colonIdx + 1).Replace("*", "");

        if (startPart.Length < 3 || endPart.Length < 3)
        {
            return;
        }

        var fsa = startPart.Substring(0, 3);

        if (!rangeIndex.TryGetValue(fsa, out var rangeList))
        {
            rangeList = new List<(string, string, CodeEntry)>();
            rangeIndex[fsa] = rangeList;
        }

        rangeList.Add((startPart, endPart, entry));

        // Index for reverse lookups — range entries still contribute to these
        AddToIndex(byName, entry.PlaceName, entry);

        if (entry.Admin1Code is string a1c && a1c.Length > 0 && a1c != "---")
        {
            AddToIndex(byAdmin1Code, a1c, entry);
        }

        if (entry.Admin1 is string a1n && a1n.Length > 0 && a1n != "---")
        {
            AddToIndex(byAdmin1Name, a1n, entry);
        }

        foreach (var admin in entry.Admins)
        {
            if (!admin.IsEmpty)
            {
                var keyByValue = $"{admin.LevelName}\0{admin.Value}";
                var keyByCode = $"{admin.LevelName}\0{admin.Code}";
                AddToIndex(byAdmin, keyByValue, entry);

                if (admin.Code != admin.Value)
                {
                    AddToIndex(byAdmin, keyByCode, entry);
                }
            }
        }

        AddToIndex(byTimeZone, entry.Timezone, entry);
    }

    private static void AddToIndex(Dictionary<string, List<CodeEntry>> index, string key, CodeEntry entry)
    {
        if (!index.TryGetValue(key, out var list))
        {
            list = new List<CodeEntry>(2);
            index[key] = list;
        }

        // Append in load order; ReorderDefaultsFirst moves defaults to the front afterwards.
        // (The previous per-default list.Insert(0, ...) was O(bucket size) per insert — quadratic
        // on CA's huge province/timezone buckets, accounting for ~32 s of the ~33 s CA build.)
        list.Add(entry);
    }

    /// <summary>
    /// Rebuilds every posting list as [defaults in reverse load order] + [non-defaults in load
    /// order] — element-for-element identical to the previous Insert(0)-per-default behaviour,
    /// but linear instead of quadratic in bucket size. Called once after all sources are loaded,
    /// before <c>_all</c> materialisation (whose stable OrderBy preserves within-code order).
    /// </summary>
    private static void ReorderDefaultsFirst(params Dictionary<string, List<CodeEntry>>[] indexes)
    {
        var buffer = new CodeEntry[16];

        foreach (var index in indexes)
        {
            foreach (var list in index.Values)
            {
                var count = list.Count;

                if (count < 2)
                {
                    continue;
                }

                var defaults = 0;

                for (var i = 0; i < count; i++)
                {
                    if (list[i].IsDefault)
                    {
                        defaults++;
                    }
                }

                if (defaults == 0)
                {
                    continue;
                }

                if (buffer.Length < count)
                {
                    buffer = new CodeEntry[Math.Max(count, buffer.Length * 2)];
                }

                var d = defaults - 1;
                var n = defaults;

                for (var i = 0; i < count; i++)
                {
                    var entry = list[i];

                    if (entry.IsDefault)
                    {
                        buffer[d--] = entry;
                    }
                    else
                    {
                        buffer[n++] = entry;
                    }
                }

                for (var i = 0; i < count; i++)
                {
                    list[i] = buffer[i];
                }
            }
        }
    }

    // =========================================================================
    // Normalisation
    // =========================================================================

    private List<CodeEntry>? NormalizeAndLookup(string input)
    {
        // Fast path: raw input first
        if (_byCode.TryGetValue(input, out var direct))
        {
            return direct;
        }

        // Normalisation slow path
        foreach (var rule in _rules)
        {
            var normalized = rule.Normalize(input);

            if (!string.Equals(normalized, input, StringComparison.OrdinalIgnoreCase) &&
                rule.Validate(normalized) &&
                _byCode.TryGetValue(normalized, out var list))
            {
                return list;
            }
        }

        // Range fallback for compressed codes (e.g. CA)
        return LookupByRange(input);
    }

    private List<CodeEntry>? LookupByRange(string input)
    {
        if (input.Length < 4)
        {
            return null;
        }

        var fsa = input.Substring(0, 3);
        var prefix = input.Substring(0, 4); // 4-char prefix for range comparison

        if (!_rangeIndex.TryGetValue(fsa, out var ranges))
        {
            return null;
        }

        foreach (var (start4, end4, entry) in ranges)
        {
            // String comparison is valid for alphanumeric Canadian codes
            var cmp1 = string.Compare(prefix, start4, StringComparison.OrdinalIgnoreCase);
            var cmp2 = string.Compare(prefix, end4, StringComparison.OrdinalIgnoreCase);

            if (cmp1 >= 0 && cmp2 <= 0)
            {
                // Return a synthetic single-entry list
                return new List<CodeEntry>(1) { entry };
            }
        }

        return null;
    }

    // =========================================================================
    // IZipLookup implementation
    // =========================================================================

    /// <inheritdoc/>
    public CodeEntry? GetByCode(string? code)
    {
        if (code == null) { return null; }

        var hit = NormalizeAndLookup(code)?[0];

        if (hit != null && _throwReasonExceptions)
        {
            ThrowIfFlagged(hit);
        }

        if (hit == null && _throwReasonExceptions && !IsWellFormedCode(code))
        {
            throw new BadlyFormattedCodeException(code);
        }

        return hit;
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

        var list = NormalizeAndLookup(code);

        if (list != null && _throwReasonExceptions && list.Count > 0)
        {
            ThrowIfFlagged(list[0]);
        }

        if (list == null && _throwReasonExceptions && !IsWellFormedCode(code))
        {
            throw new BadlyFormattedCodeException(code);
        }

        return list?.AsReadOnly() ?? (IReadOnlyList<CodeEntry>)Array.Empty<CodeEntry>();
    }

    /// <inheritdoc/>
    public CodeEntry? GetByZip(string zip) => GetByCode(zip);

    /// <inheritdoc/>
    public bool TryGetByZip(string zip, out CodeEntry? entry) => TryGetByCode(zip, out entry);

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetAllByZip(string zip) => GetAllByCode(zip);

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

        // Fast path — exact match
        if (_byCode.TryGetValue(zip, out var exact))
        {
            return exact[0];
        }

        if (_sortedUsZipInts.Length == 0)
        {
            return null;
        }

        var key = int.Parse(zip);
        var index = Array.BinarySearch(_sortedUsZipInts, key);

        if (index < 0)
        {
            index = ~index;
        }

        var bestKey = -1;

        if (index < _sortedUsZipInts.Length)
        {
            bestKey = _sortedUsZipInts[index];
        }

        if (index > 0)
        {
            var below = _sortedUsZipInts[index - 1];

            if (bestKey < 0 || (key - below) < (bestKey - key))
            {
                bestKey = below;
            }
        }

        if (bestKey < 0)
        {
            return null;
        }

        var bestZip = bestKey.ToString("D5");
        return _byCode.TryGetValue(bestZip, out var entries) ? entries[0] : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetByName(string name)
    {
        if (name == null) { throw new ArgumentNullException(nameof(name)); }

        return _byName.TryGetValue(name, out var list)
            ? list.AsReadOnly()
            : Array.Empty<CodeEntry>();
    }

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetByCity(string city) => GetByName(city);

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetByState(string admin1Code)
    {
        if (admin1Code == null) { throw new ArgumentNullException(nameof(admin1Code)); }

        return _byAdmin1Code.TryGetValue(admin1Code, out var list)
            ? list.AsReadOnly()
            : Array.Empty<CodeEntry>();
    }

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetByStateName(string admin1Name)
    {
        if (admin1Name == null) { throw new ArgumentNullException(nameof(admin1Name)); }

        return _byAdmin1Name.TryGetValue(admin1Name, out var list)
            ? list.AsReadOnly()
            : Array.Empty<CodeEntry>();
    }

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetByAdmin(string value, string levelName)
    {
        if (value == null) { throw new ArgumentNullException(nameof(value)); }
        if (levelName == null) { throw new ArgumentNullException(nameof(levelName)); }

        var key = $"{levelName}\0{value}";

        return _byAdmin.TryGetValue(key, out var list)
            ? list.AsReadOnly()
            : Array.Empty<CodeEntry>();
    }

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetByTimeZone(string timezone)
    {
        if (timezone == null) { throw new ArgumentNullException(nameof(timezone)); }

        return _byTimeZone.TryGetValue(timezone, out var list)
            ? list.AsReadOnly()
            : Array.Empty<CodeEntry>();
    }

    /// <inheritdoc/>
    public CodeEntry GetRandom()
    {
#if NET6_0_OR_GREATER
        return _all[Random.Shared.Next(_all.Count)];
#else
        return _all[new Random().Next(_all.Count)];
#endif
    }

    /// <inheritdoc/>
    public CodeEntry GetRandom(Random random)
    {
        if (random == null) { throw new ArgumentNullException(nameof(random)); }

        return _all[random.Next(_all.Count)];
    }

    /// <inheritdoc/>
    public IReadOnlyList<CodeEntry> GetAll() => _all;

    // =========================================================================
    // Batch lookups
    // =========================================================================

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, CodeEntry?> GetBatch(CodeBatch batch)
    {
        if (batch == null)
        {
            throw new ArgumentNullException(nameof(batch));
        }

        return ExecuteBatch(batch);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, CodeEntry?> GetBatch(IEnumerable<string> codes)
    {
        if (codes == null)
        {
            throw new ArgumentNullException(nameof(codes));
        }

        return ExecuteBatch(new CodeBatch(codes));
    }

    private IReadOnlyDictionary<string, CodeEntry?> ExecuteBatch(CodeBatch batch)
    {
        if (batch.IsEmpty)
        {
            return new Dictionary<string, CodeEntry?>(0);
        }

        var results = new Dictionary<string, CodeEntry?>(
            batch.Count,
            StringComparer.OrdinalIgnoreCase);

        foreach (var code in batch)
        {
            // Preserve the original casing as the key — caller gets back what they put in
            results[code] = NormalizeAndLookup(code)?[0];
        }

        return results;
    }

    // =========================================================================
    // Construction helpers
    // =========================================================================

    private static IEnumerable<ICountryCodeRules> BuildDefaultRules(IEnumerable<CountryCode> countries)
    {
        foreach (var country in countries)
        {
            if (country == CountryCode.US)
            {
                yield return _usRules;
            }
            else if (country == CountryCode.CA)
            {
                yield return _caRules;
            }
            else if (country == CountryCode.MX)
            {
                yield return _mxRules;
            }
        }
    }

    private static ReadOnlyCollection<ICountryCodeRules> MergeRules(IEnumerable<ICountryCodeRules>? overrides)
    {
        var merged = new Dictionary<string, ICountryCodeRules>(StringComparer.OrdinalIgnoreCase)
        {
            [CountryCode.US] = _usRules,
            [CountryCode.CA] = _caRules,
            [CountryCode.MX] = _mxRules,
        };

        if (overrides != null)
        {
            foreach (var rule in overrides)
            {
                merged[rule.CountryCode] = rule;
            }
        }

        return merged.Values.ToList().AsReadOnly();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static bool IsAllDigits(string value)
    {
        foreach (var c in value)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }
}