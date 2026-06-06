namespace ZipPostLookup.CountryDataTools.Commands;

public static class SnapshotCommand
{
    public static Task<int> RunAsync(string[] args) =>
        Handlers.SnapshotCommand.RunAsync(args);
}
