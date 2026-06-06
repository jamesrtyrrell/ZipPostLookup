namespace ZipPostLookup.CountryDataTools.DSV;

public interface ICSVService
{
    Task<List<T>> ParseCsvAsync<T>(string filePath) where T : class;
    Task<bool> WriteCSV<T>(string filePath, List<T> data, CsvType csvType) where T : class;
    Task<bool> CheckCSVData<T>(string filePath, CsvType csvType) where T : class;
}