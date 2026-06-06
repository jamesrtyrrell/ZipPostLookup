namespace ZipPostLookup.Core;

/// <summary>
/// Represents an ISO 3166-1 alpha-2 country code (e.g. <c>US</c>, <c>CA</c>, <c>GB</c>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CountryCode"/> is a lightweight value type that normalises its input to uppercase,
/// so <c>"us"</c>, <c>"US"</c>, and <c>CountryCode.US</c> are all equivalent.
/// </para>
/// <para>
/// Implicit conversions to and from <see cref="string"/> are provided, meaning you can pass
/// a plain string literal wherever a <see cref="CountryCode"/> is expected:
/// </para>
/// <code>
/// ZipRegistry registry = ZipPostLookup.CreateRegistry("us", "ca");
/// </code>
/// <para>
/// Pre-defined constants are available for commonly used countries. For any country not listed,
/// construct a code directly:
/// </para>
/// <code>
/// CountryCode japan = new CountryCode("JP");
/// </code>
/// <para>
/// Note that defining a <see cref="CountryCode"/> does not guarantee built-in data exists for
/// that country. If no embedded CSV is found when constructing a <see cref="ZipPostRegistry"/>,
/// a <see cref="NotSupportedException"/> is thrown with instructions on how to add the data.
/// </para>
/// </remarks>
public readonly struct CountryCode : IEquatable<CountryCode>
{
    private readonly string _value;

    /// <summary>United States of America.</summary>
    public static readonly CountryCode US = new("US");

    /// <summary>Canada.</summary>
    public static readonly CountryCode CA = new("CA");

    /// <summary>Mexico.</summary>
    public static readonly CountryCode MX = new("MX");

    /// <summary>United Kingdom of Great Britain and Northern Ireland.</summary>
    public static readonly CountryCode GB = new("GB");

    /// <summary>France.</summary>
    public static readonly CountryCode FR = new("FR");

    /// <summary>Germany.</summary>
    public static readonly CountryCode DE = new("DE");

    /// <summary>Australia.</summary>
    public static readonly CountryCode AU = new("AU");

    /// <summary>New Zealand.</summary>
    public static readonly CountryCode NZ = new("NZ");

    /// <summary>
    /// Initialises a new <see cref="CountryCode"/> from a two-letter string.
    /// The value is normalised to uppercase automatically.
    /// </summary>
    /// <param name="value">
    /// An ISO 3166-1 alpha-2 country code string, e.g. <c>"US"</c> or <c>"ca"</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is null, empty, or whitespace.
    /// </exception>
    public CountryCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Country code cannot be empty.", nameof(value));
        _value = value.ToUpperInvariant();
    }

    /// <summary>
    /// Implicitly converts a <see cref="string"/> to a <see cref="CountryCode"/>,
    /// normalising to uppercase.
    /// </summary>
    /// <param name="value">The country code string, e.g. <c>"us"</c>.</param>
    public static implicit operator CountryCode(string value) => new(value);

    /// <summary>
    /// Implicitly converts a <see cref="CountryCode"/> to its uppercase string representation.
    /// </summary>
    /// <param name="code">The country code to convert.</param>
    public static implicit operator string(CountryCode code) => code._value;

    /// <summary>
    /// Determines whether this instance equals another <see cref="CountryCode"/>.
    /// </summary>
    /// <param name="other">The other <see cref="CountryCode"/> to compare.</param>
    /// <returns><c>true</c> if both codes represent the same country; otherwise <c>false</c>.</returns>
    public bool Equals(CountryCode other) =>
        string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is CountryCode other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value?.GetHashCode() ?? 0;

    /// <summary>Returns the uppercase two-letter country code string, e.g. <c>"US"</c>.</summary>
    public override string ToString() => _value;

    /// <summary>Returns <c>true</c> if <paramref name="left"/> and <paramref name="right"/> represent the same country.</summary>
    public static bool operator ==(CountryCode left, CountryCode right) => left.Equals(right);

    /// <summary>Returns <c>true</c> if <paramref name="left"/> and <paramref name="right"/> represent different countries.</summary>
    public static bool operator !=(CountryCode left, CountryCode right) => !left.Equals(right);
}