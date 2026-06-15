using ZipPostLookup.CountryDataTools.Commands;
using ZipPostLookup.CountryDataTools.Commands.Display;
using ZipPostLookup.CountryDataTools.Database.Configuration;
using ZipPostLookup.CountryDataTools.Dashboard;

// ─────────────────────────────────────────────────────────────────────────────
// ZipPostLookup.CountryDataTools
// Developer CLI for preparing, validating, enriching, and exporting
// country postal-code data for inclusion in ZipPostLookup.
//
// Not part of the NuGet package. Runs against net10.0 only.
// ─────────────────────────────────────────────────────────────────────────────

DapperPlusConfiguration.Configure();

if (ShouldPrintHelp(args))
{
    HelpText.Print();
    return 0;
}

var command = args[0].ToLowerInvariant();
var cmdArgs = args[1..];

return await RunCommandAsync(command, cmdArgs);

// ─────────────────────────────────────────────────────────────────────────────

static bool ShouldPrintHelp(string[] args) =>
    args.Length == 0 || args[0] is "-h" or "--help" or "help";

static async Task<int> RunCommandAsync(string command, string[] cmdArgs) =>
    command switch
    {
        "dashboard"     => await DashboardCommand.RunAsync(cmdArgs),
        "ingest"        => await IngestCommand.RunAsync(cmdArgs),
        "validate"      => await ValidateCommand.RunAsync(cmdArgs),
        "enrich"        => await EnrichCommand.RunAsync(cmdArgs),
        "export"        => await ExportCommand.RunAsync(cmdArgs),
        "integrity"     => await IntegrityCheckCommand.RunAsync(cmdArgs),
        "db"            => await DbCommand.RunAsync(cmdArgs),
        "analyse"       => await AnalyseCommand.RunAsync(cmdArgs),
        "fix"           => await FixCommand.RunAsync(cmdArgs),
        "extractissues" => await ExtractIssuesCommand.RunAsync(cmdArgs),
        "convert"       => await ConvertKnownFormatsCommand.RunAsync(cmdArgs),
        "snapshot"      => await SnapshotCommand.RunAsync(cmdArgs),
        "autopromote"   => await AutoPromoteCommand.RunAsync(cmdArgs),
        _               => UnknownCommand(command)
    };

static int UnknownCommand(string name)
{
    Console.Error.WriteLine($"Unknown command: {name}");
    HelpText.Print();
    return 2;
}