namespace ZipPostLookup.CountryDataTools.Commands;

/// <summary>
/// CountryDataTools autopromote --country XX [--dry-run]
///                               --all        [--dry-run]
///
/// Phase 3 of Gold Name-Discrepancy Backlog Resolution.
/// Auto-promotes candidate aliases using PlaceNameNormalizer equivalence matching.
/// </summary>
public static class AutoPromoteCommand
{
    public static async Task<int> RunAsync(string[] args) =>
        await Handlers.AutoPromoteAliasesCommand.RunAsync(args);
}
