namespace ZipPostLookup.CountryDataTools.Dashboard.Widgets;

internal static class CdtProgressBarCell
{
    public static string Render(decimal pct, int width, string color)
    {
        var filled = Math.Clamp((int)Math.Round(pct / 100.0m * width), 0, width);
        return $"[{color}]{new string('█', filled)}{new string('░', width - filled)}[/]";
    }
}
