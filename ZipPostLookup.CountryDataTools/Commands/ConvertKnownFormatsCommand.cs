namespace ZipPostLookup.CountryDataTools.Commands;

public static class ConvertKnownFormatsCommand
{
    public static Task<int> RunAsync(string[] args) =>
        Handlers.ConvertKnownFormatsCommand.RunAsync(args);
}
