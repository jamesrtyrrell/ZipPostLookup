using System.Text;

namespace ZipPostLookup.CountryDataTools.Export.ZPimage;

/// <summary>
/// Deduplicating pool of UTF-8 strings. Each distinct string is interned once and
/// referenced everywhere by a stable integer index. Serialises to the <c>NamePool</c>
/// section: a <c>count</c>, a <c>count+1</c> offsets table, then the concatenated bytes,
/// so a reader recovers string <c>i</c> as <c>bytes[offsets[i]..offsets[i+1]]</c> with no
/// per-string length prefix and no allocation beyond the slice.
///
/// <para>Strings are compared ordinally (case-sensitive) because names are display values —
/// "St. John" and "St. john" are genuinely different and must not be merged.</para>
/// </summary>
internal sealed class StringPool
{
    private readonly Dictionary<string, int> _indexByValue = new(StringComparer.Ordinal);
    private readonly List<string> _values = new();

    /// <summary>Number of distinct strings interned so far.</summary>
    public int Count => _values.Count;

    /// <summary>Returns the stable index for <paramref name="value"/>, interning it if new.</summary>
    public int Intern(string value)
    {
        if (_indexByValue.TryGetValue(value, out var index))
        {
            return index;
        }

        index = _values.Count;
        _indexByValue[value] = index;
        _values.Add(value);
        return index;
    }

    /// <summary>Serialises the pool to its <c>NamePool</c> section bytes.</summary>
    public byte[] Serialize()
    {
        var encoded = new byte[_values.Count][];
        var total = 0;
        for (var i = 0; i < _values.Count; i++)
        {
            encoded[i] = Encoding.UTF8.GetBytes(_values[i]);
            total += encoded[i].Length;
        }

        var writer = new BlobWriter(capacity: 4 + (_values.Count + 1) * 4 + total);
        writer.U32((uint)_values.Count);

        // Offsets table: offsets[0..count] where offsets[count] == total byte length.
        uint cursor = 0;
        writer.U32(cursor);
        foreach (var bytes in encoded)
        {
            cursor += (uint)bytes.Length;
            writer.U32(cursor);
        }

        foreach (var bytes in encoded)
        {
            writer.Raw(bytes);
        }

        return writer.ToArray();
    }
}
