namespace ZipPostLookup.Core;

/// <summary>
/// The reason a postal code entry carries a flag, exposed via <see cref="CodeEntry.Reason"/>.
/// </summary>
/// <remarks>
/// When <see cref="ZipPostLookup.ThrowReasonExceptions(bool)"/> is enabled, lookups for codes
/// flagged <see cref="Obsolete"/> or <see cref="CommonFake"/> throw a
/// <see cref="CodeReasonException"/> subclass instead of returning the entry silently.
/// A code with <see cref="Flagged"/> (generic flag) is returned normally — only the two named
/// reasons produce exceptions.
/// </remarks>
public enum CodeReason
{
    /// <summary>0 — Normal entry; no flag.</summary>
    None = 0,

    /// <summary>1 — Flagged for a generic/unspecified reason.</summary>
    Flagged = 1,

    /// <summary>2 — Widely-circulated bogus or placeholder data; not a real deliverable code.</summary>
    CommonFake = 2,

    /// <summary>3 — Decommissioned/retired by the postal authority but still in circulation.</summary>
    Obsolete = 3,
}
