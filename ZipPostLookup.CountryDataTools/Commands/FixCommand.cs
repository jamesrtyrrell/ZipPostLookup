namespace ZipPostLookup.CountryDataTools.Commands;

public static class FixCommand
{
    public static Task<int> RunAsync(string[] args) =>
        Handlers.FixCommand.RunAsync(args);
}
