using ZipPostLookup.CountryDataTools.Commands.Handlers;
using ZipPostLookup.CountryDataTools.Dashboard.Layout;
using ZipPostLookup.CountryDataTools.Dashboard.Widgets;

namespace ZipPostLookup.CountryDataTools.Dashboard;

internal static class FindGoldDashboard
{
    private const string AllLabel = "All (US + CA + MX)";

    public static async Task<int> RunAsync()
    {
        HeaderBar.Render("Find Gold");

        var country = CountryPicker.Show(
            title: "Certify Gold codes for:",
            cancelLabel: "← Cancel",
            allLabel: AllLabel,
            allDescription: "all pipeline countries");

        if (country == "← Cancel") return 0;

        var all = country == AllLabel;

        HeaderBar.Render("Find Gold");

        var exitCode = await FindGoldCommand.RunAsync(
            new FindGoldCommand.Options(Country: all ? "" : country, All: all));

        FooterBar.ShowResultAndPause(exitCode);
        return exitCode;
    }
}
