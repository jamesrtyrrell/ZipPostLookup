using Xunit;
using ZipPostLookup.CountryDataTools.Dsv;

namespace ZipPostLookup.Tests.Cdt;

/// <summary>
/// Unit tests for <see cref="DelimitedFile"/> — RFC-4180 line splitting for comma files,
/// plain splitting for tab files, and delimiter sniffing. Pure logic (sniff uses a temp file).
/// </summary>
public class DelimitedFileTests
{
    [Fact]
    public void SplitLine_Comma_Simple() =>
        Assert.Equal(new[] { "a", "b", "c" }, DelimitedFile.SplitLine("a,b,c", ','));

    [Fact]
    public void SplitLine_Comma_QuotedFieldWithEmbeddedComma() =>
        Assert.Equal(new[] { "a", "b,c", "d" }, DelimitedFile.SplitLine("a,\"b,c\",d", ','));

    [Fact]
    public void SplitLine_Comma_EscapedDoubleQuote() =>
        Assert.Equal(new[] { "a\"b" }, DelimitedFile.SplitLine("\"a\"\"b\"", ','));

    [Fact]
    public void SplitLine_Tab_IsPlainSplit() =>
        Assert.Equal(new[] { "a", "b", "c" }, DelimitedFile.SplitLine("a\tb\tc", '\t'));

    [Fact]
    public void SniffDelimiter_DetectsTabVsComma()
    {
        var tabFile = Path.GetTempFileName();
        var csvFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tabFile, "a\tb\tc\n1\t2\t3\n");
            File.WriteAllText(csvFile, "a,b,c\n1,2,3\n");

            Assert.Equal('\t', DelimitedFile.SniffDelimiter(tabFile));
            Assert.Equal(',', DelimitedFile.SniffDelimiter(csvFile));
        }
        finally
        {
            File.Delete(tabFile);
            File.Delete(csvFile);
        }
    }
}
