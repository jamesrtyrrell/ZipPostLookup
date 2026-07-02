using Xunit;
using ZipPostLookup.CountryDataTools.Ingestion.AutoImport;
using ZipPostLookup.CountryDataTools.Ingestion.Models;

namespace ZipPostLookup.Tests.Cdt.AutoImport;

#if NET10_0_OR_GREATER

public class FileSnifferServiceTests
{
    private readonly FileSnifferService _sniffer = new();

    [Fact]
    public void Sniff_UsSampleTsv_DetectsCorrectFormat()
    {
        // Arrange
        var filePath = Path.Combine("samples", "Northern America", "US.tsv");

        // Act
        var result = _sniffer.Sniff(filePath, sampleRows: 20);

        // Assert
        Assert.Equal(FileFormat.Tsv, result.Format);
        Assert.Equal('\t', result.Delimiter);
        // First row is the numeric index row "0	1	2	…	11" — a "fake header". The sniffer
        // reports HasHeaderRow=true so every downstream reader skips it, but HeaderNames stays
        // null because the row carries no real column names.
        Assert.True(result.HasHeaderRow);
        Assert.Null(result.HeaderNames);
        Assert.Equal(12, result.ColumnCount);
        Assert.Empty(result.AmbiguityReasons);
    }

    [Fact]
    public void Sniff_CsvWithHeader_DetectsHeaderRow()
    {
        // Arrange - create temp CSV with header
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "ZipCode,City,State,Latitude,Longitude\n02134,Boston,MA,42.3601,-71.0589\n10001,New York,NY,40.7506,-73.9971\n");

        try
        {
            // Act
            var result = _sniffer.Sniff(tempFile, sampleRows: 10);

            // Assert
            Assert.Equal(FileFormat.Csv, result.Format);
            Assert.Equal(',', result.Delimiter);
            Assert.True(result.HasHeaderRow);
            Assert.Equal(5, result.ColumnCount);
            Assert.NotNull(result.HeaderNames);
            Assert.Equal("ZipCode", result.HeaderNames[0]);
            Assert.Equal("City", result.HeaderNames[1]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Sniff_CsvWithoutHeader_DetectsNoHeader()
    {
        // Arrange - create temp CSV without header (all numeric first row)
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "02134,42.3601,-71.0589\n10001,40.7506,-73.9971\n90210,34.0901,-118.4065\n");

        try
        {
            // Act
            var result = _sniffer.Sniff(tempFile, sampleRows: 10);

            // Assert
            Assert.False(result.HasHeaderRow); // First row is 0% alphabetic
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Sniff_InconsistentColumnCount_DetectsAmbiguity()
    {
        // Arrange - create temp file with varying column counts
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "a,b,c\nd,e\nf,g,h,i\nj,k,l\n");

        try
        {
            // Act
            var result = _sniffer.Sniff(tempFile, sampleRows: 10);

            // Assert
            Assert.Contains(result.AmbiguityReasons, r => r.Contains("Inconsistent column count"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Sniff_EmptyFile_ReturnsEmptyResult()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "");

        try
        {
            // Act
            var result = _sniffer.Sniff(tempFile, sampleRows: 10);

            // Assert
            Assert.Contains(result.AmbiguityReasons, r => r.Contains("empty"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Sniff_Utf8WithBom_DetectsUtf8()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var utf8WithBom = new System.Text.UTF8Encoding(true);
        File.WriteAllText(tempFile, "col1,col2\nval1,val2\n", utf8WithBom);

        try
        {
            // Act
            var result = _sniffer.Sniff(tempFile, sampleRows: 10);

            // Assert
            Assert.Equal(System.Text.Encoding.UTF8, result.Encoding);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Sniff_JsonContent_DetectsJsonFormat()
    {
        // Arrange — top-level array JSON
        var tempFile = Path.ChangeExtension(Path.GetTempFileName(), ".json");
        File.WriteAllText(tempFile, """[{"zip":"10001","city":"New York"}]""");

        try
        {
            var result = _sniffer.Sniff(tempFile);

            Assert.Equal(FileFormat.Json, result.Format);
            Assert.Empty(result.AmbiguityReasons);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Sniff_JsonObjectRoot_DetectsJsonFormat()
    {
        // Arrange — root object shape
        var tempFile = Path.ChangeExtension(Path.GetTempFileName(), ".json");
        File.WriteAllText(tempFile, """{"records":[{"zip":"10001"}]}""");

        try
        {
            var result = _sniffer.Sniff(tempFile);

            Assert.Equal(FileFormat.Json, result.Format);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Sniff_JsonWithUtf8Bom_DetectsJsonFormat()
    {
        // Arrange — UTF-8 BOM + JSON
        var tempFile = Path.ChangeExtension(Path.GetTempFileName(), ".json");
        var utf8Bom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        File.WriteAllText(tempFile, """[{"zip":"10001"}]""", utf8Bom);

        try
        {
            var result = _sniffer.Sniff(tempFile);

            Assert.Equal(FileFormat.Json, result.Format);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Sniff_XlsxMagicBytes_DetectsExcelFormat()
    {
        // Arrange — file with .xlsx extension and XLSX/ZIP magic bytes
        var tempFile = Path.ChangeExtension(Path.GetTempFileName(), ".xlsx");
        // Minimal ZIP/PK header (XLSX magic) followed by padding
        var header = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00 };
        File.WriteAllBytes(tempFile, header);

        try
        {
            var result = _sniffer.Sniff(tempFile);

            Assert.Equal(FileFormat.Excel, result.Format);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Sniff_XlsMagicBytes_DetectsExcelFormat()
    {
        // Arrange — file with .xls magic bytes (compound document header)
        var tempFile = Path.ChangeExtension(Path.GetTempFileName(), ".xls");
        var header = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00 };
        File.WriteAllBytes(tempFile, header);

        try
        {
            var result = _sniffer.Sniff(tempFile);

            Assert.Equal(FileFormat.Excel, result.Format);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}

#endif
