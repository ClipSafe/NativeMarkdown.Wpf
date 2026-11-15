using NativeMarkdown.Wpf.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace NativeMarkdown.Wpf;

/// <summary>
/// Draws the color emoji inside a plain <see cref="TextBlock"/>, so sidebars, titles and other read-only
/// text can show emoji the same way the Markdown preview does.
/// Text without emoji is assigned to <see cref="TextBlock.Text"/>, which keeps trimming behaviour native.
/// </summary>
public static class EmojiText
{
    private static readonly FontFamily DefaultEmojiFontFamily = new("Segoe UI Emoji");

    /// <summary>Identifies the attached text property.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source",
        typeof(string),
        typeof(EmojiText),
        new PropertyMetadata(string.Empty, OnSourceChanged));

    /// <summary>Identifies the attached emoji font family property.</summary>
    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.RegisterAttached(
        "FontFamily",
        typeof(FontFamily),
        typeof(EmojiText),
        new PropertyMetadata(null, OnSourceChanged));

    /// <summary>Identifies the attached toggle that disables color emoji.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(EmojiText),
        new PropertyMetadata(true, OnSourceChanged));

    /// <summary>Sets the text to display, replacing emoji with color art.</summary>
    public static void SetSource(DependencyObject element, string value) =>
        element.SetValue(SourceProperty, value);

    /// <summary>Gets the text displayed by the attached property.</summary>
    public static string GetSource(DependencyObject element) =>
        element.GetValue(SourceProperty) as string ?? string.Empty;

    /// <summary>Sets the font whose color tables render emoji.</summary>
    public static void SetFontFamily(DependencyObject element, FontFamily? value) =>
        element.SetValue(FontFamilyProperty, value);

    /// <summary>Gets the font whose color tables render emoji.</summary>
    public static FontFamily? GetFontFamily(DependencyObject element) =>
        element.GetValue(FontFamilyProperty) as FontFamily;

    /// <summary>Sets whether emoji are rendered as color art.</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    /// <summary>Gets whether emoji are rendered as color art.</summary>
    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
            Apply(textBlock, GetSource(textBlock));
    }

    /// <summary>Builds the text block content for <paramref name="text"/>.</summary>
    internal static void Apply(TextBlock textBlock, string text)
    {
        var emoji = GetIsEnabled(textBlock)
            ? ColorEmojiFont.ForFamily(GetFontFamily(textBlock) ?? DefaultEmojiFontFamily)
            : null;

        var segments = ColorEmojiFont.Segment(text, emoji);
        var hasEmoji = false;
        foreach (var segment in segments)
        {
            if (segment.Art is null)
                continue;

            hasEmoji = true;
            break;
        }

        if (!hasEmoji)
        {
            // Native text keeps CharacterEllipsis trimming exact, so only take over when it is needed.
            textBlock.Inlines.Clear();
            textBlock.Text = text;
            return;
        }

        // Clearing Text before filling Inlines avoids the two fighting over the same content.
        textBlock.Text = string.Empty;
        textBlock.Inlines.Clear();

        var runningSize = textBlock.FontSize > 0 ? textBlock.FontSize : 12;

        foreach (var segment in segments)
        {
            var value = text.Substring(segment.Start, segment.Length);

            if (segment.Art is not EmojiArt art)
            {
                textBlock.Inlines.Add(new Run(value));
                continue;
            }

            var size = art.Scale(runningSize);
            textBlock.Inlines.Add(new InlineUIContainer(new Image
            {
                Source = art.Image,
                Width = size.Width,
                Height = size.Height,
                Stretch = Stretch.Fill
            })
            {
                BaselineAlignment = BaselineAlignment.Bottom
            });

            // Keep the character in the text so selection and copy still produce the original string.
            textBlock.Inlines.Add(new Run(value)
            {
                FontSize = 0.05,
                Foreground = Brushes.Transparent
            });
        }
    }
}
