namespace ZipPostLookup.CountryDataTools.Dashboard;

internal sealed record CommandEntry(
    string Name,
    string Description,
    Func<Task<int>> ShowHelp,
    bool IsInteractive = false);
