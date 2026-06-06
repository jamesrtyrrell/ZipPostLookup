namespace ZipPostLookup.CountryDataTools.Commands;

public static class ExtractIssuesCommand
{
    public static Task<int> RunAsync(string[] args) =>
        Handlers.ExtractIssuesCommand.RunAsync(args);
}
