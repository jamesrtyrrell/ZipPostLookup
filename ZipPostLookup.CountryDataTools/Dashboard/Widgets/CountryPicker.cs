using Spectre.Console;

namespace ZipPostLookup.CountryDataTools.Dashboard.Widgets;

/// <summary>
/// Standard country selection prompt shared by the dashboard screens. Replaces the
/// per-screen <see cref="CdtSelectMenu.Show{T}"/> call + identical converter switch
/// (~17 copies). Returns the selected label <b>verbatim</b> — callers keep comparing
/// against <paramref name="cancelLabel"/>, the All/Any labels, and <c>.StartsWith("All")</c>
/// exactly as before. Screen-specific text (title, labels, descriptions, country set) is
/// passed in; only the visual styling is standardised.
/// </summary>
internal static class CountryPicker
{
    /// <summary>
    /// Shows the picker and returns the chosen label (one of <paramref name="countries"/>,
    /// <paramref name="allLabel"/>, <paramref name="anyLabel"/>, or <paramref name="cancelLabel"/>).
    /// Esc returns <paramref name="cancelLabel"/>.
    /// </summary>
    /// <param name="title">Prompt title, e.g. "Country:".</param>
    /// <param name="cancelLabel">The back/cancel entry, e.g. "← Cancel" or "← Back".</param>
    /// <param name="countries">The selectable country codes, e.g. ["US","CA","MX"] or ["US","MX"].</param>
    /// <param name="allLabel">Optional "all countries" entry, e.g. "All (US + CA + MX)". Null = omit.</param>
    /// <param name="allDescription">Grey description shown beside <paramref name="allLabel"/>.</param>
    /// <param name="anyLabel">Optional "any / no filter" entry, e.g. "Any (no filter)". Null = omit.</param>
    public static string Show(
        string title,
        string cancelLabel,
        IReadOnlyList<string>? countries = null,
        string? allLabel = null,
        string? allDescription = null,
        string? anyLabel = null)
    {
        countries ??= ["US", "CA", "MX"];

        var choices = new List<string>(countries);
        if (allLabel is not null) choices.Add(allLabel);
        if (anyLabel is not null) choices.Add(anyLabel);
        choices.Add(cancelLabel);

        return CdtSelectMenu.Show(
            choices,
            s =>
            {
                if (s == cancelLabel)                         return $"[grey]{Markup.Escape(s)}[/]";
                if (anyLabel is not null && s == anyLabel)    return $"[grey]{Markup.Escape(s)}[/]";
                if (allLabel is not null && s == allLabel)
                    return allDescription is null
                        ? $"[bold cyan]{Markup.Escape(s)}[/]"
                        : $"[bold cyan]{"All",-14}[/]  [grey]{Markup.Escape(allDescription)}[/]";
                return $"[bold cyan]{Markup.Escape(s)}[/]";
            },
            escapeReturns: cancelLabel,
            title: title);
    }
}
