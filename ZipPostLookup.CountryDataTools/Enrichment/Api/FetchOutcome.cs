namespace ZipPostLookup.CountryDataTools.Enrichment.Api;

internal enum FetchOutcome { Found, NotFound, RateLimited, TransientError }

/// <summary>
/// The result of a single API lookup. Carries an optional <see cref="Detail"/> string
/// describing the nature of a non-success outcome (e.g. "HTTP 503", "TaskCanceledException:
/// timeout") so transient failures can be logged with a reason, not just an outcome.
/// An implicit conversion from the legacy <c>(Result, Outcome)</c> tuple lets existing
/// success/not-found/rate-limited returns compile unchanged — only paths that want to
/// attach a reason need the 3-argument form.
/// </summary>
internal readonly record struct FetchResult(
    ApiLookupResult? Result, FetchOutcome Outcome, string? Detail = null)
{
    public static implicit operator FetchResult(
        (ApiLookupResult? Result, FetchOutcome Outcome) t) => new(t.Result, t.Outcome);

    public static implicit operator FetchResult(
        (ApiLookupResult? Result, FetchOutcome Outcome, string? Detail) t)
        => new(t.Result, t.Outcome, t.Detail);
}
