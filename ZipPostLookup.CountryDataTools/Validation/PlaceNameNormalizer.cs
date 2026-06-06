using System.Text.Json;

namespace ZipPostLookup.CountryDataTools.Validation;

/// <summary>
/// Normalises place names by expanding abbreviations so that equivalent names
/// ("St. Martin" / "Saint Martin", "Ft Worth" / "Fort Worth") can be compared
/// as equal regardless of which form a data source uses.
///
/// Abbreviation rules are loaded once from the embedded
/// <c>Data/LanguageAbbreviations.json</c> resource.
///
/// Only place-name-relevant abbreviations are included (Saint, Mount, Fort,
/// compass points, etc.). Street-type abbreviations (Road, Drive, Avenue…)
/// are intentionally excluded — they do not appear in locality name fields.
///
/// Normalisation algorithm:
///   1. Replace hyphens with spaces (Saint-Jean ↔ Saint Jean).
///   2. Split by spaces.
///   3. Strip leading/trailing periods from each token.
///   4. Look up the token (case-insensitive) in the abbreviation map for
///      each requested language (first match wins).
///   5. Rejoin with spaces.
/// </summary>
public static class PlaceNameNormalizer
{
    private const string ResourceName =
        "ZipPostLookup.CountryDataTools.Data.LanguageAbbreviations.json";

    // langName → (abbrev_lower → canonical)
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>
        _rules = new(LoadRules);

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadRules()
    {
        var assembly = typeof(PlaceNameNormalizer).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. " +
                "Ensure Data/LanguageAbbreviations.json is marked as EmbeddedResource.");

        var doc     = JsonDocument.Parse(stream);
        var result  = new Dictionary<string, IReadOnlyDictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var lang in doc.RootElement.EnumerateObject())
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in lang.Value.EnumerateObject())
            {
                var canonical = entry.Name; // e.g. "Mount"
                foreach (var abbrev in entry.Value.EnumerateArray())
                {
                    var key = abbrev.GetString();
                    if (!string.IsNullOrEmpty(key))
                        map[key] = canonical;
                }
            }
            result[lang.Name] = map;
        }

        return result;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Expands abbreviations in <paramref name="name"/> using the given language rules.
    /// "St. Martin" → "Saint Martin", "Mt-Pleasant" → "Mount Pleasant".
    /// Languages are tried in order; the first matching abbreviation map wins.
    /// </summary>
    public static string Normalize(string name, IEnumerable<string> languages)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;

        var maps = BuildMaps(languages);
        if (maps.Count == 0) return name;

        // Treat hyphens as spaces so St-Jean and St Jean normalise the same way.
        var tokens = name.Replace('-', ' ')
                         .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < tokens.Length; i++)
        {
            var clean = tokens[i].Trim('.');
            foreach (var map in maps)
            {
                if (map.TryGetValue(clean, out var canonical))
                {
                    tokens[i] = canonical;
                    break;
                }
            }
        }

        return string.Join(' ', tokens);
    }

    /// <summary>
    /// True if <paramref name="nameA"/> and <paramref name="nameB"/> refer to the
    /// same place after abbreviation expansion (case-insensitive).
    /// "St. Martin" / "Saint Martin" → true.
    /// "St Andrews" / "Stewart" → false.
    /// </summary>
    public static bool AreEquivalent(
        string nameA, string nameB, IEnumerable<string> languages)
    {
        if (string.IsNullOrWhiteSpace(nameA) || string.IsNullOrWhiteSpace(nameB))
            return false;

        var langList = languages.ToList();
        return string.Equals(
            Normalize(nameA, langList),
            Normalize(nameB, langList),
            StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static List<IReadOnlyDictionary<string, string>> BuildMaps(
        IEnumerable<string> languages)
    {
        var rules = _rules.Value;
        return languages
            .Where(l => rules.ContainsKey(l))
            .Select(l => rules[l])
            .ToList();
    }
}
