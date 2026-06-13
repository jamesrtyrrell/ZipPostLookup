namespace ZipPostLookup.CountryDataTools.Utilities;

public static class FileTools
{
    public static string StripPathQuotes(string path)
    {
        path = path.Trim();
        if (path.Length >= 2 &&
            ((path[0] == '"'  && path[^1] == '"') ||
             (path[0] == '\'' && path[^1] == '\'')))
            path = path[1..^1].Trim();
        return path;
    }
}