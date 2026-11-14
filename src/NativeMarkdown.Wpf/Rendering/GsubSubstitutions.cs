using System.Text;

namespace NativeMarkdown.Wpf.Rendering;

/// <summary>
/// Glyph substitutions from the emoji font, limited to the lookup types emoji fonts use to compose
/// sequences: ligatures (type 4) and the chained context rules (type 6) that invoke them. Skin tones,
/// keycaps and ZWJ sequences resolve through these rules, which is how the renderer reaches the
/// composed color glyph without a full OpenType shaper.
/// </summary>
internal sealed class GsubSubstitutions
{
    public static readonly GsubSubstitutions Empty = new([], [], []);

    private const int MaxDepth = 8;

    private readonly byte[] _data;

    /// <summary>Every lookup in the font, indexed by LookupList position, because chained context rules
    /// address nested lookups by that index rather than by feature membership.</summary>
    private readonly Lookup?[] _lookups;

    /// <summary>Lookups reached from a feature; only these run at the top level.</summary>
    private readonly int[] _topLevel;

    private GsubSubstitutions(byte[] data, Lookup?[] lookups, int[] topLevel)
    {
        _data = data;
        _lookups = lookups;
        _topLevel = topLevel;
    }

    public static GsubSubstitutions Read(byte[] data, uint gsub)
    {
        var scriptList = gsub + OpenTypeTables.ReadU16(data, gsub + 4);
        var featureList = gsub + OpenTypeTables.ReadU16(data, gsub + 6);
        var lookupList = gsub + OpenTypeTables.ReadU16(data, gsub + 8);

        var lookupCount = OpenTypeTables.ReadU16(data, lookupList);
        var lookups = new Lookup?[lookupCount];

        for (var i = 0; i < lookupCount; i++)
        {
            var lookup = lookupList + OpenTypeTables.ReadU16(data, lookupList + 2u + ((uint)i * 2u));
            var type = OpenTypeTables.ReadU16(data, lookup);
            if (type is not (4 or 6))
                continue;

            var subTableCount = OpenTypeTables.ReadU16(data, lookup + 4);
            var subTables = new List<uint>(subTableCount);
            for (var j = 0; j < subTableCount; j++)
                subTables.Add(lookup + OpenTypeTables.ReadU16(data, lookup + 6u + ((uint)j * 2u)));

            lookups[i] = new Lookup(type, subTables);
        }

        var features = ReadScriptFeatures(data, scriptList);
        var topLevel = ReadTopLevelLookups(data, featureList, features)
            .Where(index => index < lookupCount && lookups[index] is not null)
            .ToArray();

        return new GsubSubstitutions(data, lookups, topLevel);
    }

    /// <summary>Applies the substitution rules to a glyph sequence.</summary>
    public List<ushort> Apply(IReadOnlyList<ushort> glyphs)
    {
        var current = new List<ushort>(glyphs);
        if (_topLevel.Length == 0)
            return current;

        // Rules compose in stages (one ligature can feed the next), so repeat until stable.
        for (var pass = 0; pass < 4; pass++)
        {
            var changed = false;

            foreach (var index in _topLevel)
            {
                if (_lookups[index] is not Lookup lookup)
                    continue;

                for (var position = 0; position < current.Count; position++)
                    changed |= ApplyAt(lookup, current, position, depth: 0);
            }

            if (!changed)
                break;
        }

        return current;
    }

    private bool ApplyAt(Lookup lookup, List<ushort> glyphs, int index, int depth)
    {
        if (depth > MaxDepth || index >= glyphs.Count)
            return false;

        foreach (var subTable in lookup.SubTables)
        {
            var changed = lookup.Type switch
            {
                4 => ApplyLigature(subTable, glyphs, index),
                6 => ApplyChainContext(subTable, glyphs, index, depth),
                _ => false
            };

            if (changed)
                return true;
        }

        return false;
    }

    private bool ApplyLigature(uint subTable, List<ushort> glyphs, int index)
    {
        if (OpenTypeTables.ReadU16(_data, subTable) != 1)
            return false;

        var coverage = OpenTypeCoverage.Read(_data, subTable + OpenTypeTables.ReadU16(_data, subTable + 2));
        var covered = coverage.IndexOf(glyphs[index]);
        if (covered < 0)
            return false;

        var setCount = OpenTypeTables.ReadU16(_data, subTable + 4);
        if (covered >= setCount)
            return false;

        var set = subTable + OpenTypeTables.ReadU16(_data, subTable + 6u + ((uint)covered * 2u));
        var ligatureCount = OpenTypeTables.ReadU16(_data, set);

        // Prefer the longest match so a three-glyph rule wins over a two-glyph rule.
        var bestLength = 0;
        var bestGlyph = (ushort)0;

        for (var i = 0; i < ligatureCount; i++)
        {
            var ligature = set + OpenTypeTables.ReadU16(_data, set + 2u + ((uint)i * 2u));
            var componentCount = OpenTypeTables.ReadU16(_data, ligature + 2);

            if (componentCount < 2 || componentCount <= bestLength || index + componentCount > glyphs.Count)
                continue;

            var matches = true;
            for (var k = 1; k < componentCount; k++)
            {
                if (OpenTypeTables.ReadU16(_data, ligature + 4u + ((uint)(k - 1) * 2u)) == glyphs[index + k])
                    continue;

                matches = false;
                break;
            }

            if (!matches)
                continue;

            bestLength = componentCount;
            bestGlyph = OpenTypeTables.ReadU16(_data, ligature);
        }

        if (bestLength == 0)
            return false;

        glyphs.RemoveRange(index + 1, bestLength - 1);
        glyphs[index] = bestGlyph;
        return true;
    }

    private bool ApplyChainContext(uint subTable, List<ushort> glyphs, int index, int depth)
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
            if (!inputs[i].Contains(glyphs[index + i]))
                return false;
        }

        // Backtrack coverage is stored nearest-first.
        for (var i = 0; i < backtrackCount; i++)
        {
            if (!backtracks[i].Contains(glyphs[index - 1 - i]))
                return false;
        }

        for (var i = 0; i < lookaheadCount; i++)
        {
            if (!lookaheads[i].Contains(glyphs[index + inputCount + i]))
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

    private static HashSet<int> ReadScriptFeatures(byte[] data, uint scriptList)
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

        return features;
    }

    private static void AddLangSysFeatures(byte[] data, uint langSys, HashSet<int> features)
    {
        var count = OpenTypeTables.ReadU16(data, langSys + 4);
        for (var i = 0; i < count; i++)
            features.Add(OpenTypeTables.ReadU16(data, langSys + 6u + ((uint)i * 2u)));
    }

    private static List<int> ReadTopLevelLookups(byte[] data, uint featureList, HashSet<int> features)
    {
        var lookups = new List<int>();
        var featureCount = OpenTypeTables.ReadU16(data, featureList);
        var wanted = new HashSet<string>(StringComparer.Ordinal) { "ccmp", "liga", "rlig", "clig" };

        for (var i = 0; i < featureCount; i++)
        {
            if (!features.Contains(i))
                continue;

            var record = featureList + 2u + ((uint)i * 6u);
            var tag = Encoding.ASCII.GetString(data, (int)record, 4);
            if (!wanted.Contains(tag))
                continue;

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

    private readonly record struct Lookup(ushort Type, List<uint> SubTables);
}
