using Spectre.Console;
using ZipPostLookup.CountryDataTools.Database;
using ZipPostLookup.CountryDataTools.Database.WorkDb;
using ZipPostLookup.CountryDataTools.Models.Dbo;

namespace ZipPostLookup.CountryDataTools.Dashboard;

/// <summary>
/// Country Management dashboard - enable/disable countries, view status, initialize from JSON.
/// </summary>
public static class CountryManagementDashboard
{
    public static async Task<int> RunAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new FigletText("Country Management").Color(Color.Cyan1));
            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Choose an action:[/]")
                    .AddChoices(
                        "View All Countries",
                        "View Enabled Countries",
                        "Enable/Disable Country",
                        "Bulk Enable Region",
                        "Initialize from JSON",
                        "← Back"
                    )
            );

            try
            {
                switch (choice)
                {
                    case "View All Countries":
                        await ViewAllCountriesAsync();
                        break;
                    case "View Enabled Countries":
                        await ViewEnabledCountriesAsync();
                        break;
                    case "Enable/Disable Country":
                        await ToggleCountryAsync();
                        break;
                    case "Bulk Enable Region":
                        await BulkEnableRegionAsync();
                        break;
                    case "Initialize from JSON":
                        await InitializeFromJsonAsync();
                        break;
                    case "← Back":
                        return 0;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.Markup("[grey]Press any key to continue...[/]");
                Console.ReadKey(true);
            }
        }
    }

    private static async Task ViewAllCountriesAsync()
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[cyan]Loading all countries...[/]");

        var db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
        var service = new CountryManagementService(db.GetFactory());
        var countries = await service.GetAllCountriesAsync();

        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[cyan]All Countries ({countries.Count} total)[/]");
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[yellow]ID[/]")
            .AddColumn("[yellow]Country Name[/]")
            .AddColumn("[yellow]Enabled[/]")
            .AddColumn("[yellow]Has Codes[/]")
            .AddColumn("[yellow]Code Count[/]")
            .AddColumn("[yellow]Status[/]");

        foreach (var country in countries.OrderBy(c => c.CountryName))
        {
            var enabledIcon = country.Enabled ? "[green]✓[/]" : "[grey]✗[/]";
            var hasCodesIcon = country.HasPostalCodes ? "[green]✓[/]" : "[grey]✗[/]";
            var codeCount = country.CodeCount > 0 ? country.CodeCount.ToString("N0") : "[grey]0[/]";
            var status = country.CurationStatus.ToString();

            table.AddRow(
                country.CountryId,
                country.CountryName,
                enabledIcon,
                hasCodesIcon,
                codeCount,
                status
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[grey]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static async Task ViewEnabledCountriesAsync()
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[cyan]Loading enabled countries...[/]");

        var db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
        var service = new CountryManagementService(db.GetFactory());
        var countries = await service.GetEnabledCountriesAsync();

        AnsiConsole.Clear();
        AnsiConsole.MarkupLine($"[cyan]Enabled Countries ({countries.Count} total)[/]");
        AnsiConsole.WriteLine();

        if (countries.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No countries are currently enabled.[/]");
        }
        else
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("[yellow]ID[/]")
                .AddColumn("[yellow]Country Name[/]")
                .AddColumn("[yellow]Code Count[/]")
                .AddColumn("[yellow]Status[/]")
                .AddColumn("[yellow]Notes[/]");

            foreach (var country in countries.OrderBy(c => c.CountryName))
            {
                var codeCount = country.CodeCount > 0 ? country.CodeCount.ToString("N0") : "[grey]0[/]";
                var status = country.DataCurated ? "[green]Curated[/]" : country.CurationStatus.ToString();
                var notes = string.IsNullOrEmpty(country.Notes) ? "[grey]-[/]" : country.Notes.Length > 50
                    ? country.Notes.Substring(0, 50) + "..."
                    : country.Notes;

                table.AddRow(
                    country.CountryId,
                    country.CountryName,
                    codeCount,
                    status,
                    notes
                );
            }

            AnsiConsole.Write(table);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[grey]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static async Task ToggleCountryAsync()
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[cyan]Enable/Disable Country[/]");
        AnsiConsole.WriteLine();

        var db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
        var service = new CountryManagementService(db.GetFactory());
        var countries = await service.GetAllCountriesAsync();

        var selectedCountry = AnsiConsole.Prompt(
            new SelectionPrompt<DataCountryInfo>()
                .Title("Select a country:")
                .PageSize(15)
                .AddChoices(countries.OrderBy(c => c.CountryName))
                .UseConverter(c => $"{c.CountryId} - {c.CountryName} [{(c.Enabled ? "Enabled" : "Disabled")}]")
        );

        var newStatus = !selectedCountry.Enabled;
        var action = newStatus ? "enable" : "disable";

        if (AnsiConsole.Confirm($"Are you sure you want to {action} [cyan]{selectedCountry.CountryName}[/]?"))
        {
            await AnsiConsole.Status()
                .StartAsync($"{(newStatus ? "Enabling" : "Disabling")} {selectedCountry.CountryName}...", async ctx =>
                {
                    if (newStatus)
                        await service.EnableCountryAsync(selectedCountry.CountryId);
                    else
                        await service.DisableCountryAsync(selectedCountry.CountryId);
                });

            AnsiConsole.MarkupLine($"[green]✓[/] {selectedCountry.CountryName} is now {(newStatus ? "enabled" : "disabled")}.");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[grey]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static async Task BulkEnableRegionAsync()
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[cyan]Bulk Enable Region[/]");
        AnsiConsole.WriteLine();

        var regions = new Dictionary<string, string[]>
        {
            ["Northern America"] = new[] { "BM", "CA", "GL", "MX", "PM", "US" },
            ["Western Europe"] = new[] { "AT", "BE", "CH", "DE", "FR", "LI", "LU", "MC", "NL" },
            ["Northern Europe"] = new[] { "DK", "EE", "FI", "GB", "IE", "IS", "LT", "LV", "NO", "SE" },
            ["Southern Europe"] = new[] { "AD", "AL", "BA", "ES", "GI", "GR", "HR", "IT", "ME", "MK", "MT", "PT", "RS", "SI", "SM", "VA" },
            ["Eastern Europe"] = new[] { "BG", "BY", "CZ", "HU", "MD", "PL", "RO", "RU", "SK", "UA" },
            ["East Asia"] = new[] { "CN", "JP", "KP", "KR", "MN", "TW" },
            ["Southeast Asia"] = new[] { "BN", "ID", "KH", "LA", "MM", "MY", "PH", "SG", "TH", "TL", "VN" },
            ["South Asia"] = new[] { "AF", "BD", "BT", "IN", "LK", "MV", "NP", "PK" },
            ["Oceania"] = new[] { "AU", "FJ", "KI", "MH", "NC", "NR", "NZ", "PF", "PG", "PW", "SB", "TO", "TV", "VU", "WF", "WS" }
        };

        var region = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a region to enable:")
                .AddChoices(regions.Keys)
        );

        var countryCodes = regions[region];
        var countryList = string.Join(", ", countryCodes);

        AnsiConsole.MarkupLine($"[yellow]This will enable {countryCodes.Length} countries:[/]");
        AnsiConsole.MarkupLine($"[grey]{countryList}[/]");
        AnsiConsole.WriteLine();

        if (AnsiConsole.Confirm($"Enable all countries in [cyan]{region}[/]?"))
        {
            int count = 0;
            await AnsiConsole.Status()
                .StartAsync($"Enabling {region} countries...", async ctx =>
                {
                    var db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
                    var service = new CountryManagementService(db.GetFactory());
                    count = await service.BulkEnableCountriesAsync(countryCodes);
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Enabled {count} countries in {region}.");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[grey]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static async Task InitializeFromJsonAsync()
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[cyan]Initialize Countries from JSON[/]");
        AnsiConsole.WriteLine();

        var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "countries.json");
        if (!File.Exists(jsonPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: countries.json not found at {jsonPath}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.Markup("[grey]Press any key to continue...[/]");
            Console.ReadKey(true);
            return;
        }

        AnsiConsole.MarkupLine($"[grey]JSON file: {jsonPath}[/]");
        AnsiConsole.WriteLine();

        if (AnsiConsole.Confirm("This will [yellow]insert new countries[/] and [yellow]update existing countries[/] from the JSON file. Continue?"))
        {
            (int inserted, int updated) result = (0, 0);

            await AnsiConsole.Status()
                .StartAsync("Initializing countries from JSON...", async ctx =>
                {
                    var db = await WorkDbContext.LoadAsync(Directory.GetCurrentDirectory());
                    var service = new CountryManagementService(db.GetFactory());
                    result = await service.InitializeFromJsonAsync(jsonPath);
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Initialization complete:");
            AnsiConsole.MarkupLine($"  - Inserted: {result.inserted}");
            AnsiConsole.MarkupLine($"  - Updated: {result.updated}");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[grey]Press any key to continue...[/]");
        Console.ReadKey(true);
    }
}
