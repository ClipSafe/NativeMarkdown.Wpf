using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace NativeMarkdown.Wpf.Rendering;

/// <summary>
/// Resolves emoji to layered vector art using the color tables (COLR/CPAL) of the system emoji font.
/// WPF's text stack can only draw glyph outlines, so emoji are rendered as a <see cref="DrawingImage"/>
/// instead of text when the font can compose the cluster into a single emoji. Multi-person families and
/// flags stay as text because this font exposes no reachable composition for them.
/// </summary>
internal sealed class ColorEmojiFont
{
    /// <summary>Canonical em size the outlines are built at; callers scale the frozen art instead.</summary>
    private const double ReferenceEmSize = 100;

    /// <summary>A composed emoji must stay close to a single em, otherwise the font did not compose it.</summary>
    private const double MaxComposedWidthEm = 1.6;

    private static readonly ConcurrentDictionary<string, ColorEmojiFont?> Fonts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<(string Cluster, string Font), EmojiArt?> Art = new();

    private readonly GlyphTypeface _glyphTypeface;
    private readonly GsubSubstitutions _substitutions;
    private readonly GposPositioning _positioning;
    private readonly Dictionary<int, (ushort First, ushort Count)> _baseGlyphs;
    private readonly (ushort Glyph, ushort PaletteIndex)[] _layers;
    private readonly uint[] _palette;
    private readonly int _unitsPerEm;

    private ColorEmojiFont(GlyphTypeface glyphTypeface, byte[] fontData)
    {
        _glyphTypeface = glyphTypeface;

        var tables = OpenTypeTables.ReadDirectory(fontData);
        _baseGlyphs = [];
        _layers = [];
        _palette = [];
        _unitsPerEm = tables.TryGetValue("head", out var head) ? OpenTypeTables.ReadU16(fontData, head + 18) : 0;

        if (tables.TryGetValue("CPAL", out var cpal))
        {
            var cpalVersion = OpenTypeTables.ReadU16(fontData, cpal);
            if (cpalVersion == 0)
                _palette = ReadPalette(fontData, cpal);
        }

        if (tables.TryGetValue("COLR", out var colr))
            ReadColorLayers(fontData, colr, out _baseGlyphs, out _layers);

        _substitutions = tables.TryGetValue("GSUB", out var gsub)
            ? GsubSubstitutions.Read(fontData, gsub)
            : GsubSubstitutions.Empty;

        _positioning = tables.TryGetValue("GPOS", out var gpos)
            ? GposPositioning.Read(fontData, gpos)
            : GposPositioning.Empty;
    }

    /// <summary>Returns the shared resolver for an emoji font family, or <c>null</c> when it is unavailable.</summary>
    public static ColorEmojiFont? ForFamily(FontFamily? family)
    {
        var name = family?.Source ?? string.Empty;
        return Fonts.GetOrAdd(name, static key => Load(new FontFamily(key)));
    }

    /// <summary>
    /// Returns frozen vector art for an emoji grapheme cluster, or <c>null</c> when the cluster is not a
    /// single complete colored glyph. The result is cached per cluster, so repeated emoji cost nothing.
    /// </summary>
    public EmojiArt? Resolve(string cluster)
    {
        if (cluster.Length == 0 || _baseGlyphs.Count == 0)
            return null;

        return Art.GetOrAdd((cluster, _glyphTypeface.FamilyNames.Values.FirstOrDefault() ?? string.Empty), _ => Build(cluster));
    }

    private static ColorEmojiFont? Load(FontFamily family)
    {
        try
        {
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            if (!typeface.TryGetGlyphTypeface(out var glyphTypeface))
                return null;

            using var stream = glyphTypeface.GetFontStream();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            var font = new ColorEmojiFont(glyphTypeface, buffer.ToArray());
            return font._baseGlyphs.Count == 0 ? null : font;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException)
        {
            // A missing or unusual emoji font is not fatal: the caller falls back to plain text.
            return null;
        }
    }

    private EmojiArt? Build(string cluster)
    {
        var glyphs = ResolveGlyphs(cluster);
        if (glyphs.Count == 0)
            return null;

        return glyphs.Count == 1 ? BuildSingle(glyphs[0]) : BuildSequence(glyphs);
    }

    private EmojiArt? BuildSingle(ushort glyph)
    {
        if (!_baseGlyphs.TryGetValue(glyph, out var range) || range.Count == 0)
            return null;

        var group = new DrawingGroup();
        using (var context = group.Open())
        {
            DrawLayers(context, range, 0, 0);
        }

        return Finish(group);
    }

    /// <summary>
    /// Lays out a sequence the font draws from several glyphs, such as people holding hands, using the
    /// font's own positioning adjustments. A sequence the font does not pull together stays as text.
    /// </summary>
    private EmojiArt? BuildSequence(List<ushort> glyphs)
    {
        if (_positioning.IsEmpty || _unitsPerEm <= 0)
            return null;

        var placements = new List<GlyphPlacement>(glyphs.Count);
        foreach (var glyph in glyphs)
        {
            var advance = _glyphTypeface.AdvanceWidths.TryGetValue(glyph, out var width) ? width : 0;
            placements.Add(new GlyphPlacement(glyph, (int)Math.Round(advance * _unitsPerEm)));
        }

        _positioning.Apply(placements);

        var scale = ReferenceEmSize / _unitsPerEm;
        var group = new DrawingGroup();
        var pen = 0;

        using (var context = group.Open())
        {
            foreach (var placement in placements)
            {
                if (_baseGlyphs.TryGetValue(placement.Glyph, out var range) && range.Count > 0)
                    DrawLayers(context, range, (pen + placement.XOffset) * scale, -placement.YOffset * scale);

                pen += placement.Advance + placement.XAdvance;
            }
        }

        var art = Finish(group);

        // Only accept the result when the font actually pulled the glyphs into a single emoji.
        return art is EmojiArt value && value.WidthEm <= MaxComposedWidthEm ? art : null;
    }

    private void DrawLayers(DrawingContext context, (ushort First, ushort Count) range, double x, double y)
    {
        var translated = x != 0 || y != 0;
        if (translated)
            context.PushTransform(new TranslateTransform(x, y));

        for (var i = 0; i < range.Count; i++)
        {
            var layer = _layers[range.First + i];
            var geometry = _glyphTypeface.GetGlyphOutline(layer.Glyph, ReferenceEmSize, ReferenceEmSize);
            if (geometry.Bounds.IsEmpty)
                continue;

            context.DrawGeometry(new SolidColorBrush(GetColor(layer.PaletteIndex)), null, geometry);
        }

        if (translated)
            context.Pop();
    }

    private EmojiArt? Finish(DrawingGroup group)
    {
        if (group.Bounds.IsEmpty)
            return null;

        group.Freeze();

        var bounds = group.Bounds;
        var normalized = new DrawingGroup
        {
            Transform = new TranslateTransform(-bounds.X, -bounds.Y),
            Children = { group }
        };
        normalized.Freeze();

        var image = new DrawingImage(normalized);
        image.Freeze();

        return new EmojiArt(image, bounds.Width / ReferenceEmSize, bounds.Height / ReferenceEmSize);
    }

    private List<ushort> ResolveGlyphs(string cluster)
    {
        var map = _glyphTypeface.CharacterToGlyphMap;
        var glyphs = new List<ushort>(4);

        foreach (var rune in cluster.EnumerateRunes())
        {
            // Variation selectors carry presentation only; they are not part of ligature components.
            if (rune.Value is 0xFE0E or 0xFE0F)
                continue;

            if (!map.TryGetValue(rune.Value, out var glyph))
                return [];

            glyphs.Add(glyph);
        }

        if (glyphs.Count == 0)
            return [];

        return _substitutions.Apply(glyphs);
    }

    private Color GetColor(ushort paletteIndex)
    {
        if (paletteIndex == 0xFFFF || paletteIndex >= _palette.Length)
            return Colors.Black;

        var bgra = _palette[paletteIndex];
        return Color.FromArgb(
            (byte)(bgra & 0xFF),
            (byte)((bgra >> 8) & 0xFF),
            (byte)((bgra >> 16) & 0xFF),
            (byte)((bgra >> 24) & 0xFF));
    }

    private static uint[] ReadPalette(byte[] data, uint cpal)
    {
        var count = OpenTypeTables.ReadU16(data, cpal + 2);
        var recordsOffset = OpenTypeTables.ReadU32(data, cpal + 8);
        var firstIndex = OpenTypeTables.ReadU16(data, cpal + 12);
        var palette = new uint[count];
        var start = cpal + recordsOffset;

        for (var i = 0; i < count; i++)
        {
            var record = start + ((uint)(firstIndex + i) * 4);
            if (record + 4 > data.Length)
                break;

            palette[i] = OpenTypeTables.ReadU32(data, record);
        }

        return palette;
    }

    private static void ReadColorLayers(
        byte[] data,
        uint colr,
        out Dictionary<int, (ushort First, ushort Count)> baseGlyphs,
        out (ushort Glyph, ushort PaletteIndex)[] layers)
    {
        var baseGlyphCount = OpenTypeTables.ReadU16(data, colr + 2);
        var baseGlyphOffset = OpenTypeTables.ReadU32(data, colr + 4);
        var layerOffset = OpenTypeTables.ReadU32(data, colr + 8);
        var layerCount = OpenTypeTables.ReadU16(data, colr + 12);

        layers = new (ushort, ushort)[layerCount];
        for (var i = 0; i < layerCount; i++)
        {
            var record = colr + layerOffset + ((uint)i * 4);
            if (record + 4 > data.Length)
                break;

            layers[i] = (
                OpenTypeTables.ReadU16(data, record),
                OpenTypeTables.ReadU16(data, record + 2));
        }

        baseGlyphs = new Dictionary<int, (ushort, ushort)>(baseGlyphCount);
        for (var i = 0; i < baseGlyphCount; i++)
        {
            var record = colr + baseGlyphOffset + ((uint)i * 6);
            if (record + 6 > data.Length)
                break;

            baseGlyphs[OpenTypeTables.ReadU16(data, record)] = (
                OpenTypeTables.ReadU16(data, record + 2),
                OpenTypeTables.ReadU16(data, record + 4));
        }
    }
}

/// <summary>Frozen vector art for one emoji, sized in em units.</summary>
internal readonly record struct EmojiArt(ImageSource Image, double WidthEm, double HeightEm)
{
    public Size Scale(double emSize) => new(WidthEm * emSize, HeightEm * emSize);
}
