using Xunit;
using ZipPostLookup.CountryDataTools.Commands.Handlers;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// Unit tests for <c>ArgScanner</c> — the shared CLI token scanner used by the handlers'
/// hand-rolled arg parsing. Internal type, reached via InternalsVisibleTo.
/// </summary>
public class ArgScannerTests
{
    [Fact]
    public void OptionValue_ReturnsFollowingToken()
    {
        var args = new[] { "--country", "US", "--limit", "100" };
        Assert.Equal("US", args.OptionValue("--country"));
        Assert.Equal("100", args.OptionValue("--limit"));
    }

    [Fact]
    public void OptionValue_Missing_ReturnsNull() =>
        Assert.Null(new[] { "--country", "US" }.OptionValue("--run"));

    [Fact]
    public void OptionValue_LastOccurrenceWins() =>
        Assert.Equal("CA", new[] { "--country", "US", "--country", "CA" }.OptionValue("--country"));

    [Fact]
    public void OptionValue_FlagIsCaseInsensitive() =>
        Assert.Equal("US", new[] { "--Country", "US" }.OptionValue("--country"));

    [Fact]
    public void OptionValue_RejectFlagValue_SkipsFollowingFlag()
    {
        var args = new[] { "--country", "--all" };
        Assert.Null(args.OptionValue("--country", rejectFlagValue: true));
        Assert.Equal("--all", args.OptionValue("--country"));   // permissive mode takes it
    }

    [Fact]
    public void HasFlag_IsCaseInsensitive()
    {
        var args = new[] { "--all", "--dry-run" };
        Assert.True(args.HasFlag("--all"));
        Assert.True(args.HasFlag("--DRY-RUN"));
        Assert.False(args.HasFlag("--force"));
    }

    [Fact] public void IntOption_Parses() =>
        Assert.Equal(250, new[] { "--limit", "250" }.IntOption("--limit", fallback: 100));

    [Fact] public void IntOption_NonNumeric_Fallback() =>
        Assert.Equal(100, new[] { "--limit", "abc" }.IntOption("--limit", fallback: 100));

    [Fact] public void IntOption_Absent_Fallback() =>
        Assert.Equal(100, Array.Empty<string>().IntOption("--limit", fallback: 100));

    [Fact] public void IntOption_BelowMin_Fallback() =>
        Assert.Equal(100, new[] { "--limit", "0" }.IntOption("--limit", fallback: 100, min: 1));
}
