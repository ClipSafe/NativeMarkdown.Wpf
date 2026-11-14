using Markdig;
using Markdig.Syntax;

namespace NativeMarkdown.Wpf.Rendering;

/// <summary>
/// Parses Markdown and produces a cheap signature for every top-level block. The signature lets the
/// viewer keep visual blocks that did not change instead of rebuilding the whole document on each keystroke.
/// </summary>
internal sealed class MarkdownDocumentRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley()
        .DisableHtml()
        .Build();

    public MarkdownDocument Parse(string markdown) => Markdig.Markdown.Parse(markdown, Pipeline);

    public static int[] ComputeSignatures(MarkdownDocument document, string markdown)
    {
        var signatures = new int[document.Count];
        var index = 0;

        foreach (var block in document)
            signatures[index++] = ComputeSignature(block, markdown);

        return signatures;
    }

    private static int ComputeSignature(Block block, string markdown)
    {
        var span = block.Span;

        // The signature is position independent on purpose: an edit in one block shifts the source spans
        // of every following block, but their visuals can still be reused.
        if (span.End < span.Start)
        {
            var childCount = block is ContainerBlock container ? container.Count : 0;
            return HashCode.Combine(block.GetType(), block.Line, childCount);
        }

        var start = Math.Clamp(span.Start, 0, markdown.Length);
        var end = Math.Clamp(span.End + 1, start, markdown.Length);

        // The content hash catches same-length edits inside a block, which leave the source span untouched.
        return HashCode.Combine(block.GetType(), HashSlice(markdown, start, end));
    }

    private static uint HashSlice(string text, int start, int end)
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;

            var hash = offset;
            for (var i = start; i < end; i++)
            {
                hash ^= text[i];
                hash *= prime;
            }

            return hash;
        }
    }
}
