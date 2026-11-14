namespace NativeMarkdown.Wpf;

/// <summary>
/// Provides the target of a link that was activated inside a <see cref="MarkdownViewer"/>.
/// </summary>
public sealed class MarkdownLinkClickedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MarkdownLinkClickedEventArgs"/> class.
    /// </summary>
    /// <param name="link">The link from the Markdown source.</param>
    public MarkdownLinkClickedEventArgs(string link)
    {
        Link = link;
    }

    /// <summary>
    /// Gets the link exactly as it appears in the Markdown source.
    /// </summary>
    public string Link { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the click was handled. When left <see langword="false"/>,
    /// http, https and mailto links are opened with the default system handler.
    /// </summary>
    public bool Handled { get; set; }
}
