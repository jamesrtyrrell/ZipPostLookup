namespace ZipPostLookup.CountryDataTools.Commands;

/// <summary>
/// CountryDataTools db &lt;subcommand&gt;
///
/// Manages the per-folder working database configuration.
/// Replaces the old 'workdb' command — 'workdb' still works as a legacy alias.
///
/// Subcommands:
///   init    --country XX --connection "Server=..."
///   status  Show config, connection status, and recent runs.
///   newrun  --source &lt;file&gt;   Create a new run and update activeRunId.
///   test    Verify the connection and schema only.
/// </summary>
public static class DbCommand
{
    public static async Task<int> RunAsync(string[] args) =>
        await Handlers.WorkDbCommand.RunAsync(args);
}