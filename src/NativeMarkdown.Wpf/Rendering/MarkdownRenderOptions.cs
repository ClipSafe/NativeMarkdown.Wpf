using System.Windows.Media;

namespace NativeMarkdown.Wpf.Rendering;

/// <summary>
/// Immutable snapshot of the visual settings used while building a Markdown document.
/// Comparing two snapshots tells the viewer whether the cached visual tree can be reused.
/// </summary>
internal sealed class MarkdownRenderOptions : IEquatable<MarkdownRenderOptions>
{
    public required Brush Foreground { get; init; }

    public required Brush SecondaryForeground { get; init; }

    public required Brush Accent { get; init; }

    public required Brush CodeBackground { get; init; }

    public required Brush TableHeaderBackground { get; init; }

    public required Brush Border { get; init; }

    public required FontFamily FontFamily { get; init; }

    public required FontFamily MonoFontFamily { get; init; }

    public required double FontSize { get; init; }

    public FontFamily? EmojiFontFamily { get; init; }

    public bool ColorEmoji { get; init; }

    public string BasePath { get; init; } = string.Empty;

    public bool RenderLocalImages { get; init; }

    public bool LoadRemoteImages { get; init; }

    public bool Equals(MarkdownRenderOptions? other)
    {
        if (other is null)
            return false;

        return ReferenceEquals(Foreground, other.Foreground) &&
               ReferenceEquals(SecondaryForeground, other.SecondaryForeground) &&
               ReferenceEquals(Accent, other.Accent) &&
               ReferenceEquals(CodeBackground, other.CodeBackground) &&
               ReferenceEquals(TableHeaderBackground, other.TableHeaderBackground) &&
               ReferenceEquals(Border, other.Border) &&
               Equals(FontFamily, other.FontFamily) &&
               Equals(MonoFontFamily, other.MonoFontFamily) &&
               FontSize.Equals(other.FontSize) &&
               Equals(EmojiFontFamily, other.EmojiFontFamily) &&
               ColorEmoji == other.ColorEmoji &&
               string.Equals(BasePath, other.BasePath, StringComparison.Ordinal) &&
               RenderLocalImages == other.RenderLocalImages &&
               LoadRemoteImages == other.LoadRemoteImages;
    }

    public override bool Equals(object? obj) => Equals(obj as MarkdownRenderOptions);

    public override int GetHashCode() => HashCode.Combine(
        Foreground,
        SecondaryForeground,
        Accent,
        CodeBackground,
        TableHeaderBackground,
        Border,
        FontFamily);
}
