namespace ZipPostLookup.CountryDataTools.Database.WorkDb;

/// <summary>
/// Typed representation of workdb.json — the per-folder developer config file
/// that tells every command where the working database lives and which country
/// and run it is currently operating on.
///
/// workdb.json is gitignored and created by:
///   CountryDataTools workdb init --country US --connection "Server=localhost,1433;..."
///
/// Minimal example:
/// {
///   "provider":          "sqlserver",
///   "connectionString":  "Server=localhost,1433;Database=ZipPostLookupWorkDB;User Id=sa;Password=...;TrustServerCertificate=true",
///   "countryCode":       "US",
///   "activeRunId":       "run_20260527_001"
/// }
/// </summary>
public sealed class WorkDbConfig
{
    /// <summary>
    /// Database provider. Currently only "sqlserver" is supported.
    /// Reserved values for future use: "postgres", "sqlite".
    /// </summary>
    public string Provider { get; init; } = "sqlserver";

    /// <summary>ADO.NET connection string for the target server.</summary>
    public string ConnectionString { get; init; } = "";

    /// <summary>ISO 3166-1 alpha-2 country code this working folder is for, e.g. "US".</summary>
    public string CountryCode { get; init; } = "";

    /// <summary>
    /// The run ID that commands will tag new rows with.
    /// Set automatically by 'workdb init' and 'workdb newrun'.
    /// Format: run_{yyyyMMdd}_{NNN}, e.g. "run_20260527_001".
    /// </summary>
    public string ActiveRunId { get; init; } = "";

    // -------------------------------------------------------------------------

    /// <summary>
    /// File name searched for in the current directory and each parent up to the
    /// solution root. Follows the same "walk-up" convention as .editorconfig.
    /// </summary>
    public const string FileName = "workdb.json";

    /// <summary>
    /// Searches <paramref name="startDirectory"/> and each parent for workdb.json.
    /// Returns null if no file is found.
    /// </summary>
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
    /// Loads and deserialises workdb.json from <paramref name="path"/>.
    /// Throws <see cref="InvalidOperationException"/> if the file is missing
    /// required fields.
    /// </summary>
    public static WorkDbConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        var config = System.Text.Json.JsonSerializer.Deserialize<WorkDbConfig>(
            json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Failed to deserialise {path}");

        if (string.IsNullOrWhiteSpace(config.ConnectionString))
            throw new InvalidOperationException(
                $"{path}: 'connectionString' is required.");

        if (string.IsNullOrWhiteSpace(config.CountryCode))
            throw new InvalidOperationException(
                $"{path}: 'countryCode' is required.");

        return config;
    }

    /// <summary>
    /// Writes this config to <paramref name="path"/> as formatted JSON.
    /// </summary>
    public void Save(string path)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            });
        File.WriteAllText(path, json);
    }
}
