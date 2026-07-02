using Xunit;
using ZipPostLookup.CountryDataTools.Ingestion.AutoImport;

namespace ZipPostLookup.Tests.Cdt.AutoImport;

#if NET10_0_OR_GREATER

public class JsonFlattenerServiceTests
{
    // ── ReadRows ─────────────────────────────────────────────────────────────

    [Fact]
    public void ReadRows_TopLevelArray_ReturnsHeaderPlusDataRows()
    {
        var json = """[{"zip":"10001","city":"New York"},{"zip":"90210","city":"Beverly Hills"}]""";
        using var tmp = TempJson(json);

        var rows = JsonFlattenerService.ReadRows(tmp.Path);

        Assert.Equal(3, rows.Count); // header + 2 data
        Assert.Equal(new[] { "zip", "city" }, rows[0]);
        Assert.Equal(new[] { "10001", "New York" }, rows[1]);
        Assert.Equal(new[] { "90210", "Beverly Hills" }, rows[2]);
    }

    [Fact]
    public void ReadRows_WrappedArray_PicksFirstArrayKey()
    {
        var json = """{"meta":{"count":2},"records":[{"code":"A1A"},{"code":"B2B"}]}""";
        using var tmp = TempJson(json);

        var rows = JsonFlattenerService.ReadRows(tmp.Path);

        Assert.True(rows.Count >= 2);
        Assert.Equal("code", rows[0][0]);
        Assert.Equal("A1A", rows[1][0]);
    }

    [Fact]
    public void ReadRows_NestedObject_DotPathFlattened()
    {
        var json = """[{"zip":"10001","admin":{"state":"NY","county":"Manhattan"}}]""";
        using var tmp = TempJson(json);

        var rows = JsonFlattenerService.ReadRows(tmp.Path);

        var header = rows[0];
        Assert.Contains("zip", header);
        Assert.Contains("admin.state", header);
        Assert.Contains("admin.county", header);

        var data = rows[1];
        int stateIdx = Array.IndexOf(header, "admin.state");
        Assert.Equal("NY", data[stateIdx]);
    }

    [Fact]
    public void ReadRows_ScalarArray_PipeJoined()
    {
        var json = """[{"zip":"10001","tags":["postal","primary"]}]""";
        using var tmp = TempJson(json);

        var rows = JsonFlattenerService.ReadRows(tmp.Path);

        var header = rows[0];
        int tagIdx = Array.IndexOf(header, "tags");
        Assert.True(tagIdx >= 0, "Expected 'tags' column");
        Assert.Equal("postal|primary", rows[1][tagIdx]);
    }

    [Fact]
    public void ReadRows_MissingFieldInSomeRows_EmptyStringFill()
    {
        var json = """[{"zip":"10001","city":"New York","state":"NY"},{"zip":"90210"}]""";
        using var tmp = TempJson(json);

        var rows = JsonFlattenerService.ReadRows(tmp.Path);

        Assert.Equal(3, rows.Count);
        var header = rows[0];
        int cityIdx = Array.IndexOf(header, "city");
        int stateIdx = Array.IndexOf(header, "state");
        Assert.Equal("", rows[2][cityIdx]);
        Assert.Equal("", rows[2][stateIdx]);
    }

    [Fact]
    public void ReadRows_MaxRows_LimitsDataRows()
    {
        var items = string.Join(",", Enumerable.Range(1, 20).Select(i => $"{{\"n\":\"{i}\"}}"));
        var json = $"[{items}]";
        using var tmp = TempJson(json);

        var rows = JsonFlattenerService.ReadRows(tmp.Path, maxRows: 5);

        Assert.Equal(6, rows.Count); // header + 5
    }

    [Fact]
    public void ReadRows_UnionKeyHeader_CoversSparseObjects()
    {
        // Object 1 has key "a", object 2 has key "b" — both should appear in header
        var json = """[{"a":"1"},{"b":"2"}]""";
        using var tmp = TempJson(json);

        var rows = JsonFlattenerService.ReadRows(tmp.Path);

        var header = rows[0];
        Assert.Contains("a", header);
        Assert.Contains("b", header);
    }

    [Fact]
    public void ReadRows_NullValues_EmptyString()
    {
        var json = """[{"zip":"10001","city":null}]""";
        using var tmp = TempJson(json);

        var rows = JsonFlattenerService.ReadRows(tmp.Path);

        var header = rows[0];
        int cityIdx = Array.IndexOf(header, "city");
        Assert.Equal("", rows[1][cityIdx]);
    }

    // ── ConvertToCsv ─────────────────────────────────────────────────────────

    [Fact]
    public void ConvertToCsv_ProducesTempFile_WithCorrectContent()
    {
        var json = """[{"zip":"10001","city":"New York"}]""";
        using var tmp = TempJson(json);

        string? csvPath = null;
        try
        {
            csvPath = JsonFlattenerService.ConvertToCsv(tmp.Path);

            Assert.True(File.Exists(csvPath));
            var lines = File.ReadAllLines(csvPath);
            Assert.True(lines.Length >= 2); // header + 1 data row
            Assert.Contains("zip", lines[0]);
            Assert.Contains("10001", lines[1]);
        }
        finally
        {
            if (csvPath != null) File.Delete(csvPath);
        }
    }

    [Fact]
    public void ConvertToCsv_CommasInValues_QuotedCorrectly()
    {
        var json = """[{"zip":"10001","city":"New York, NY"}]""";
        using var tmp = TempJson(json);

        string? csvPath = null;
        try
        {
            csvPath = JsonFlattenerService.ConvertToCsv(tmp.Path);
            var lines = File.ReadAllLines(csvPath);
            Assert.Contains("\"New York, NY\"", lines[1]);
        }
        finally
        {
            if (csvPath != null) File.Delete(csvPath);
        }
    }

    [Fact]
    public void ReadRows_InvalidRootKind_Throws()
    {
        var json = "\"just a string\"";
        using var tmp = TempJson(json);

        Assert.Throws<InvalidOperationException>(() => JsonFlattenerService.ReadRows(tmp.Path));
    }

    [Fact]
    public void ReadRows_ObjectWithNoArrayProperty_Throws()
    {
        var json = """{"a":1,"b":2}""";
        using var tmp = TempJson(json);

        Assert.Throws<InvalidOperationException>(() => JsonFlattenerService.ReadRows(tmp.Path));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static TempFile TempJson(string content)
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".json");
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);
        return new TempFile(path);
    }

    private sealed class TempFile(string path) : IDisposable
    {
        public string Path { get; } = path;
        public void Dispose() { try { File.Delete(Path); } catch { } }
    }
}

#endif
