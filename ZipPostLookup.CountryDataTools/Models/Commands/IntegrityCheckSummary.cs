namespace ZipPostLookup.CountryDataTools.Models.Commands;

/// <summary>
/// Display-ready snapshot of an integrity check run, decoupled from the private
/// <c>CheckResults</c> type in <c>IntegrityCheckCommand</c>.
/// Passed to <c>IntegrityDisplay.PrintSummary</c> in <c>Commands/Display/</c>.
/// </summary>
public sealed record IntegrityCheckSummary(
    int  DefaultTested,
    int  NonDefaultTested,
    int  GetByCodeAttempts,
    int  GetByCodeFailures,
    int  GetByZipAttempts,
    int  GetByZipFailures,
    int  GetAllByCodeAttempts,
    int  GetAllByCodeFailures,
    int  GetAllByCodeNonDefaultAttempts,
    int  GetAllByCodeNonDefaultFailures,
    int  TotalFailures,
    int  UniqueFailed,
    int  UniqueDefaultFailed,
    bool HasFailures);
