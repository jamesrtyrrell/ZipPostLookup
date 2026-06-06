namespace ZipPostLookup.CountryDataTools.Database;

public interface IDeleteServices
{
    Task<bool> DeleteCommandAsync(
        string commonQuery, 
        string countryId = "");

}