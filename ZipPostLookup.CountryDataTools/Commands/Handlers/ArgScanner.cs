namespace ZipPostLookup.CountryDataTools.Commands.Handlers;

/// <summary>
/// Small token-scanning helpers shared by the hand-rolled <c>TryParseArgs</c> methods.
/// Each scan is independent and case-insensitive; "last occurrence wins" matches the
/// original left-to-right overwrite semantics. Handlers keep their own defaults,
/// validation, and transforms — these only replace the repeated <c>for/switch</c> loop.
/// </summary>
internal static class ArgScanner
{
    /// <summary>
    /// Returns the value following <paramref name="name"/> (last occurrence), or null if the
    /// option is absent or has no following token. When <paramref name="rejectFlagValue"/> is
    /// true, a following token that starts with '-' is treated as "no value" (matches the
    /// <c>--country</c> guard).
    /// </summary>
    public static string? OptionValue(this string[] args, string name, bool rejectFlagValue = false)
    {
        string? value = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 >= args.Length) continue;
            if (rejectFlagValue && args[i + 1].StartsWith('-')) continue;
            value = args[i + 1];
        }
        return value;
    }

    /// <summary>True if the flag <paramref name="name"/> appears anywhere in <paramref name="args"/>.</summary>
    public static bool HasFlag(this string[] args, string name) =>
        args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Parses the int following <paramref name="name"/>, returning <paramref name="fallback"/>
    /// when the option is absent or the value is non-numeric or below <paramref name="min"/>.
    /// </summary>
    public static int IntOption(this string[] args, string name, int fallback, int min = int.MinValue)
    {
        var raw = OptionValue(args, name);
        return int.TryParse(raw, out var n) && n >= min ? n : fallback;
    }
}
