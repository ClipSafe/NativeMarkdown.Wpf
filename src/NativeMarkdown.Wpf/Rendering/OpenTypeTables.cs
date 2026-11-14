using System.Text;

namespace NativeMarkdown.Wpf.Rendering;

/// <summary>Big-endian helpers for the OpenType tables used by the color emoji renderer.</summary>
internal static class OpenTypeTables
{
    public static Dictionary<string, uint> ReadDirectory(byte[] data)
    {
        var tables = new Dictionary<string, uint>(StringComparer.Ordinal);
        if (data.Length < 12)
            return tables;

        var count = ReadU16(data, 4);
        for (var i = 0; i < count; i++)
        {
            var record = 12u + ((uint)i * 16u);
            if (record + 16 > data.Length)
                break;

            var tag = Encoding.ASCII.GetString(data, (int)record, 4);
            tables[tag] = ReadU32(data, record + 8);
        }

        return tables;
    }

    public static ushort ReadU16(byte[] data, uint offset) =>
        offset + 1 < data.Length ? (ushort)((data[offset] << 8) | data[offset + 1]) : (ushort)0;

    public static short ReadI16(byte[] data, uint offset) =>
        unchecked((short)ReadU16(data, offset));

    public static uint ReadU32(byte[] data, uint offset) =>
        offset + 3 < data.Length
            ? ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
              ((uint)data[offset + 2] << 8) | data[offset + 3]
            : 0;
}

/// <summary>A glyph coverage table, used by both substitution and positioning rules.</summary>
internal readonly struct OpenTypeCoverage
{
    private readonly byte[]? _data;
    private readonly uint _listOffset;
    private readonly ushort _count;
    private readonly uint _rangeOffset;
    private readonly ushort _rangeCount;

    private OpenTypeCoverage(byte[] data, uint listOffset, ushort count, uint rangeOffset, ushort rangeCount)
    {
        _data = data;
        _listOffset = listOffset;
        _count = count;
        _rangeOffset = rangeOffset;
        _rangeCount = rangeCount;
    }

    public bool IsEmpty => _data is null;

    public static OpenTypeCoverage Read(byte[] data, uint offset)
    {
        var format = OpenTypeTables.ReadU16(data, offset);

        if (format == 1)
            return new OpenTypeCoverage(data, offset + 4, OpenTypeTables.ReadU16(data, offset + 2), 0, 0);

        if (format == 2)
            return new OpenTypeCoverage(data, 0, 0, offset + 4, OpenTypeTables.ReadU16(data, offset + 2));

        return default;
    }

    public bool Contains(ushort glyph) => IndexOf(glyph) >= 0;

    public int IndexOf(ushort glyph)
    {
        if (_data is null)
            return -1;

        if (_rangeCount == 0)
        {
            for (var i = 0; i < _count; i++)
            {
                if (OpenTypeTables.ReadU16(_data, _listOffset + ((uint)i * 2u)) == glyph)
                    return i;
            }

            return -1;
        }

        for (var i = 0; i < _rangeCount; i++)
        {
            var record = _rangeOffset + ((uint)i * 6u);
            var start = OpenTypeTables.ReadU16(_data, record);
            var end = OpenTypeTables.ReadU16(_data, record + 2);
            if (glyph < start || glyph > end)
                continue;

            var startIndex = OpenTypeTables.ReadU16(_data, record + 4);
            return startIndex + (glyph - start);
        }

        return -1;
    }
}
