namespace ZipPostLookup;

/// <summary>
/// Base type for the opt-in exceptions that explain <i>why</i> a postal-code lookup did not
/// return a usable result, instead of a silent <c>null</c>/empty miss.
/// </summary>
/// <remarks>
/// These are only thrown when the caller opts in via
/// <see cref="ZipPostLookup.ThrowReasonExceptions(bool)"/> (default <c>false</c>). With the
/// toggle off, lookups behave exactly as before (return <c>null</c> / empty).
/// </remarks>
public abstract class CodeReasonException : Exception
{
    /// <summary>The offending postal code, exactly as supplied by the caller.</summary>
    public string Code { get; }

    /// <summary>Creates the exception for <paramref name="code"/> with an explanatory message.</summary>
    protected CodeReasonException(string code, string message) : base(message)
    {
        Code = code;
    }
}

/// <summary>
/// Thrown when the supplied input does not match the postal-code format of any supported
/// country (US, CA, MX) — i.e. the per-country normalizer rejects it. Detected from the input
/// format alone; requires no embedded data.
/// </summary>
public sealed class BadlyFormattedCodeException : CodeReasonException
{
    /// <summary>Creates the exception for the malformed <paramref name="code"/>.</summary>
    public BadlyFormattedCodeException(string code)
        : base(code, $"The postal code '{code}' is not a valid format for any supported country (US, CA, MX).")
    {
    }
}

/// <summary>
/// Thrown when a found postal code has been decommissioned/retired by the postal authority but
/// is still in circulation.
/// </summary>
public sealed class ObsoleteCodeException : CodeReasonException
{
    /// <summary>Creates the exception for the obsolete <paramref name="code"/>.</summary>
    public ObsoleteCodeException(string code)
        : base(code, $"The postal code '{code}' is obsolete (decommissioned by the postal authority).")
    {
    }
}

/// <summary>
/// Thrown when a found postal code is widely-circulated bogus/placeholder data rather than a real
/// deliverable code.
/// </summary>
public sealed class CommonFakeDataException : CodeReasonException
{
    /// <summary>Creates the exception for the fake <paramref name="code"/>.</summary>
    public CommonFakeDataException(string code)
        : base(code, $"The postal code '{code}' is known common fake/placeholder data, not a real deliverable code.")
    {
    }
}
