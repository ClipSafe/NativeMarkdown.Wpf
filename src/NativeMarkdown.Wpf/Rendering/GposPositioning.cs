using System.Windows.Media;

namespace NativeMarkdown.Wpf.Rendering;

/// <summary>
/// Glyph positioning for emoji sequences. Some sequences, such as people holding hands, are drawn from
/// several glyphs that the font pulls together with contextual adjustments rather than one composed
/// glyph. Only the sub-table formats those lookups use are implemented.
/// </summary>
internal sealed class GposPositioning
{
    public static readonly GposPositioning Empty = new([], [], []);

    private readonly byte[] _data;

    /// <summary>Every lookup in the font, indexed by LookupList position, because chained context rules
    /// address nested lookups by that index rather than by feature membership.</summary>
    private readonly Lookup?[] _lookups;

    /// <summary>Lookups reached from a feature; only these run at the top level.</summary>
    private readonly int[] _topLevel;

    private GposPositioning(byte[] data, Lookup?[] lookups, int[] topLevel)
    {
        _data = data;
        _lookups = lookups;
        _topLevel = topLevel;
    }

    public bool IsEmpty => _topLevel.Length == 0;

    public static GposPositioning Read(byte[] data, uint gpos)
    {
        var scriptList = gpos + OpenTypeTables.ReadU16(data, gpos + 4);
        var featureList = gpos + OpenTypeTables.ReadU16(data, gpos + 6);
        var lookupList = gpos + OpenTypeTables.ReadU16(data, gpos + 8);

        var lookupCount = OpenTypeTables.ReadU16(data, lookupList);
        var lookups = new Lookup?[lookupCount];

        for (var i = 0; i < lookupCount; i++)
        {
            var lookup = lookupList + OpenTypeTables.ReadU16(data, lookupList + 2u + ((uint)i * 2u));
            var type = OpenTypeTables.ReadU16(data, lookup);
            if (type is < 1 or > 8)
                continue;

            var subTableCount = OpenTypeTables.ReadU16(data, lookup + 4);
            var subTables = new List<uint>(subTableCount);
            for (var j = 0; j < subTableCount; j++)
                subTables.Add(lookup + OpenTypeTables.ReadU16(data, lookup + 6u + ((uint)j * 2u)));

            lookups[i] = new Lookup(type, subTables);
        }

        var topLevel = ReadReferencedLookups(data, scriptList, featureList)
            .Where(index => index < lookupCount && lookups[index] is not null)
            .ToArray();

        return new GposPositioning(data, lookups, topLevel);
    }

    /// <summary>Adjusts the placement of each shaped glyph in place.</summary>
    public void Apply(List<GlyphPlacement> glyphs)
    {
        foreach (var index in _topLevel)
        {
            if (_lookups[index] is not Lookup lookup)
                continue;

            for (var position = 0; position < glyphs.Count; position++)
                ApplyAt(lookup, glyphs, position, depth: 0);
        }
    }

    private bool ApplyAt(Lookup lookup, List<GlyphPlacement> glyphs, int index, int depth)
    {
        if (depth > 8 || index >= glyphs.Count)
            return false;

        foreach (var subTable in lookup.SubTables)
        {
            var changed = lookup.Type switch
            {
                1 => ApplySingle(subTable, glyphs, index),
                2 => ApplyPair(subTable, glyphs, index),
                8 => ApplyChainContext(subTable, glyphs, index, depth),
                _ => false
            };

            if (changed)
                return true;
        }

        return false;
    }

    private bool ApplySingle(uint subTable, List<GlyphPlacement> glyphs, int index)
    {
        var format = OpenTypeTables.ReadU16(_data, subTable);
        var coverage = OpenTypeCoverage.Read(_data, subTable + OpenTypeTables.ReadU16(_data, subTable + 2));
        var covered = coverage.IndexOf(glyphs[index].Glyph);
        if (covered < 0)
            return false;

        var valueFormat = OpenTypeTables.ReadU16(_data, subTable + 4);
        var valueSize = ValueSize(valueFormat);

        if (format == 1)
        {
            ApplyValue(subTable + 6, valueFormat, glyphs[index]);
            return true;
        }

        if (format != 2)
            return false;

        var valueCount = OpenTypeTables.ReadU16(_data, subTable + 6);
        if (covered >= valueCount)
            return false;

        ApplyValue(subTable + 8u + ((uint)covered * (uint)valueSize), valueFormat, glyphs[index]);
        return true;
    }

    private bool ApplyPair(uint subTable, List<GlyphPlacement> glyphs, int index)
    {
        if (index + 1 >= glyphs.Count)
            return false;

        var format = OpenTypeTables.ReadU16(_data, subTable);
        var coverage = OpenTypeCoverage.Read(_data, subTable + OpenTypeTables.ReadU16(_data, subTable + 2));
        var covered = coverage.IndexOf(glyphs[index].Glyph);
        if (covered < 0)
            return false;

        var valueFormat1 = OpenTypeTables.ReadU16(_data, subTable + 4);
        var valueFormat2 = OpenTypeTables.ReadU16(_data, subTable + 6);
        var size1 = ValueSize(valueFormat1);
        var size2 = ValueSize(valueFormat2);

        if (format == 1)
        {
            var pairSetCount = OpenTypeTables.ReadU16(_data, subTable + 8);
            if (covered >= pairSetCount)
                return false;

            var pairSet = subTable + OpenTypeTables.ReadU16(_data, subTable + 10u + ((uint)covered * 2u));
            var pairCount = OpenTypeTables.ReadU16(_data, pairSet);
            var recordSize = 2 + size1 + size2;

            for (var i = 0; i < pairCount; i++)
            {
                var record = pairSet + 2u + ((uint)i * (uint)recordSize);
                if (OpenTypeTables.ReadU16(_data, record) != glyphs[index + 1].Glyph)
                    continue;

                ApplyValue(record + 2u, valueFormat1, glyphs[index]);
                ApplyValue(record + 2u + (uint)size1, valueFormat2, glyphs[index + 1]);
                return true;
            }

            return false;
        }

        if (format != 2)
            return false;

        var classDef1 = ReadClassDef(subTable + OpenTypeTables.ReadU16(_data, subTable + 8));
        var classDef2 = ReadClassDef(subTable + OpenTypeTables.ReadU16(_data, subTable + 10));
        var class1Count = OpenTypeTables.ReadU16(_data, subTable + 12);
        var class2Count = OpenTypeTables.ReadU16(_data, subTable + 14);
        var class1 = classDef1(glyphs[index].Glyph);
        var class2 = classDef2(glyphs[index + 1].Glyph);

        if (class1 >= class1Count || class2 >= class2Count)
            return false;

        var majorSize = size1 + size2;
        var offset = subTable + 16u + ((((uint)class1 * (uint)class2Count) + (uint)class2) * (uint)majorSize);

        ApplyValue(offset, valueFormat1, glyphs[index]);
        ApplyValue(offset + (uint)size1, valueFormat2, glyphs[index + 1]);
        return true;
    }

    private bool ApplyChainContext(uint subTable, List<GlyphPlacement> glyphs, int index, int depth)
    {
        if (OpenTypeTables.ReadU16(_data, subTable) != 3)
            return false;

        var cursor = subTable + 2u;

        var backtrackCount = OpenTypeTables.ReadU16(_data, cursor);
        cursor += 2;
        var backtracks = new OpenTypeCoverage[backtrackCount];
        for (var i = 0; i < backtrackCount; i++)
        {
            backtracks[i] = OpenTypeCoverage.Read(_data, subTable + OpenTypeTables.ReadU16(_data, cursor));
            cursor += 2;
        }

        var inputCount = OpenTypeTables.ReadU16(_data, cursor);
        cursor += 2;
        var inputs = new OpenTypeCoverage[inputCount];
        for (var i = 0; i < inputCount; i++)
        {
            inputs[i] = OpenTypeCoverage.Read(_data, subTable + OpenTypeTables.ReadU16(_data, cursor));
            cursor += 2;
        }

        var lookaheadCount = OpenTypeTables.ReadU16(_data, cursor);
        cursor += 2;
        var lookaheads = new OpenTypeCoverage[lookaheadCount];
        for (var i = 0; i < lookaheadCount; i++)
        {
            lookaheads[i] = OpenTypeCoverage.Read(_data, subTable + OpenTypeTables.ReadU16(_data, cursor));
            cursor += 2;
        }

        if (inputCount == 0 ||
            index - backtrackCount < 0 ||
            index + inputCount + lookaheadCount > glyphs.Count)
        {
            return false;
        }

        for (var i = 0; i < inputCount; i++)
        {
            if (!inputs[i].Contains(glyphs[index + i].Glyph))
                return false;
        }

        // Backtrack coverage is stored nearest-first.
        for (var i = 0; i < backtrackCount; i++)
        {
            if (!backtracks[i].Contains(glyphs[index - 1 - i].Glyph))
                return false;
        }

        for (var i = 0; i < lookaheadCount; i++)
        {
            if (!lookaheads[i].Contains(glyphs[index + inputCount + i].Glyph))
                return false;
        }

        var recordCount = OpenTypeTables.ReadU16(_data, cursor);
        cursor += 2;
        var changed = false;

        for (var i = 0; i < recordCount; i++)
        {
            var record = cursor + ((uint)i * 4u);
            var sequenceIndex = OpenTypeTables.ReadU16(_data, record);
            var lookupIndex = OpenTypeTables.ReadU16(_data, record + 2);

            if (sequenceIndex >= inputCount || lookupIndex >= _lookups.Length)
                continue;

            if (_lookups[lookupIndex] is not Lookup nested)
                continue;

            var target = index + sequenceIndex;
            if (target < glyphs.Count)
                changed |= ApplyAt(nested, glyphs, target, depth + 1);
        }

        return changed;
    }

    private void ApplyValue(uint offset, ushort format, GlyphPlacement placement)
    {
        if ((format & 0x0001) != 0)
        {
            placement.XOffset += OpenTypeTables.ReadI16(_data, offset);
            offset += 2;
        }

        if ((format & 0x0002) != 0)
        {
            placement.YOffset += OpenTypeTables.ReadI16(_data, offset);
            offset += 2;
        }

        if ((format & 0x0004) != 0)
        {
            placement.XAdvance += OpenTypeTables.ReadI16(_data, offset);
            offset += 2;
        }

        if ((format & 0x0008) != 0)
            placement.YAdvance += OpenTypeTables.ReadI16(_data, offset);
    }

    private static int ValueSize(ushort format)
    {
        var size = 0;
        for (var bit = 0; bit < 8; bit++)
        {
            if ((format & (1 << bit)) != 0)
                size += 2;
        }

        return size;
    }

    private Func<ushort, ushort> ReadClassDef(uint offset)
    {
        if (offset == 0)
            return _ => 0;

        var format = OpenTypeTables.ReadU16(_data, offset);

        if (format == 1)
        {
            var start = OpenTypeTables.ReadU16(_data, offset + 2);
            var count = OpenTypeTables.ReadU16(_data, offset + 4);
            return glyph =>
            {
                var index = glyph - start;
                return index >= 0 && index < count
                    ? OpenTypeTables.ReadU16(_data, offset + 6u + ((uint)index * 2u))
                    : (ushort)0;
            };
        }

        if (format == 2)
        {
            var rangeCount = OpenTypeTables.ReadU16(_data, offset + 2);
            return glyph =>
            {
                for (var i = 0; i < rangeCount; i++)
                {
                    var record = offset + 4u + ((uint)i * 6u);
                    var start = OpenTypeTables.ReadU16(_data, record);
                    var end = OpenTypeTables.ReadU16(_data, record + 2);
                    if (glyph >= start && glyph <= end)
                        return OpenTypeTables.ReadU16(_data, record + 4);
                }

                return 0;
            };
        }

        return _ => 0;
    }

    private static List<int> ReadReferencedLookups(byte[] data, uint scriptList, uint featureList)
    {
        var features = new HashSet<int>();
        var scriptCount = OpenTypeTables.ReadU16(data, scriptList);

        for (var i = 0; i < scriptCount; i++)
        {
            var record = scriptList + 2u + ((uint)i * 6u);
            var script = scriptList + OpenTypeTables.ReadU16(data, record + 4);
            var defaultLangSys = OpenTypeTables.ReadU16(data, script);

            if (defaultLangSys != 0)
                AddLangSysFeatures(data, script + defaultLangSys, features);

            var langSysCount = OpenTypeTables.ReadU16(data, script + 2);
            for (var j = 0; j < langSysCount; j++)
            {
                var langRecord = script + 4u + ((uint)j * 6u);
                AddLangSysFeatures(data, script + OpenTypeTables.ReadU16(data, langRecord + 4), features);
            }
        }

        var lookups = new List<int>();
        var featureCount = OpenTypeTables.ReadU16(data, featureList);

        for (var i = 0; i < featureCount; i++)
        {
            if (!features.Contains(i))
                continue;

            var record = featureList + 2u + ((uint)i * 6u);
            var feature = featureList + OpenTypeTables.ReadU16(data, record + 4);
            var lookupCount = OpenTypeTables.ReadU16(data, feature + 2);

            for (var j = 0; j < lookupCount; j++)
            {
                var index = OpenTypeTables.ReadU16(data, feature + 4u + ((uint)j * 2u));
                if (!lookups.Contains(index))
                    lookups.Add(index);
            }
        }

        return lookups;
    }

    private static void AddLangSysFeatures(byte[] data, uint langSys, HashSet<int> features)
    {
        var count = OpenTypeTables.ReadU16(data, langSys + 4);
        for (var i = 0; i < count; i++)
            features.Add(OpenTypeTables.ReadU16(data, langSys + 6u + ((uint)i * 2u)));
    }

    private readonly record struct Lookup(ushort Type, List<uint> SubTables);
}

/// <summary>Mutable placement of one shaped glyph, in font units.</summary>
internal sealed class GlyphPlacement(ushort glyph, int advance)
{
    public ushort Glyph { get; } = glyph;

    public int Advance { get; } = advance;

    public int XOffset { get; set; }

    public int YOffset { get; set; }

    public int XAdvance { get; set; }

    public int YAdvance { get; set; }
}
