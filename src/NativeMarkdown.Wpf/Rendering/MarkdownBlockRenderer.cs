 using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DocList = System.Windows.Documents.List;
using MdBlock = Markdig.Syntax.Block;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using Table = System.Windows.Documents.Table;
using TableCell = System.Windows.Documents.TableCell;
using TableRow = System.Windows.Documents.TableRow;
using WpfBlock = System.Windows.Documents.Block;

namespace NativeMarkdown.Wpf.Rendering;

/// <summary>
/// Builds WPF document blocks from the Markdig syntax tree. Every top-level block is created on its own,
/// which is what allows the viewer to replace just the edited block instead of the whole document.
/// </summary>
internal sealed partial class MarkdownBlockRenderer
{
    private static readonly double[] HeadingScales = [2.0, 1.55, 1.28, 1.13, 1.0, 0.93];

    private readonly MarkdownRenderOptions _options;
    private readonly MarkdownImageLoader _images;
    private readonly Action<string> _linkHandler;
    private ColorEmojiFont? _emoji;

    public MarkdownBlockRenderer(MarkdownRenderOptions options, Action<string> linkHandler)
    {
        _options = options;
        _images = new MarkdownImageLoader(options);
        _linkHandler = linkHandler;
    }

    public WpfBlock Create(MdBlock block) => block switch
    {
        HeadingBlock heading => CreateHeading(heading),
        ParagraphBlock paragraph => CreateParagraph(paragraph),
        CodeBlock code => CreateCode(code.Lines.ToString()),
        ThematicBreakBlock => CreateThematicBreak(),
        QuoteBlock quote => CreateContainer(quote, CreateQuoteSection()),
        ListBlock list => CreateList(list),
        MdTable table => CreateTable(table),
        DefinitionTerm term => CreateDefinitionTerm(term),
        ContainerBlock container => CreateContainer(container, new Section()),
        LeafBlock leaf => CreateLiteral(leaf.Lines.ToString()),
        _ => new Section()
    };

    private static double ParagraphLineHeight(MarkdownRenderOptions options) => options.FontSize * 1.65;

    private WpfBlock CreateHeading(HeadingBlock heading)
    {
        var level = Math.Clamp(heading.Level, 1, 6);
        var size = _options.FontSize * HeadingScales[level - 1];

        var paragraph = new Paragraph
        {
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = _options.Foreground,
            Margin = new Thickness(0, level == 1 ? 4 : 22, 0, 8),
            LineHeight = size * 1.3,
            LineStackingStrategy = LineStackingStrategy.MaxHeight,
            KeepWithNext = true
        };

        if (level == 1)
        {
            paragraph.Padding = new Thickness(0, 0, 0, 10);
            paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
            paragraph.BorderBrush = _options.Border;
        }

        AppendInlines(paragraph.Inlines, heading.Inline, size);
        return paragraph;
    }

    private WpfBlock CreateParagraph(ParagraphBlock paragraph)
    {
        if (TryGetStandaloneImage(paragraph, out var image))
            return CreateImageBlock(image);

        var result = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 12),
            LineHeight = ParagraphLineHeight(_options),
            LineStackingStrategy = LineStackingStrategy.MaxHeight
        };

        AppendInlines(result.Inlines, paragraph.Inline, _options.FontSize);
        return result;
    }

    private WpfBlock CreateCode(string text)
    {
        var paragraph = new Paragraph
        {
            FontFamily = _options.MonoFontFamily,
            FontSize = _options.FontSize * 0.94,
            Foreground = _options.Foreground,
            Background = _options.CodeBackground,
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 2, 0, 14),
            LineHeight = _options.FontSize * 1.6,
            LineStackingStrategy = LineStackingStrategy.MaxHeight
        };

        paragraph.Inlines.Add(new Run(text.TrimEnd('\r', '\n')));
        return paragraph;
    }

    private WpfBlock CreateThematicBreak()
    {
        var line = new Border
        {
            Height = 1,
            Background = _options.Border,
            SnapsToDevicePixels = true
        };

        return new BlockUIContainer(line) { Margin = new Thickness(0, 14, 0, 14) };
    }

    private Section CreateQuoteSection() => new()
    {
        BorderThickness = new Thickness(3, 0, 0, 0),
        BorderBrush = _options.Accent,
        Padding = new Thickness(16, 2, 0, 2),
        Margin = new Thickness(0, 0, 0, 12)
    };

    private WpfBlock CreateContainer(ContainerBlock container, Section section)
    {
        foreach (var child in container)
            section.Blocks.Add(Create(child));

        return section;
    }

    private WpfBlock CreateList(ListBlock list)
    {
        var documentList = new DocList
        {
            MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(22, 0, 0, 0),
            MarkerOffset = 8
        };

        if (list.IsOrdered &&
            int.TryParse(list.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) &&
            start > 1)
        {
            documentList.StartIndex = start;
        }

        foreach (var child in list)
        {
            if (child is not ListItemBlock item)
                continue;

            var listItem = new ListItem();

            foreach (var itemBlock in item)
            {
                var block = Create(itemBlock);
                switch (block)
                {
                    case Paragraph paragraph:
                        paragraph.Margin = new Thickness(0, 0, 0, 6);
                        break;
                    case DocList nested:
                        nested.Margin = new Thickness(0, 0, 0, 6);
                        break;
                }

                listItem.Blocks.Add(block);
            }

            if (listItem.Blocks.Count == 0)
                listItem.Blocks.Add(new Paragraph());

            documentList.ListItems.Add(listItem);
        }

        return documentList;
    }

    private WpfBlock CreateTable(MdTable table)
    {
        var wpfTable = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 2, 0, 16),
            BorderBrush = _options.Border,
            BorderThickness = new Thickness(1)
        };

        var columnCount = table.ColumnDefinitions?.Count ?? 0;
        if (columnCount == 0)
        {
            foreach (var row in table.OfType<MdTableRow>())
                foreach (var cell in row.OfType<MdTableCell>())
                    columnCount = Math.Max(columnCount, cell.ColumnIndex + Math.Max(1, cell.ColumnSpan));
        }

        for (var i = 0; i < columnCount; i++)
            wpfTable.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        var group = new TableRowGroup();
        wpfTable.RowGroups.Add(group);

        foreach (var row in table.OfType<MdTableRow>())
        {
            var wpfRow = new TableRow();

            if (row.IsHeader)
            {
                wpfRow.FontWeight = FontWeights.SemiBold;
                wpfRow.Foreground = _options.Foreground;
                wpfRow.Background = _options.TableHeaderBackground;
            }

            foreach (var cell in row.OfType<MdTableCell>())
            {
                var alignment = GetCellAlignment(table, cell);
                var wpfCell = new TableCell
                {
                    BorderBrush = _options.Border,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(10, 8, 10, 8),
                    ColumnSpan = Math.Max(1, cell.ColumnSpan),
                    RowSpan = Math.Max(1, cell.RowSpan)
                };

                if (row.IsHeader)
                    wpfCell.Background = _options.TableHeaderBackground;

                foreach (var child in cell)
                {
                    var block = Create(child);
                    if (block is Paragraph paragraph)
                    {
                        paragraph.Margin = new Thickness(0);
                        paragraph.TextAlignment = alignment;
                    }

                    wpfCell.Blocks.Add(block);
                }

                if (wpfCell.Blocks.Count == 0)
                    wpfCell.Blocks.Add(new Paragraph());

                wpfRow.Cells.Add(wpfCell);
            }

            group.Rows.Add(wpfRow);
        }

        return wpfTable;
    }

    private static TextAlignment GetCellAlignment(MdTable table, MdTableCell cell)
    {
        if (table.ColumnDefinitions is null ||
            cell.ColumnIndex < 0 ||
            cell.ColumnIndex >= table.ColumnDefinitions.Count)
        {
            return TextAlignment.Left;
        }

        return table.ColumnDefinitions[cell.ColumnIndex].Alignment switch
        {
            TableColumnAlign.Center => TextAlignment.Center,
            TableColumnAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left
        };
    }

    private WpfBlock CreateDefinitionTerm(DefinitionTerm term)
    {
        var paragraph = new Paragraph
        {
            FontWeight = FontWeights.SemiBold,
            Foreground = _options.Foreground,
            Margin = new Thickness(0, 4, 0, 2)
        };

        AppendInlines(paragraph.Inlines, term.Inline, _options.FontSize);
        return paragraph;
    }

    private WpfBlock CreateLiteral(string text)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 12),
            LineHeight = ParagraphLineHeight(_options),
            LineStackingStrategy = LineStackingStrategy.MaxHeight
        };

        var value = text.TrimEnd('\r', '\n');
        if (value.Length > 0)
            paragraph.Inlines.Add(new Run(value));

        return paragraph;
    }

    private WpfBlock CreateImageBlock(LinkInline image)
    {
        var alt = GetAltText(image);
        var element = _images.Create(image.Url);

        if (element is null)
            return CreateImageFallback(alt);

        element.HorizontalAlignment = HorizontalAlignment.Left;
        return new BlockUIContainer(element) { Margin = new Thickness(0, 4, 0, 16) };
    }

    private WpfBlock CreateImageFallback(string alt)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };

        if (alt.Length > 0)
        {
            paragraph.Inlines.Add(new Run(alt)
            {
                FontStyle = FontStyles.Italic,
                Foreground = _options.SecondaryForeground
            });
        }

        return paragraph;
    }
}
