using System.Text.Json;
using System.Text.Json.Serialization;
using ZipPostLookup.Core;

namespace ZipPostLookup.CountryDataTools.Pipeline;

/// <summary>
/// Resolves a canonical (state_code, state_name) pair from any of:
///   1. ANSI numeric code  — e.g. "72"  → ("PR", "Puerto Rico")
///   2. USPS postal code   — e.g. "KY"  → ("KY", "Kentucky")
///   3. Full state name    — e.g. "Kentucky" → ("KY", "Kentucky")
///
/// Loaded once from the embedded us_info.json resource and cached
/// for the lifetime of the Process.
///
/// Returns null if no match is found — the caller should treat this as
/// an unresolvable state and leave the field unverified.
/// </summary>
public static class StateResolver
{
    private static readonly Lazy<StateIndex> _index =
        new(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Resolves a canonical state pair from any input format.
    /// Returns null if unresolvable.
    /// </summary>
    public static StateMatch? Resolve(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var idx = _index.Value;

        var key = input.Trim();

        // 1. Try ANSI numeric code
        if (idx.ByAnsi.TryGetValue(key, out var byAnsi))
            return byAnsi;

        // 2. Try USPS code (case-insensitive)
        if (idx.ByUsps.TryGetValue(key.ToUpperInvariant(), out var byUsps))
            return byUsps;

        // 3. Try full name (case-insensitive)
        if (idx.ByName.TryGetValue(key.ToLowerInvariant(), out var byName))
            return byName;

        return null;
    }

    // -------------------------------------------------------------------------

    private static StateIndex BuildIndex()
    {
        var assembly = typeof(StateResolver).Assembly;

        // Look in both the CountryDataTools assembly and the ZipPostLookup assembly
        var resourceName =
            assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("us_info.json",
                    StringComparison.OrdinalIgnoreCase))
            ?? typeof(CountryInfo).Assembly
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("us_info.json",
                    StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
            throw new InvalidOperationException(
                "Embedded resource 'us_info.json' not found. " +
                "Ensure it is marked as EmbeddedResource in ZipPostLookup.csproj.");

        // Determine which assembly has the resource
        var targetAssembly = assembly.GetManifestResourceNames()
            .Any(n => n.EndsWith("us_info.json", StringComparison.OrdinalIgnoreCase))
            ? assembly
            : typeof(CountryInfo).Assembly;

        using var stream = targetAssembly.GetManifestResourceStream(resourceName)!;
        var doc = JsonSerializer.Deserialize<UsStatesDoc>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialise us_info.json.");

        var byAnsi = new Dictionary<string, StateMatch>(StringComparer.Ordinal);
        var byUsps = new Dictionary<string, StateMatch>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, StateMatch>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in doc.States)
        {
            var match = new StateMatch(s.Usps, s.Name);

            if (!byUsps.ContainsKey(s.Usps))
                byUsps[s.Usps] = match;

            if (!byName.ContainsKey(s.Name.ToLowerInvariant()))
                byName[s.Name.ToLowerInvariant()] = match;

            if (!string.IsNullOrWhiteSpace(s.Ansi) && !byAnsi.ContainsKey(s.Ansi))
                byAnsi[s.Ansi] = match;

            // Also index individual UM islands by ANSI code
            if (s.Islands != null)
            {
                foreach (var island in s.Islands)
                {
                    if (!string.IsNullOrWhiteSpace(island.Ansi) &&
                        !byAnsi.ContainsKey(island.Ansi))
                        byAnsi[island.Ansi] = new StateMatch(s.Usps, island.Name);

                    if (!byName.ContainsKey(island.Name.ToLowerInvariant()))
                        byName[island.Name.ToLowerInvariant()] = new StateMatch(s.Usps, island.Name);
                }
            }
        }

        return new StateIndex(byAnsi, byUsps, byName);
    }

    // -------------------------------------------------------------------------
    // Internal types
    // -------------------------------------------------------------------------

    private sealed record StateIndex(
        IReadOnlyDictionary<string, StateMatch> ByAnsi,
        IReadOnlyDictionary<string, StateMatch> ByUsps,
        IReadOnlyDictionary<string, StateMatch> ByName
    );

    private sealed class UsStatesDoc
    {
        [JsonPropertyName("states")]
        public List<StateEntry> States { get; set; } = [];
    }

    private sealed class StateEntry
    {
        [JsonPropertyName("usps")] public string Usps { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("ansi")] public string? Ansi { get; set; }
        [JsonPropertyName("islands")] public List<IslandEntry>? Islands { get; set; }
    }

    private sealed class IslandEntry
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("ansi")] public string? Ansi { get; set; }
    }
}

/// <summary>
/// A resolved canonical state pair — USPS code and full name.
/// </summary>
public sealed record StateMatch(
    string StateCode,   // e.g. "PR"
    string StateName    // e.g. "Puerto Rico"
);