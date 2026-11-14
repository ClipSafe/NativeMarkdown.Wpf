using Markdig.Extensions.Abbreviations;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace NativeMarkdown.Wpf.Rendering;

internal sealed partial class MarkdownBlockRenderer
{
    private void AppendInlines(InlineCollection target, ContainerInline? container, double emSize)
    {
        if (container is null)
            return;

        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
            AppendInline(target, inline, emSize);
    }

    private void AppendInline(InlineCollection target, MdInline inline, double emSize)
    {
        switch (inline)
        {
            // Emoji shortcodes and smileys are already converted to Unicode characters by Markdig.
            case LiteralInline literal:
                AppendText(target, literal.Content.ToString(), emSize);
                break;

            case EmphasisInline emphasis:
                AppendEmphasis(target, emphasis, emSize);
                break;

            case CodeInline code:
                target.Add(new Run(code.Content)
                {
                    FontFamily = _options.MonoFontFamily,
                    FontSize = _options.FontSize * 0.94,
                    Foreground = _options.Foreground,
                    Background = _options.CodeBackground
                });
                break;

            case FootnoteLink footnote:
                target.Add(new Run($"[{footnote.Footnote?.Order}]")
                {
                    BaselineAlignment = BaselineAlignment.Superscript,
                    Foreground = _options.Accent
                });
                break;

            case LinkInline { IsImage: true } image:
                AppendImage(target, image);
                break;

            case LinkInline link:
                AppendLink(target, link.Url, link, emSize);
                break;

            case AutolinkInline autolink:
                AppendLink(target, autolink.Url, null, emSize);
                break;

            case LineBreakInline lineBreak:
                if (lineBreak.IsHard)
                    target.Add(new LineBreak());
                else
                    target.Add(new Run(" "));
                break;

            case MathInline math:
                target.Add(new Run(math.Content.ToString())
                {
                    FontFamily = _options.MonoFontFamily,
                    FontStyle = FontStyles.Italic
                });
                break;

            case HtmlEntityInline entity:
                AppendText(target, entity.Transcoded.ToString(), emSize);
                break;

            case TaskList task:
                target.Add(new Run(task.Checked ? "\u2611" : "\u2610")
                {
                    Foreground = _options.Accent,
                    FontWeight = FontWeights.SemiBold
                });
                target.Add(new Run(" "));
                break;

            case AbbreviationInline abbreviation:
                AppendText(target, abbreviation.Abbreviation?.Label ?? string.Empty, emSize);
                break;

            case ContainerInline container:
                AppendInlines(target, container, emSize);
                break;
        }
    }

    private void AppendEmphasis(InlineCollection target, EmphasisInline emphasis, double emSize)
    {
        var span = new Span();

        switch (emphasis.DelimiterChar)
        {
            case '~' when emphasis.DelimiterCount == 1:
                span.BaselineAlignment = BaselineAlignment.Subscript;
                break;
            case '~':
                span.TextDecorations = TextDecorations.Strikethrough;
                break;
            case '^':
                span.BaselineAlignment = BaselineAlignment.Superscript;
                break;
            case '=':
                span.Background = _options.CodeBackground;
                span.Foreground = _options.Foreground;
                break;
            default:
                if ((emphasis.DelimiterCount & 1) == 1)
                    span.FontStyle = FontStyles.Italic;
                if (emphasis.DelimiterCount >= 2)
                    span.FontWeight = FontWeights.Bold;
                span.Foreground = _options.Foreground;
                break;
        }

        AppendInlines(span.Inlines, emphasis, emSize);
        target.Add(span);
    }

    private void AppendLink(InlineCollection target, string? url, ContainerInline? content, double emSize)
    {
        if (string.IsNullOrWhiteSpace(url) && content is null)
            return;

        var link = url ?? string.Empty;
        var hyperlink = new Hyperlink
        {
            Foreground = _options.Accent,
            Cursor = Cursors.Hand,
            ToolTip = link,
            TextDecorations = null
        };

        if (content is not null)
            AppendInlines(hyperlink.Inlines, content, emSize);
        else
            AppendText(hyperlink.Inlines, link, emSize);

        hyperlink.Click += (_, e) =>
        {
            e.Handled = true;
            _linkHandler(link);
        };

        hyperlink.MouseEnter += (_, _) => hyperlink.TextDecorations = TextDecorations.Underline;
        hyperlink.MouseLeave += (_, _) => hyperlink.TextDecorations = null;

        target.Add(hyperlink);
    }

    private void AppendImage(InlineCollection target, LinkInline image)
    {
        var element = _images.Create(image.Url);

        if (element is null)
        {
            var alt = GetAltText(image);
            if (alt.Length > 0)
                target.Add(new Run(alt) { FontStyle = FontStyles.Italic, Foreground = _options.SecondaryForeground });
            return;
        }

        target.Add(new InlineUIContainer(element) { BaselineAlignment = BaselineAlignment.Center });
    }

    private void AppendText(InlineCollection target, string text, double emSize)
    {
        if (text.Length == 0)
            return;

        if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0)
        {
            var builder = new StringBuilder(text.Length);
            foreach (var character in text)
            {
                if (character == '\r')
                    continue;

                // Soft line breaks join into a single paragraph, matching CommonMark behavior.
                builder.Append(character == '\n' ? ' ' : character);
            }

            text = builder.ToString();
        }

        if (!TryAppendColorEmoji(target, text, emSize))
            target.Add(new Run(text));
    }

    /// <summary>
    /// Replaces emoji grapheme clusters with pre-rendered color art. Returns <c>false</c> when the text
    /// contains no emoji the font can draw as a single colored glyph, so it can stay a plain run.
    /// </summary>
    private bool TryAppendColorEmoji(InlineCollection target, string text, double emSize)
    {
        if (!_options.ColorEmoji)
            return false;

        var hasNonAscii = false;
        foreach (var character in text)
        {
            if (character < 0x80)
                continue;

            hasNonAscii = true;
            break;
        }

        if (!hasNonAscii)
            return false;

        var emoji = _emoji ??= ColorEmojiFont.ForFamily(_options.EmojiFontFamily);
        if (emoji is null)
            return false;

        var start = 0;
        var index = 0;

        while (index < text.Length)
        {
            var length = StringInfo.GetNextTextElementLength(text.AsSpan(index));
            if (length <= 0)
                break;

            if (emoji.Resolve(text.Substring(index, length)) is EmojiArt art)
            {
                if (index > start)
                    target.Add(new Run(text[start..index]));

                var size = art.Scale(emSize);
                target.Add(new InlineUIContainer(new Image
                {
                    Source = art.Image,
                    Width = size.Width,
                    Height = size.Height,
                    Stretch = Stretch.Fill
                })
                {
                    BaselineAlignment = BaselineAlignment.Bottom
                });

                start = index + length;
            }

            index += length;
        }

        if (start == 0)
            return false;

        if (start < text.Length)
            target.Add(new Run(text[start..]));

        return true;
    }

    private static bool TryGetStandaloneImage(ParagraphBlock paragraph, out LinkInline image)
    {
        image = null!;
        LinkInline? found = null;

        for (var inline = paragraph.Inline?.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LinkInline { IsImage: true } link when found is null:
                    found = link;
                    break;
                case LiteralInline literal when string.IsNullOrWhiteSpace(literal.Content.ToString()):
                case LineBreakInline:
                    break;
                default:
                    return false;
            }
        }

        if (found is null)
            return false;

        image = found;
        return true;
    }

    private static string GetAltText(ContainerInline? container)
    {
        if (container is null)
            return string.Empty;

        var builder = new StringBuilder();
        CollectText(container, builder);
        return builder.ToString().Trim();
    }

    private static void CollectText(ContainerInline container, StringBuilder builder)
    {
        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case LineBreakInline:
                    builder.Append(' ');
                    break;
                case ContainerInline nested:
                    CollectText(nested, builder);
                    break;
            }
        }
    }
}
