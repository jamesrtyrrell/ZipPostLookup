using System.Security.Cryptography;

namespace ZipPostLookup.CountryDataTools.Utilities;

internal static class FileHash
{
    public static string Compute(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    public static string Compute(string filePath) =>
        Compute(File.ReadAllBytes(filePath));

    public static async Task WriteSidecarAsync(string filePath)
    {
        var hash = Compute(filePath);
        await File.WriteAllTextAsync(filePath + ".sha256", hash + "\n");
    }
}
