using System.Buffers.Binary;
using System.Text;

namespace ZipPostLookup.CountryDataTools.Export.ZPimage;

/// <summary>
/// Minimal append-only writer of little-endian primitives into a growable byte buffer.
/// Used to assemble ZP-image sections deterministically across platforms.
/// </summary>
internal sealed class BlobWriter
{
    private readonly List<byte> _bytes;

    public BlobWriter(int capacity = 0) => _bytes = new List<byte>(capacity);

    public int Length => _bytes.Count;

    public void U8(byte value) => _bytes.Add(value);

    public void U16(ushort value)
    {
        Span<byte> tmp = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(tmp, value);
        Append(tmp);
    }

    public void U32(uint value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(tmp, value);
        Append(tmp);
    }

    public void I32(int value)
    {
        Span<byte> tmp = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(tmp, value);
        Append(tmp);
    }

    public void U64(ulong value)
    {
        Span<byte> tmp = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(tmp, value);
        Append(tmp);
    }

    public void Raw(ReadOnlySpan<byte> value) => Append(value);

    /// <summary>Writes a UTF-8 string prefixed with its byte length as a <see cref="ushort"/>.</summary>
    public void Utf8WithU16Length(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
        {
            throw new ArgumentException($"String too long to length-prefix ({bytes.Length} bytes).");
        }

        U16((ushort)bytes.Length);
        Append(bytes);
    }

    public byte[] ToArray() => _bytes.ToArray();

    private void Append(ReadOnlySpan<byte> value)
    {
        foreach (var b in value)
        {
            _bytes.Add(b);
        }
    }
}
