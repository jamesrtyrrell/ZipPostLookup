namespace ZipPostLookup.CountryDataTools.Models.Enums;

/// <summary>
/// Reason a <c>data.Reference</c> code is flagged. Stored as the integer
/// <c>data.Reference.Flagged</c> column; Dapper maps the int to/from this enum
/// automatically. Any value other than <see cref="Valid"/> excludes the code from
/// every export query.
/// </summary>
public enum DataFlagReasonType
{
    /// <summary>0 — valid; not flagged.</summary>
    Valid = 0,

    /// <summary>1 — flagged for a generic/unspecified reason.</summary>
    Flagged = 1,

    /// <summary>2 — common fake: bad data distributed or found across multiple sources.</summary>
    CommonFake = 2,

    /// <summary>3 — obsolete: no longer in use by the postal service.</summary>
    Obsolete = 3,
}
