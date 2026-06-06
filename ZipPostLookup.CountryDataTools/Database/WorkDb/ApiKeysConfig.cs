namespace ZipPostLookup.CountryDataTools.Database.WorkDb;

/// <summary>
/// Represents one entry in the apikeys.json array.
/// </summary>
public sealed class ApiKeyEntry
{
    public string Name         { get; init; } = "";
    public string Key          { get; init; } = "";
    public int?   DailyLimit   { get; init; }
    public int?   MonthlyLimit { get; init; }

    /// <summary>
    /// False when the key is empty or still the placeholder value (starts with "YOUR_").
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Key) &&
        !Key.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Loader for apikeys.json — an optional array of third-party API credentials used by
/// enrichment commands. Lives next to workdb.json (typically the solution root).
///
/// Format:
/// [
///   { "name": "geoApify", "key": "your-key-here", "dailyLimit": 3000 }
/// ]
///
/// apikeys.json is gitignored. Create it locally by copying the array structure above.
/// All entries are optional — omitting an entry silently disables that API.
/// A present entry with a placeholder key produces a warning and is also skipped.
/// </summary>
public sealed class ApiKeysConfig
{
    private readonly IReadOnlyList<ApiKeyEntry> _entries;

    private ApiKeysConfig(IReadOnlyList<ApiKeyEntry> entries) => _entries = entries;

    public const string FileName = "apikeys.json";

    /// <summary>
    /// Returns the entry for <paramref name="name"/>, or null if not present in the file.
    /// </summary>
    public ApiKeyEntry? TryGetEntry(string name) =>
        _entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // -------------------------------------------------------------------------

    public static string? FindConfigFile(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// Searches <paramref name="startDirectory"/> and its parents for apikeys.json.
    /// Returns an empty config if no file is found — never throws.
    /// </summary>
    public static ApiKeysConfig TryLoad(string startDirectory)
    {
        var path = FindConfigFile(startDirectory);
        if (path == null) return new ApiKeysConfig([]);

        try
        {
            var json    = File.ReadAllText(path);
            var entries = System.Text.Json.JsonSerializer.Deserialize<List<ApiKeyEntry>>(
                json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? [];
            return new ApiKeysConfig(entries);
        }
        catch
        {
            return new ApiKeysConfig([]);
        }
    }
}
