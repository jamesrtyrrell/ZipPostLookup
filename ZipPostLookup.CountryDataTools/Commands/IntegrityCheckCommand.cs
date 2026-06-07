namespace ZipPostLookup.CountryDataTools.Commands;

public static class IntegrityCheckCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        // integrity cdt-db [options]  → DB quality scan
        // integrity zpl-data [options] → existing ZPL-vs-DB check (default)
        // integrity [options]          → same as zpl-data (backwards compatible)
        if (args.Length > 0 && args[0].Equals("cdt-db", StringComparison.OrdinalIgnoreCase))
            return Handlers.CdtDbIntegrityCommand.RunAsync(args[1..]);

        if (args.Length > 0 && args[0].Equals("zpl-data", StringComparison.OrdinalIgnoreCase))
            return Handlers.IntegrityCheckCommand.RunAsync(args[1..]);

        return Handlers.IntegrityCheckCommand.RunAsync(args);
    }
}
