using Xunit;
using ZipPostLookup.CountryDataTools.Utilities;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>Unit tests for <see cref="FileTools.StripPathQuotes"/> (pasted-path cleanup).</summary>
public class FileToolsTests
{
    [Theory]
    [InlineData("\"C:\\a b\\f.csv\"", "C:\\a b\\f.csv")]   // double-quoted
    [InlineData("'C:\\a b\\f.csv'", "C:\\a b\\f.csv")]      // single-quoted
    [InlineData("  C:\\plain.csv  ", "C:\\plain.csv")]      // trims whitespace
    [InlineData("C:\\plain.csv", "C:\\plain.csv")]          // unquoted unchanged
    [InlineData("\"unterminated", "\"unterminated")]        // only strips matched pairs
    public void StripPathQuotes(string input, string expected) =>
        Assert.Equal(expected, FileTools.StripPathQuotes(input));
}
