#if NET8_0_OR_GREATER
using System.Reflection;
using ZipPostLookup.ZPImage;

namespace ZipPostLookup.Core;

/// <summary>
/// Dynamic country registry that discovers available countries from embedded resources.
/// Replaces hardcoded US/CA/MX list with automatic discovery based on embedded .zpi.br files.
/// </summary>
public static class CountryRegistry
{
    private static readonly Lazy<List<CountryCode>> _availableCountries = new(DiscoverCountries);
    private static readonly Dictionary<CountryCode, IZipPostLookup> _lookupCache = new();
    private static readonly object _cacheLock = new();

    /// <summary>
    /// Get all countries with embedded .zpi.br/.u16/.u32 ZP image resources.
    /// Results are cached after first call.
    /// </summary>
    /// <returns>Read-only list of available country codes.</returns>
    public static IReadOnlyList<CountryCode> GetAvailableCountries()
        => _availableCountries.Value;

    /// <summary>
    /// Get or create a lookup instance for the specified country.
    /// Instances are lazy-loaded and cached per country.
    /// </summary>
    /// <param name="countryCode">Country code (e.g., "US", "CA", "MX").</param>
    /// <returns>Lookup instance for the country.</returns>
    /// <exception cref="NotSupportedException">No embedded image exists for the country.</exception>
    public static IZipPostLookup GetLookup(CountryCode countryCode)
    {
        lock (_cacheLock)
        {
            if (_lookupCache.TryGetValue(countryCode, out var cached))
            {
                return cached;
            }

            // ZpImageLookup.FromBuiltIn throws NotSupportedException if not embedded
            var lookup = ZpImageLookup.FromBuiltIn(countryCode);
            _lookupCache[countryCode] = lookup;
            return lookup;
        }
    }

    /// <summary>
    /// Check if a country has embedded data available.
    /// </summary>
    /// <param name="countryCode">Country code to check.</param>
    /// <returns>True if the country has an embedded ZP image.</returns>
    public static bool IsAvailable(CountryCode countryCode)
        => _availableCountries.Value.Contains(countryCode);

    /// <summary>
    /// Discover all countries by scanning assembly manifest resources.
    /// Looks for patterns: ZipPostLookup.Data.{CC}.{CC}.{zpi.br|u16|u32}
    /// </summary>
    private static List<CountryCode> DiscoverCountries()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();

        // Pattern: "ZipPostLookup.Data.{cc}.{cc}.{zpi.br|u16|u32}"
        var countries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in resourceNames)
        {
            // Example: "ZipPostLookup.Data.us.us.u16"
            if (!name.StartsWith("ZipPostLookup.Data.", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = name.Split('.');
            if (parts.Length < 5)
                continue;

            // parts[2] = country code (lowercase in resource name)
            var cc = parts[2];

            // Verify it ends with a known ZP image extension
            var ext = parts[^1].ToLowerInvariant();
            if (ext == "u16" || ext == "u32" || (parts.Length >= 6 && parts[^2] == "zpi" && parts[^1] == "br"))
            {
                countries.Add(cc.ToUpperInvariant());
            }
        }

        // Convert to CountryCode and sort
        var result = countries
            .Select(cc => (CountryCode)cc)
            .OrderBy(cc => (string)cc)
            .ToList();

        return result;
    }
}
#endif
