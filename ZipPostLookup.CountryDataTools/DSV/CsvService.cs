using System.Globalization;

namespace ZipPostLookup.CountryDataTools.DSV;

public class CsvService: ICSVService
{
    public async Task<List<T>> ParseCsvAsync<T>(string filePath) where T : class
    {
        using var reader = new StreamReader(filePath);
        using var csv = new CsvHelper.CsvReader(reader, CultureInfo.InvariantCulture);

        return await csv.GetRecordsAsync<T>().ToListAsync();
    }

    public Task<bool> WriteCSV<T>(string filePath, List<T> data, CsvType csvType) where T : class
    {
        throw new NotImplementedException();
    }

    public Task<bool> CheckCSVData<T>(string filePath, CsvType csvType) where T : class
    {
        throw new NotImplementedException();
    }
}