namespace ZipPostLookup.CountryDataTools.Pipeline;

/// <summary>
/// Case-insensitive equality comparer for (Code, Name) tuple keys.
/// Shared by <see cref="ValidationRules"/> and <see cref="Merger"/>.
/// </summary>
internal sealed class ZipCityComparer : IEqualityComparer<(string Code, string Name)>
{
    public static readonly ZipCityComparer Instance = new();

    private ZipCityComparer() { }

    public bool Equals((string Code, string Name) x, (string Code, string Name) y) =>
        string.Equals(x.Code, y.Code, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((string Code, string Name) obj) =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Code),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
}