using Spectre.Console;

namespace ZipPostLookup.CountryDataTools.Dashboard.Layout;

internal static class BreadCrumbBar
{
    public static string Markup(string path) =>
        $"[grey]  ›[/]  [bold]{Spectre.Console.Markup.Escape(path)}[/]";
}
