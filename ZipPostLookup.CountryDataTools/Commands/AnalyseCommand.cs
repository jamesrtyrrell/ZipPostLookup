namespace ZipPostLookup.CountryDataTools.Commands;

public static class AnalyseCommand
{
    public static Task<int> RunAsync(string[] args) =>
        Handlers.AnalyseCommand.RunAsync(args);
}
