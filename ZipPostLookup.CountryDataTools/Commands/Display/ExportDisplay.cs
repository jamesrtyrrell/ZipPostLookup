namespace ZipPostLookup.CountryDataTools.Commands.Display;

public static class ExportDisplay
{
    public static void PrintRefHeader(string ccUpper, bool curatedOnly, string output) =>
        CommandDisplay.PrintTable($"Export — {ccUpper} — ref (source of truth)",
            ("Country",      ccUpper),
            ("Curated only", curatedOnly ? "Yes" : "No"),
            ("Output",       output));

    public static void PrintMainHeader(string ccUpper, bool curatedOnly, string output, bool isPipeline) =>
        CommandDisplay.PrintTable($"Export — {ccUpper} — main (ZipPostLookup CSV)",
            ("Country",      ccUpper),
            ("Curated only", curatedOnly ? "Yes" : "No"),
            ("Pipeline",     isPipeline ? "Optimised (range + index)" : "Standard"),
            ("Output",       output));

    public static void PrintZpiHeader(string ccUpper, bool curatedOnly, bool uncompressed, string output) =>
        CommandDisplay.PrintTable($"Export — {ccUpper} — zpi (frozen image)",
            ("Country",      ccUpper),
            ("Curated only", curatedOnly ? "Yes" : "No"),
            ("Compression",  uncompressed ? "None (.zpi)" : "Brotli (.zpi.br)"),
            ("Output",       output));
}
