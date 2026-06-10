using ZipPostLookup.CountryDataTools.Models.Enums;

namespace ZipPostLookup.CountryDataTools.Validation.Csv;

/// <summary>
/// A single validation finding attached to a specific record in the CSV.
/// </summary>
public sealed record ValidationError(
    int       RecordNumber,
    string    Zip,
    string    Field,
    ErrorType ErrorType,
    string    Value,
    string    Suggested = ""
)
{
    /// <summary>
    /// Formats the error as a single log line, e.g.:
    ///   record:54  zip:12345  field:Name      error:MissingValue
    ///   record:89  zip:90210  field:timezone  error:NonIanaTimezone  value:"Eastern Standard Time"  suggested:"America/New_York"
    ///   record:102 zip:00601  field:zip       error:DuplicatePair    value:"AGUADILLA"  suggested:"duplicate_of:record:7"
    /// </summary>
    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"record:{RecordNumber,-6} zip:{Zip,-12} field:{Field,-12} error:{ErrorType}");
        if (!string.IsNullOrEmpty(Value))     sb.Append($"  value:\"{Value}\"");
        if (!string.IsNullOrEmpty(Suggested)) sb.Append($"  suggested:\"{Suggested}\"");
        return sb.ToString();
    }

    public Severity Severity => ErrorType switch
    {
        // Structural — cannot be auto-fixed; block Fix and Merge
        ErrorType.MissingColumn        => Severity.Error,
        ErrorType.TooFewColumns        => Severity.Error,
        ErrorType.MissingValue         => Severity.Error,
        ErrorType.InvalidZipFormat     => Severity.Error,
        ErrorType.UnresolvableTimezone => Severity.Error,

        // Fixable — Fixer handles these automatically
        ErrorType.DuplicatePair        => Severity.Fixable,
        ErrorType.MultipleDefaults     => Severity.Fixable,
        ErrorType.NonIanaTimezone      => Severity.Fixable,
        ErrorType.DeprecatedTimezone   => Severity.Fixable,
        ErrorType.InvalidBoolean       => Severity.Fixable,

        // Advisory
        ErrorType.NoDefaultForZip      => Severity.Warning,

        _                              => Severity.Error
    };
}
