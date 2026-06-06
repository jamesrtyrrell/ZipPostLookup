using ZipPostLookup.CountryDataTools.Utilities;

namespace ZipPostLookup.CountryDataTools.Commands;

/// <summary>
/// Shared argument parsing helpers for all commands.
/// </summary>
internal static class CommandArgs
{
    /// <summary>
    /// Resolves the country code from either the --country flag or the filename stem.
    /// Per the ZipPostLookup convention, data files are named {cc}.csv (e.g. us.csv,
    /// mx.csv, gb.csv), so the country code can be derived automatically.
    ///
    /// Returns false and writes an error if neither source provides a two-letter code.
    /// </summary>
    public static bool ResolveCountry(
        string file, string explicitCountry, out string country)
    {
        if (!string.IsNullOrEmpty(explicitCountry))
        {
            country = explicitCountry.ToUpperInvariant();
            return true;
        }

        var derived = TimezoneResolver.CountryCodeFromFileName(file);
        if (derived != null)
        {
            country = derived;
            Console.WriteLine($"  Country code derived from filename: {country}");
            return true;
        }

        country = "";
        Console.Error.WriteLine(
            $"  Cannot determine country code from filename '{Path.GetFileName(file)}'.");
        Console.Error.WriteLine(
            $"  Either rename the file to {{cc}}.csv (e.g. us.csv) or pass --country XX.");
        return false;
    }
}
