using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using Xunit;
using ZipPostLookup.Core;
using ZipPostLookup.ZPImage;

namespace ZipPostLookup.Tests;

/// <summary>
/// Tier 2 robustness: the opt-in structural validation pass and SHA256 sidecar verification on the
/// ZP frozen-image reader. These guard the untrusted-image load paths (<see cref="ZpImageLookup.FromImage"/>
/// / <see cref="ZpImageLookup.FromFile"/>) against corrupt input that would otherwise reach the
/// unchecked <c>Unsafe</c> hot path.
/// </summary>
public sealed class ZpImageRobustnessTests
{
    // The built-in US image is the v3 .u16.zpi.br embedded resource.
    private static string UsResourceName =>
        typeof(ZpImageLookup).Assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("us.u16.zpi.br", StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the raw (decompressed) image bytes for the built-in US image.</summary>
    private static byte[] LoadRawUsImage()
    {
        using var s = typeof(ZpImageLookup).Assembly.GetManifestResourceStream(UsResourceName)!;
        Span<byte> sizeBuf = stackalloc byte[4];
        s.ReadExactly(sizeBuf);
        var size = (int)BitConverter.ToUInt32(sizeBuf);
        var raw = new byte[size];
        using var brotli = new BrotliStream(s, CompressionMode.Decompress);
        brotli.ReadExactly(raw);
        return raw;
    }

    /// <summary>Returns the compressed on-disk bytes (size prefix + Brotli) for the built-in US image.</summary>
    private static byte[] CompressedUsImage()
    {
        using var s = typeof(ZpImageLookup).Assembly.GetManifestResourceStream(UsResourceName)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Validate_AcceptsAValidImage()
    {
        var lookup = ZpImageLookup.FromImage(LoadRawUsImage(), "State", validate: true);
        Assert.NotNull(lookup.GetByCode("10001"));
    }

    [Fact]
    public void Validate_RejectsACorruptSectionOffset()
    {
        var raw = LoadRawUsImage();

        // Section table starts at the 32-byte header; each entry is 12 bytes
        // (kind u16, reserved u16, offset u32, length u32). SortedSlots is the 7th section the
        // writer emits (index 6), so its offset field is at 32 + 6*12 + 4 = 108. It is only read
        // lazily (GetClosest/GetAll), so corrupting it does NOT crash plain construction — only the
        // validation pass should catch it.
        raw[108] = 0xF0;
        raw[109] = 0xFF;
        raw[110] = 0xFF;
        raw[111] = 0x7F; // offset = 0x7FFFFFF0, far past end of image

        // Without validation, construction succeeds and the exact-code fast path still works.
        var unchecked_ = ZpImageLookup.FromImage(raw, "State");
        Assert.NotNull(unchecked_.GetByCode("10001"));

        // With validation, the out-of-bounds section is rejected at load.
        Assert.Throws<InvalidDataException>(() => ZpImageLookup.FromImage(raw, "State", validate: true));
    }

    [Fact]
    public void VerifyHash_AcceptsAMatchingSidecar()
    {
        var (path, dir) = WriteTempImage(CompressedUsImage(), writeCorrectHash: true);
        try
        {
            var lookup = ZpImageLookup.FromFile(path, "State", verifyHash: true);
            Assert.NotNull(lookup.GetByCode("10001"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void VerifyHash_RejectsAMismatchedSidecar()
    {
        var bytes = CompressedUsImage();
        var (path, dir) = WriteTempImage(bytes, writeCorrectHash: false);
        try
        {
            Assert.Throws<InvalidDataException>(
                () => ZpImageLookup.FromFile(path, "State", verifyHash: true));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void VerifyHash_ThrowsWhenSidecarMissing()
    {
        var dir  = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "us.u16.zpi.br");
        File.WriteAllBytes(path, CompressedUsImage());
        try
        {
            Assert.Throws<FileNotFoundException>(
                () => ZpImageLookup.FromFile(path, "State", verifyHash: true));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static (string Path, string Dir) WriteTempImage(byte[] bytes, bool writeCorrectHash)
    {
        var dir  = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "us.u16.zpi.br");
        File.WriteAllBytes(path, bytes);

        var hash = writeCorrectHash
            ? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
            : new string('0', 64);
        File.WriteAllText(path + ".sha256", hash + "\n");

        return (path, dir);
    }
}
