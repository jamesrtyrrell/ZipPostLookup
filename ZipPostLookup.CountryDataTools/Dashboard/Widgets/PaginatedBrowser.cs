namespace ZipPostLookup.CountryDataTools.Dashboard.Widgets;

/// <summary>
/// Shared keyboard paging loop for the ZpCode Editor browse screens. Owns the
/// ↑↓ / PgUp / PgDn / Enter / Esc mechanics and all offset/selection math; the caller supplies
/// page loading, rendering, the Enter action, and (optionally) empty-state + extra-key handling.
/// Extracted from the near-identical loops in <c>BrowseCodesAsync</c> and
/// <c>BrowseCandidateStatusAsync</c>.
/// </summary>
internal static class PaginatedBrowser
{
    /// <param name="pageSize">Rows per page (offset stride).</param>
    /// <param name="loadPage">offset → (page rows, total count).</param>
    /// <param name="render">Draws the current page (header, table, hint bar).</param>
    /// <param name="onEnter">Invoked for the selected row; the page reloads afterwards and
    ///   offset/selection are clamped.</param>
    /// <param name="onEmpty">total==0 handler; return true to exit, false to reload-from-0 and
    ///   continue (e.g. after a fix). Null = exit immediately on empty.</param>
    /// <param name="onKey">Extra-key handler for the non-empty view; return true if it changed
    ///   data (the current page reloads, selection clamped). Null = no extra keys.</param>
    public static async Task RunAsync<TRow>(
        int pageSize,
        Func<int, Task<(List<TRow> Page, int Total)>> loadPage,
        Action<List<TRow>, int, int, int> render,
        Func<TRow, Task> onEnter,
        Func<Task<bool>>? onEmpty = null,
        Func<ConsoleKey, Task<bool>>? onKey = null)
    {
        var offset        = 0;
        var selectedIndex = 0;
        var (page, total) = await loadPage(offset);

        while (true)
        {
            if (total == 0)
            {
                if (onEmpty is null) return;
                if (await onEmpty()) return;
                offset = 0;
                selectedIndex = 0;
                (page, total) = await loadPage(offset);
                continue;
            }

            render(page, selectedIndex, offset, total);

            var key = Console.ReadKey(intercept: true).Key;

            switch (key)
            {
                case ConsoleKey.UpArrow:
                    if (selectedIndex > 0)
                        selectedIndex--;
                    else if (offset > 0)
                    {
                        offset -= pageSize;
                        (page, total) = await loadPage(offset);
                        selectedIndex = page.Count - 1;
                    }
                    break;

                case ConsoleKey.DownArrow:
                    if (selectedIndex < page.Count - 1)
                        selectedIndex++;
                    else if (offset + pageSize < total)
                    {
                        offset += pageSize;
                        (page, total) = await loadPage(offset);
                        selectedIndex = 0;
                    }
                    break;

                case ConsoleKey.PageUp:
                    if (offset > 0)
                    {
                        offset = Math.Max(0, offset - pageSize);
                        (page, total) = await loadPage(offset);
                        selectedIndex = 0;
                    }
                    break;

                case ConsoleKey.PageDown:
                    if (offset + pageSize < total)
                    {
                        offset += pageSize;
                        (page, total) = await loadPage(offset);
                        selectedIndex = 0;
                    }
                    break;

                case ConsoleKey.Enter when page.Count > 0:
                    await onEnter(page[selectedIndex]);
                    (page, total) = await loadPage(offset);
                    var maxOffset = total == 0 ? 0 : ((total - 1) / pageSize) * pageSize;
                    if (offset > maxOffset)
                    {
                        offset = maxOffset;
                        (page, total) = await loadPage(offset);
                    }
                    selectedIndex = Math.Min(selectedIndex, Math.Max(0, page.Count - 1));
                    break;

                case ConsoleKey.Escape:
                    return;

                default:
                    if (onKey is not null && await onKey(key))
                    {
                        (page, total) = await loadPage(offset);
                        selectedIndex = Math.Min(selectedIndex, Math.Max(0, page.Count - 1));
                    }
                    break;
            }
        }
    }
}
