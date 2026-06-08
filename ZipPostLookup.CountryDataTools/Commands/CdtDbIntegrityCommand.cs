namespace ZipPostLookup.CountryDataTools.Commands;

public static class CdtDbIntegrityCommand
{
    public static Task<int> RunAsync(string[] args) =>
        Handlers.CdtDbIntegrityCommand.RunAsync(args);
}
