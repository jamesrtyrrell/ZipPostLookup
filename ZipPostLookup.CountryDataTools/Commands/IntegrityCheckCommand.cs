namespace ZipPostLookup.CountryDataTools.Commands;

public static class IntegrityCheckCommand
{
    public static Task<int> RunAsync(string[] args) =>
        Handlers.IntegrityCheckCommand.RunAsync(args);
}
