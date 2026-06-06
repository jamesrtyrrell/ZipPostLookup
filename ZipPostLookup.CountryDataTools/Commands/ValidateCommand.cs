namespace ZipPostLookup.CountryDataTools.Commands;

public static class ValidateCommand
{
    public static Task<int> RunAsync(string[] args) =>
        Handlers.ValidateCommand.RunAsync(args);
}
