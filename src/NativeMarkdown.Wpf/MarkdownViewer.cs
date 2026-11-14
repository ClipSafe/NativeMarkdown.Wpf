using Markdig.Syntax;
using NativeMarkdown.Wpf.Rendering;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfBlock = System.Windows.Documents.Block;

namespace NativeMarkdown.Wpf;

/// <summary>
/// A native WPF Markdown viewer. The document is rendered into a <see cref="FlowDocument"/>, so it supports
/// text selection and printing without a web browser. Rendering is incremental: unchanged top-level blocks
/// keep their visual tree, which makes live previews update instantly without flicker.
/// </summary>
public sealed class MarkdownViewer : FrameworkElement
{
    private const double WheelScrollPageFraction = 0.15;
    private const double MouseWheelDeltaPerNotch = 120d;

    private static readonly FontFamily DefaultFontFamily =
        new("Segoe UI Variable Text, Segoe UI, Segoe UI Emoji, Segoe UI Symbol");

    private static readonly FontFamily DefaultMonoFontFamily = new("Cascadia Mono, Consolas");

    private static readonly FontFamily DefaultEmojiFontFamily = new("Segoe UI Emoji");

    private static readonly Brush DefaultForeground = CreateBrush(0xFF, 0x16, 0x16, 0x1D);
    private static readonly Brush DefaultSecondaryForeground = CreateBrush(0xFF, 0x5A, 0x5A, 0x66);
    private static readonly Brush DefaultAccent = CreateBrush(0xFF, 0x6D, 0x28, 0xD9);
    private static readonly Brush DefaultCodeBackground = CreateBrush(0xFF, 0xF1, 0xF1, 0xF5);
    private static readonly Brush DefaultTableHeaderBackground = CreateBrush(0xFF, 0xF7, 0xF7, 0xF9);
    private static readonly Brush DefaultBorder = CreateBrush(0x14, 0x00, 0x00, 0x00);
    private static readonly Brush DefaultBackground = CreateBrush(0xFF, 0xF7, 0xF7, 0xF9);

    /// <summary>Identifies the <see cref="Markdown"/> dependency property.</summary>
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(string.Empty, OnMarkdownChanged));

    /// <summary>Identifies the <see cref="BasePath"/> dependency property.</summary>
    public static readonly DependencyProperty BasePathProperty = DependencyProperty.Register(
        nameof(BasePath),
        typeof(string),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(string.Empty, OnRenderSettingsChanged));

    /// <summary>Identifies the <see cref="RenderLocalImages"/> dependency property.</summary>
    public static readonly DependencyProperty RenderLocalImagesProperty = DependencyProperty.Register(
        nameof(RenderLocalImages),
        typeof(bool),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(true, OnRenderSettingsChanged));

    /// <summary>Identifies the <see cref="LoadRemoteImages"/> dependency property.</summary>
    public static readonly DependencyProperty LoadRemoteImagesProperty = DependencyProperty.Register(
        nameof(LoadRemoteImages),
        typeof(bool),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(true, OnRenderSettingsChanged));

    /// <summary>Identifies the <see cref="SelectionEnabled"/> dependency property.</summary>
    public static readonly DependencyProperty SelectionEnabledProperty = DependencyProperty.Register(
        nameof(SelectionEnabled),
        typeof(bool),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(true, OnSelectionEnabledChanged));

    /// <summary>Identifies the <see cref="ContentMaxWidth"/> dependency property.</summary>
    public static readonly DependencyProperty ContentMaxWidthProperty = DependencyProperty.Register(
        nameof(ContentMaxWidth),
        typeof(double),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(820d, OnLayoutSettingsChanged));

    /// <summary>Identifies the <see cref="ContentPadding"/> dependency property.</summary>
    public static readonly DependencyProperty ContentPaddingProperty = DependencyProperty.Register(
        nameof(ContentPadding),
        typeof(Thickness),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(new Thickness(36, 32, 36, 96), OnLayoutSettingsChanged));

    /// <summary>Identifies the <see cref="Background"/> dependency property.</summary>
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background),
        typeof(Brush),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(DefaultBackground, OnBackgroundChanged));

    /// <summary>Identifies the <see cref="Foreground"/> dependency property.</summary>
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground),
        typeof(Brush),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(DefaultForeground, OnStyleChanged));

    /// <summary>Identifies the <see cref="SecondaryForeground"/> dependency property.</summary>
    public static readonly DependencyProperty SecondaryForegroundProperty = DependencyProperty.Register(
        nameof(SecondaryForeground),
        typeof(Brush),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(DefaultSecondaryForeground, OnStyleChanged));

    /// <summary>Identifies the <see cref="AccentBrush"/> dependency property.</summary>
    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush),
        typeof(Brush),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(DefaultAccent, OnStyleChanged));

    /// <summary>Identifies the <see cref="CodeBackground"/> dependency property.</summary>
    public static readonly DependencyProperty CodeBackgroundProperty = DependencyProperty.Register(
        nameof(CodeBackground),
        typeof(Brush),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(DefaultCodeBackground, OnStyleChanged));

    /// <summary>Identifies the <see cref="TableHeaderBackground"/> dependency property.</summary>
    public static readonly DependencyProperty TableHeaderBackgroundProperty = DependencyProperty.Register(
        nameof(TableHeaderBackground),
        typeof(Brush),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(DefaultTableHeaderBackground, OnStyleChanged));

    /// <summary>Identifies the <see cref="BorderBrush"/> dependency property.</summary>
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush),
        typeof(Brush),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(DefaultBorder, OnStyleChanged));

    /// <summary>Identifies the <see cref="FontFamily"/> dependency property.</summary>
    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
        nameof(FontFamily),
        typeof(FontFamily),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(DefaultFontFamily, OnStyleChanged));

    /// <summary>Identifies the <see cref="MonoFontFamily"/> dependency property.</summary>
    public static readonly DependencyProperty MonoFontFamilyProperty = DependencyProperty.Register(
        nameof(MonoFontFamily),
        typeof(FontFamily),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(DefaultMonoFontFamily, OnStyleChanged));

    /// <summary>Identifies the <see cref="FontSize"/> dependency property.</summary>
    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize),
        typeof(double),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(14d, OnStyleChanged));

    /// <summary>Identifies the <see cref="EmojiFontFamily"/> dependency property.</summary>
    public static readonly DependencyProperty EmojiFontFamilyProperty = DependencyProperty.Register(
        nameof(EmojiFontFamily),
        typeof(FontFamily),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(DefaultEmojiFontFamily, OnStyleChanged));

    /// <summary>Identifies the <see cref="ColorEmoji"/> dependency property.</summary>
    public static readonly DependencyProperty ColorEmojiProperty = DependencyProperty.Register(
        nameof(ColorEmoji),
        typeof(bool),
        typeof(MarkdownViewer),
        new FrameworkPropertyMetadata(true, OnStyleChanged));

    private readonly MarkdownDocumentRenderer _parser = new();
    private readonly FlowDocument _document;
    private readonly FlowDocumentScrollViewer _host;
    private readonly List<BlockEntry> _blocks = [];

    private MarkdownRenderOptions? _options;
    private MarkdownDocument? _parsedDocument;
    private int[] _signatures = [];
    private string _renderedMarkdown = string.Empty;
    private ScrollViewer? _scrollViewer;
    private bool _isRendering;

    /// <summary>
    /// Initializes a new instance of the <see cref="MarkdownViewer"/> class.
    /// </summary>
    public MarkdownViewer()
    {
        _document = new FlowDocument
        {
            IsOptimalParagraphEnabled = false,
            IsHyphenationEnabled = false
        };

        _host = new FlowDocumentScrollViewer
        {
            Document = _document,
            IsToolBarVisible = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsSelectionEnabled = true,
            Focusable = true,
            Background = Background
        };

        _host.ContextMenu = CreateContextMenu();
        _host.PreviewMouseWheel += OnPreviewMouseWheel;
        AddLogicalChild(_host);
        AddVisualChild(_host);

        ApplyDocumentStyle();
        UpdatePagePadding();

        Loaded += OnLoaded;
        SizeChanged += (_, _) => UpdatePagePadding();
    }

    /// <summary>
    /// Raised when a link in the document is activated.
    /// </summary>
    public event EventHandler<MarkdownLinkClickedEventArgs>? LinkClicked;

    /// <summary>
    /// Gets or sets the Markdown source to render.
    /// </summary>
    public string Markdown
    {
        get => (string?)GetValue(MarkdownProperty) ?? string.Empty;
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>
    /// Gets or sets the directory used to resolve relative image paths.
    /// </summary>
    public string BasePath
    {
        get => (string?)GetValue(BasePathProperty) ?? string.Empty;
        set => SetValue(BasePathProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether images on the local file system are rendered.
    /// </summary>
    public bool RenderLocalImages
    {
        get => (bool)GetValue(RenderLocalImagesProperty);
        set => SetValue(RenderLocalImagesProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether http and https images are downloaded and rendered.
    /// </summary>
    public bool LoadRemoteImages
    {
        get => (bool)GetValue(LoadRemoteImagesProperty);
        set => SetValue(LoadRemoteImagesProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether text can be selected and copied.
    /// </summary>
    public bool SelectionEnabled
    {
        get => (bool)GetValue(SelectionEnabledProperty);
        set => SetValue(SelectionEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum width of the rendered content. Wider content is centered in the available space.
    /// Set to zero to use the full width.
    /// </summary>
    public double ContentMaxWidth
    {
        get => (double)GetValue(ContentMaxWidthProperty);
        set => SetValue(ContentMaxWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the padding around the rendered content. Horizontal padding is applied as a minimum
    /// and grows automatically when <see cref="ContentMaxWidth"/> leaves free space.
    /// </summary>
    public Thickness ContentPadding
    {
        get => (Thickness)GetValue(ContentPaddingProperty);
        set => SetValue(ContentPaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets the document background.
    /// </summary>
    public Brush Background
    {
        get => (Brush)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the color of headings, emphasis, code and table headers.
    /// </summary>
    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the color of body text.
    /// </summary>
    public Brush SecondaryForeground
    {
        get => (Brush)GetValue(SecondaryForegroundProperty);
        set => SetValue(SecondaryForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the accent color used for links, quotes and task list markers.
    /// </summary>
    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the background of inline code and code blocks.
    /// </summary>
    public Brush CodeBackground
    {
        get => (Brush)GetValue(CodeBackgroundProperty);
        set => SetValue(CodeBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the background of table header rows.
    /// </summary>
    public Brush TableHeaderBackground
    {
        get => (Brush)GetValue(TableHeaderBackgroundProperty);
        set => SetValue(TableHeaderBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the color of rules, table borders and the heading underline.
    /// </summary>
    public Brush BorderBrush
    {
        get => (Brush)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the proportional text font.
    /// </summary>
    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the monospace font used for code.
    /// </summary>
    public FontFamily MonoFontFamily
    {
        get => (FontFamily)GetValue(MonoFontFamilyProperty);
        set => SetValue(MonoFontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the body font size in device independent pixels.
    /// </summary>
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the font whose color tables are used to render emoji. Defaults to Segoe UI Emoji.
    /// </summary>
    public FontFamily EmojiFontFamily
    {
        get => (FontFamily)GetValue(EmojiFontFamilyProperty);
        set => SetValue(EmojiFontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether emoji are rendered as color vector art. When disabled, or
    /// when the font provides no single colored glyph for an emoji, emoji stay as monochrome text.
    /// </summary>
    public bool ColorEmoji
    {
        get => (bool)GetValue(ColorEmojiProperty);
        set => SetValue(ColorEmojiProperty, value);
    }

    /// <summary>
    /// Scrolls the document back to the beginning.
    /// </summary>
    public void ScrollToTop()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            (GetScrollViewer())?.ScrollToTop()));
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Background is not null)
            drawingContext.DrawRectangle(Background, null, new Rect(RenderSize));
    }

    /// <inheritdoc />
    protected override int VisualChildrenCount => 1;

    /// <inheritdoc />
    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _host : throw new ArgumentOutOfRangeException(nameof(index));

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _host.Measure(availableSize);
        return _host.DesiredSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        _host.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        GetScrollViewer();
        UpdatePagePadding();
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(new MenuItem
        {
            Header = "Copy",
            Command = ApplicationCommands.Copy,
            CommandTarget = _host
        });

        menu.Items.Add(new MenuItem
        {
            Header = "Select All",
            Command = ApplicationCommands.SelectAll,
            CommandTarget = _host
        });

        return menu;
    }

    private ScrollViewer? GetScrollViewer()
    {
        if (_scrollViewer is not null)
            return _scrollViewer;

        _scrollViewer = FindVisualChild<ScrollViewer>(_host);
        if (_scrollViewer is not null)
            _scrollViewer.CanContentScroll = false;

        return _scrollViewer;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0)
            return;

        var scrollViewer = GetScrollViewer();
        if (scrollViewer is null ||
            !double.IsFinite(scrollViewer.ScrollableHeight) ||
            scrollViewer.ScrollableHeight <= 0 ||
            !double.IsFinite(scrollViewer.ViewportHeight) ||
            scrollViewer.ViewportHeight <= 0)
        {
            return;
        }

        var offset = scrollViewer.VerticalOffset -
                     e.Delta / MouseWheelDeltaPerNotch *
                     scrollViewer.ViewportHeight *
                     WheelScrollPageFraction;

        scrollViewer.ScrollToVerticalOffset(offset);
        e.Handled = true;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownViewer viewer)
            viewer.RenderDocument(force: false);
    }

    private static void OnRenderSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownViewer viewer)
            viewer.RenderDocument(force: true);
    }

    private static void OnLayoutSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownViewer viewer)
            viewer.UpdatePagePadding();
    }

    private static void OnSelectionEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownViewer viewer)
            viewer._host.IsSelectionEnabled = viewer.SelectionEnabled;
    }

    private static void OnBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownViewer viewer)
        {
            viewer._host.Background = viewer.Background;
            viewer.InvalidateVisual();
        }
    }

    private static void OnStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Built synchronously so a theme swap can never show stale colors for a frame.
        if (d is MarkdownViewer viewer)
            viewer.RenderDocument(force: true);
    }

    private void ApplyDocumentStyle()
    {
        _document.FontFamily = FontFamily;
        _document.FontSize = FontSize;
        _document.Foreground = SecondaryForeground;
        _document.LineHeight = FontSize * 1.65;
        _document.LineStackingStrategy = LineStackingStrategy.MaxHeight;
        _host.Background = Background;
        _host.IsSelectionEnabled = SelectionEnabled;
    }

    private void UpdatePagePadding()
    {
        var width = ActualWidth;

        if (width <= 0 || double.IsNaN(width))
        {
            _document.PagePadding = ContentPadding;
            return;
        }

        var contentWidth = ContentMaxWidth > 0 ? ContentMaxWidth : width;
        var spare = Math.Max(0, width - contentWidth) / 2;

        _document.PagePadding = new Thickness(
            Math.Max(ContentPadding.Left, spare),
            ContentPadding.Top,
            Math.Max(ContentPadding.Right, spare),
            ContentPadding.Bottom);
    }

    private MarkdownRenderOptions CreateOptions() => new()
    {
        Foreground = Foreground ?? DefaultForeground,
        SecondaryForeground = SecondaryForeground ?? DefaultSecondaryForeground,
        Accent = AccentBrush ?? DefaultAccent,
        CodeBackground = CodeBackground ?? DefaultCodeBackground,
        TableHeaderBackground = TableHeaderBackground ?? DefaultTableHeaderBackground,
        Border = BorderBrush ?? DefaultBorder,
        FontFamily = FontFamily ?? DefaultFontFamily,
        MonoFontFamily = MonoFontFamily ?? DefaultMonoFontFamily,
        FontSize = FontSize > 0 ? FontSize : 14,
        EmojiFontFamily = EmojiFontFamily,
        ColorEmoji = ColorEmoji,
        BasePath = BasePath,
        RenderLocalImages = RenderLocalImages,
        LoadRemoteImages = LoadRemoteImages
    };

    private void RenderDocument(bool force)
    {
        if (_isRendering)
            return;

        var markdown = Markdown;
        var options = CreateOptions();
        var styleChanged = _options is null || !options.Equals(_options);

        if (!force && !styleChanged && string.Equals(markdown, _renderedMarkdown, StringComparison.Ordinal))
            return;

        force |= styleChanged;
        _isRendering = true;

        try
        {
            ApplyDocumentStyle();

            var canReuseParse = _parsedDocument is not null &&
                                string.Equals(markdown, _renderedMarkdown, StringComparison.Ordinal);
            var parsed = canReuseParse ? _parsedDocument! : _parser.Parse(markdown);
            var signatures = canReuseParse
                ? _signatures
                : MarkdownDocumentRenderer.ComputeSignatures(parsed, markdown);

            var oldBlocks = _blocks;
            var prefix = 0;
            var oldSuffix = oldBlocks.Count;
            var newSuffix = signatures.Length;

            if (!force)
            {
                while (prefix < oldBlocks.Count &&
                       prefix < signatures.Length &&
                       oldBlocks[prefix].Signature == signatures[prefix])
                {
                    prefix++;
                }

                while (oldSuffix > prefix &&
                       newSuffix > prefix &&
                       oldBlocks[oldSuffix - 1].Signature == signatures[newSuffix - 1])
                {
                    oldSuffix--;
                    newSuffix--;
                }
            }

            var preserveScrollOffset = force && oldBlocks.Count > 0 ? GetScrollViewer()?.VerticalOffset : null;

            var renderer = new MarkdownBlockRenderer(options, OnLinkActivated);

            for (var i = oldSuffix - 1; i >= prefix; i--)
                _document.Blocks.Remove(oldBlocks[i].Block);

            var rebuilt = new List<BlockEntry>(newSuffix - prefix);
            WpfBlock? anchor = prefix > 0 ? _document.Blocks.ElementAt(prefix - 1) : null;
            WpfBlock? firstSuffix = prefix < _document.Blocks.Count ? _document.Blocks.ElementAt(prefix) : null;
            var index = 0;

            foreach (var markdownBlock in parsed)
            {
                if (index >= prefix && index < newSuffix)
                {
                    var block = renderer.Create(markdownBlock);

                    if (anchor is not null)
                    {
                        _document.Blocks.InsertAfter(anchor, block);
                        anchor = block;
                    }
                    else if (firstSuffix is not null)
                    {
                        _document.Blocks.InsertBefore(firstSuffix, block);
                    }
                    else
                    {
                        _document.Blocks.Add(block);
                    }

                    rebuilt.Add(new BlockEntry(signatures[index], block));
                }

                index++;
            }

            var combined = new List<BlockEntry>(signatures.Length);
            for (var i = 0; i < prefix; i++)
                combined.Add(oldBlocks[i]);
            combined.AddRange(rebuilt);
            for (var i = oldSuffix; i < oldBlocks.Count; i++)
                combined.Add(oldBlocks[i]);

            _blocks.Clear();
            _blocks.AddRange(combined);
            _renderedMarkdown = markdown;
            _parsedDocument = parsed;
            _signatures = signatures;
            _options = options;

            if (preserveScrollOffset is double offset)
                RestoreScrollOffset(offset);
        }
        finally
        {
            _isRendering = false;
        }
    }

    private void RestoreScrollOffset(double offset)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var scrollViewer = GetScrollViewer();
            scrollViewer?.ScrollToVerticalOffset(offset);
        }));
    }

    private void OnLinkActivated(string link)
    {
        var args = new MarkdownLinkClickedEventArgs(link);
        LinkClicked?.Invoke(this, args);

        if (args.Handled || string.IsNullOrWhiteSpace(link))
            return;

        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            return;

        if (uri.Scheme != Uri.UriSchemeHttp &&
            uri.Scheme != Uri.UriSchemeHttps &&
            uri.Scheme != Uri.UriSchemeMailto)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // No default handler is available; the link event still gives consumers a chance to react.
        }
    }

    private static SolidColorBrush CreateBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    internal readonly record struct BlockEntry(int Signature, WpfBlock Block);

    internal IReadOnlyList<BlockEntry> CachedBlocks => _blocks;
}
