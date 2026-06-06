using System.Text.RegularExpressions;

namespace ZipPostLookup.CountryDataTools.Utilities;

internal static class ReadmeUpdater
{
    public static async Task UpdateCountryAsync(
        string repoRoot, string country,
        int codes, int timezones, int divisions)
    {
        var readmePath = Path.Combine(repoRoot, "README.md");
        if (!File.Exists(readmePath)) return;

        var cc = country.ToLowerInvariant();
        var content = await File.ReadAllTextAsync(readmePath, System.Text.Encoding.UTF8);

        content = SetTag(content, $"zpl:{cc}:codes",   $"{codes:N0}");
        content = SetTag(content, $"zpl:{cc}:tz",      timezones.ToString());
        if (divisions > 0)
        {
            content = SetTag(content, $"zpl:{cc}:divs", divisions.ToString());
        }

        // Abbreviated form e.g. "901k" for Canada
        if (codes >= 1000)
        {
            content = SetTag(content, $"zpl:{cc}:codes:k", $"~{codes / 1000}k");
        }

        var currentDate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        content = SetTag(content, $"zpl:updated", currentDate);
        
        await File.WriteAllTextAsync(readmePath, content, System.Text.Encoding.UTF8);
    }

    public static async Task UpdateEntriesAsync(string repoRoot, string country, int entries)
    {
        var readmePath = Path.Combine(repoRoot, "README.md");
        if (!File.Exists(readmePath)) return;

        var cc = country.ToLowerInvariant();
        var content = await File.ReadAllTextAsync(readmePath, System.Text.Encoding.UTF8);

        // Format as abbreviated e.g. "412k"
        var formatted = entries >= 1000 ? $"{entries / 1000}k" : entries.ToString();
        content = SetTag(content, $"zpl:{cc}:entries", formatted);

        await File.WriteAllTextAsync(readmePath, content, System.Text.Encoding.UTF8);
    }

    private static string SetTag(string content, string tag, string value)
    {
        var pattern = $@"<!--\s*{Regex.Escape(tag)}\s*-->.*?<!--\s*/{Regex.Escape(tag)}\s*-->";
        var replacement = $"<!-- {tag} -->{value}<!-- /{tag} -->";
        return Regex.Replace(content, pattern, replacement,
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }
}
