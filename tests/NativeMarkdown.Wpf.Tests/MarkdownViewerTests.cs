using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NativeMarkdown.Wpf.Tests;

public class MarkdownViewerTests
{
    [Fact]
    public void Renders_common_block_types()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer
            {
                Markdown = """
                    # Title

                    Body **bold** and *italic*.

                    - first
                    - second

                    > quoted

                    ```csharp
                    var value = 1;
                    ```

                    | Name | Value |
                    | ---- | ----- |
                    | one  | 1     |
                    """
            };

            Assert.Equal(6, viewer.CachedBlocks.Count);

            var heading = Assert.IsType<Paragraph>(viewer.CachedBlocks[0].Block);
            Assert.Equal(28, heading.FontSize, 3);
            Assert.Equal("Title", GetText(heading));

            var list = Assert.IsType<List>(viewer.CachedBlocks[2].Block);
            Assert.Equal(2, list.ListItems.Count);

            var quote = Assert.IsType<Section>(viewer.CachedBlocks[3].Block);
            Assert.Equal("quoted", GetText(quote));

            var code = Assert.IsType<Paragraph>(viewer.CachedBlocks[4].Block);
            Assert.Contains("var value = 1;", GetText(code));

            var table = Assert.IsType<Table>(viewer.CachedBlocks[5].Block);
            Assert.Equal(2, table.RowGroups[0].Rows.Count);
        });
    }

    [Fact]
    public void Renders_emoji_shortcodes_and_task_lists()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer
            {
                Markdown = "- [x] shipped :rocket:\n- [ ] pending :smile:",
                ColorEmoji = false
            };

            var list = Assert.IsType<List>(viewer.CachedBlocks[0].Block);
            var text = GetText(list);

            Assert.Contains('\u2611', text);
            Assert.Contains('\u2610', text);
            Assert.Contains("\U0001F680", text);
            Assert.Contains("\U0001F604", text);
            Assert.DoesNotContain(":rocket:", text);
        });
    }

    [Fact]
    public void Reuses_unchanged_blocks_when_editing()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer { Markdown = "# Title\n\nalpha\n\nomega" };
            var before = viewer.CachedBlocks.Select(entry => entry.Block).ToArray();

            viewer.Markdown = "# Title\n\nalpha edited\n\nomega";
            var after = viewer.CachedBlocks.Select(entry => entry.Block).ToArray();

            Assert.Equal(3, after.Length);
            Assert.Same(before[0], after[0]);
            Assert.Same(before[2], after[2]);
            Assert.NotSame(before[1], after[1]);
            Assert.Equal("alpha edited", GetText(after[1]));
        });
    }

    [Fact]
    public void Replaces_whole_document_when_switching_notes()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer { Markdown = "# First\n\none" };
            var before = viewer.CachedBlocks.Select(entry => entry.Block).ToArray();

            viewer.Markdown = "# Second\n\ntwo";

            Assert.Equal(2, viewer.CachedBlocks.Count);
            Assert.All(viewer.CachedBlocks, entry => Assert.DoesNotContain(entry.Block, before));
            Assert.Equal("Second", GetText(viewer.CachedBlocks[0].Block));
        });
    }

    [Fact]
    public void Does_not_render_images_when_local_images_are_disabled()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer
            {
                Markdown = "![diagram](images/diagram.png)",
                RenderLocalImages = false
            };

            Assert.Contains("diagram", GetText(viewer.CachedBlocks[0].Block));
        });
    }

    [Fact]
    public void Raises_link_clicked_for_hyperlinks()
    {
        StaThread.Run(() =>
        {
            string? clicked = null;
            var viewer = new MarkdownViewer { Markdown = "See [docs](https://example.com/docs)." };
            viewer.LinkClicked += (_, e) =>
            {
                clicked = e.Link;
                e.Handled = true;
            };

            var paragraph = Assert.IsType<Paragraph>(viewer.CachedBlocks[0].Block);
            var hyperlink = FindHyperlink(paragraph.Inlines);
            Assert.NotNull(hyperlink);

            hyperlink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            Assert.Equal("https://example.com/docs", clicked);
        });
    }

    [Fact]
    public void Mouse_wheel_scrolls_fifteen_percent_of_the_viewport()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer
            {
                Markdown = string.Join("\n\n", Enumerable.Range(1, 100).Select(index => $"Paragraph {index}."))
            };
            viewer.Measure(new Size(600, 400));
            viewer.Arrange(new Rect(0, 0, 600, 400));
            viewer.UpdateLayout();
            viewer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, viewer));
            viewer.UpdateLayout();

            var scrollViewer = FindVisualChild<ScrollViewer>(viewer);
            Assert.NotNull(scrollViewer);
            var viewportHeight = scrollViewer.ViewportHeight;
            Assert.True(viewportHeight > 0);
            Assert.True(scrollViewer.ScrollableHeight > viewportHeight);

            var wheelEvent = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
            {
                RoutedEvent = Mouse.PreviewMouseWheelEvent
            };
            scrollViewer.RaiseEvent(wheelEvent);
            viewer.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

            Assert.True(wheelEvent.Handled);
            Assert.InRange(scrollViewer.VerticalOffset, viewportHeight * 0.14, viewportHeight * 0.16);
        });
    }

    [Fact]
    public void Zoom_is_clamped_to_the_supported_range()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer { Markdown = "Body" };
            Assert.Equal(100, viewer.Zoom);

            viewer.Zoom = 900;
            Assert.Equal(300, viewer.Zoom);

            viewer.Zoom = 10;
            Assert.Equal(50, viewer.Zoom);

            viewer.Zoom = double.NaN;
            Assert.Equal(100, viewer.Zoom);
        });
    }

    [Fact]
    public void Zoom_badge_appears_only_when_the_indicator_is_enabled()
    {
        StaThread.Run(() =>
        {
            var plain = new MarkdownViewer { Markdown = "Body" };
            LoadViewer(plain);
            plain.Zoom = 150;
            plain.UpdateLayout();

            Assert.Equal(1, VisualTreeHelper.GetChildrenCount(plain));

            var viewer = new MarkdownViewer { Markdown = "Body", ShowZoomIndicator = true };
            LoadViewer(viewer);
            viewer.Zoom = 125;
            viewer.UpdateLayout();

            Assert.Equal(2, VisualTreeHelper.GetChildrenCount(viewer));

            var badge = Assert.IsType<Border>(VisualTreeHelper.GetChild(viewer, 1));
            Assert.Equal(Visibility.Visible, badge.Visibility);
            Assert.False(badge.IsHitTestVisible);
            Assert.Equal("125%", Assert.IsType<TextBlock>(badge.Child).Text);
            Assert.True(badge.ActualWidth > 0);

            // Bottom right corner, clear of the scrollbar.
            var corner = badge.TranslatePoint(new Point(badge.ActualWidth, badge.ActualHeight), viewer);
            Assert.InRange(corner.X, viewer.ActualWidth - 40, viewer.ActualWidth + 0.5);
            Assert.InRange(corner.Y, viewer.ActualHeight - 32, viewer.ActualHeight + 0.5);
        });
    }

    [Fact]
    public void Zoom_badge_hides_itself_after_the_change()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer { Markdown = "Body", ShowZoomIndicator = true };
            LoadViewer(viewer);
            viewer.Zoom = 140;
            viewer.UpdateLayout();

            var badge = Assert.IsType<Border>(VisualTreeHelper.GetChild(viewer, 1));
            Assert.Equal(Visibility.Visible, badge.Visibility);

            var watch = Stopwatch.StartNew();
            while (badge.Visibility == Visibility.Visible && watch.ElapsedMilliseconds < 4000)
            {
                Thread.Sleep(50);
                DoEvents();
            }

            Assert.Equal(Visibility.Collapsed, badge.Visibility);
        });
    }

    [Fact]
    public void Renders_single_emoji_as_color_art()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer { Markdown = "Launch :rocket:" };

            var paragraph = Assert.IsType<Paragraph>(viewer.CachedBlocks[0].Block);
            var container = paragraph.Inlines.OfType<InlineUIContainer>().Single();
            var element = Assert.IsAssignableFrom<FrameworkElement>(container.Child);
            Assert.True(element.Width > 0);
            Assert.True(element.Height > 0);

            // The character stays in the document so copying the preview keeps the emoji.
            Assert.Contains("\U0001F680", GetText(paragraph));
        });
    }

    [Fact]
    public void Keeps_the_emoji_character_out_of_sight()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer { Markdown = "Launch :rocket:" };

            var paragraph = Assert.IsType<Paragraph>(viewer.CachedBlocks[0].Block);
            var copied = paragraph.Inlines
                .OfType<Run>()
                .Single(run => run.Text.Contains("\U0001F680", StringComparison.Ordinal));

            // Tiny and transparent: text for the clipboard, invisible on screen.
            Assert.True(copied.FontSize < 1);
            Assert.Equal(Brushes.Transparent, copied.Foreground);
        });
    }

    [Fact]
    public void Raises_render_completed_for_small_documents_too()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer();
            var completed = 0;
            viewer.RenderCompleted += (_, _) => completed++;

            viewer.Markdown = "# Small";

            Assert.False(viewer.IsRendering);
            Assert.Equal(1, completed);
        });
    }

    [Fact]
    public void Defers_rendering_until_input_idle()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer { RenderOnInputIdle = true };
            var renders = 0;
            viewer.RenderCompleted += (_, _) => renders++;

            viewer.Markdown = "# Title";

            // The property change must not render on the input thread.
            Assert.Empty(viewer.CachedBlocks);

            DoEvents();

            Assert.Single(viewer.CachedBlocks);
            Assert.Equal(1, renders);
        });
    }

    [Fact]
    public void Collapses_fast_edits_into_one_render()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer { RenderOnInputIdle = true };
            viewer.Markdown = "# One";
            DoEvents();

            var renders = 0;
            viewer.RenderCompleted += (_, _) => renders++;

            for (var i = 0; i < 5; i++)
                viewer.Markdown = $"# One{i}";

            DoEvents();
            PumpUntilRendered(viewer);

            Assert.Equal(1, renders);
            Assert.Equal("One4", GetText(viewer.CachedBlocks[0].Block));
        });
    }

    [Fact]
    public void Renders_large_documents_in_slices()    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer();
            var completed = 0;
            viewer.RenderCompleted += (_, _) => completed++;

            viewer.Markdown = BuildDocument(400);

            // The big document is handed to the dispatcher instead of blocking this call.
            Assert.True(viewer.IsRendering);

            PumpUntilRendered(viewer);

            Assert.Equal(1, completed);
            Assert.Equal(800, viewer.CachedBlocks.Count);
        });
    }

    [Fact]
    public void Replacing_a_sliced_render_leaves_a_consistent_document()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer();
            viewer.Markdown = BuildDocument(400);
            Assert.True(viewer.IsRendering);

            viewer.Markdown = "# Small";
            PumpUntilRendered(viewer);

            Assert.Single(viewer.CachedBlocks);
            Assert.Equal("Small", GetText(viewer.CachedBlocks[0].Block));
        });
    }

    [Fact]
    public void Small_edits_to_a_large_document_stay_synchronous()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer();
            var markdown = BuildDocument(400);
            viewer.Markdown = markdown;
            PumpUntilRendered(viewer);

            viewer.Markdown = markdown.Replace("## Section 0", "## Section zero", StringComparison.Ordinal);

            // Typing must never wait on the dispatcher, so a small change is applied in place.
            Assert.False(viewer.IsRendering);
            Assert.Equal(800, viewer.CachedBlocks.Count);
            Assert.Equal("Section zero", GetText(viewer.CachedBlocks[0].Block));
        });
    }

    private static string BuildDocument(int sections)
    {
        var builder = new StringBuilder(sections * 120);
        for (var i = 0; i < sections; i++)
        {
            builder.Append("## Section ").Append(i).Append("\n\n");
            builder.Append("Paragraph with **bold** and `code` and a [link](https://example.com).\n\n");
        }

        return builder.ToString();
    }

    private static void PumpUntilRendered(MarkdownViewer viewer, int timeoutMs = 20_000)
    {
        var watch = Stopwatch.StartNew();
        while (viewer.IsRendering && watch.ElapsedMilliseconds < timeoutMs)
            DoEvents();

        Assert.False(viewer.IsRendering);
    }

    /// <summary>Lays the viewer out and raises Loaded, so it behaves like one inside a window.</summary>
    private static void LoadViewer(MarkdownViewer viewer)
    {
        viewer.Measure(new Size(600, 400));
        viewer.Arrange(new Rect(0, 0, 600, 400));
        viewer.UpdateLayout();
        viewer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, viewer));
        viewer.UpdateLayout();
    }

    /// <summary>Processes queued dispatcher work once, without needing a window or message loop.</summary>
    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    [Fact]
    public void Renders_zwj_sequences_composed_by_substitution_features()
    {
        StaThread.Run(() =>
        {
            // Regression: applying every ligature lookup in the font, rather than only the ones the
            // substitution features reference, left these sequences stuck in the text fallback.
            var viewer = new MarkdownViewer { Markdown = "\U0001F9D1\u200D\U0001F4BB" };

            var paragraph = Assert.IsType<Paragraph>(viewer.CachedBlocks[0].Block);
            Assert.Single(paragraph.Inlines.OfType<InlineUIContainer>());
        });
    }

    [Fact]
    public void Renders_zwj_sequences_that_need_chained_context_rules()
    {
        StaThread.Run(() =>
        {
            // Holding hands is only composed by the font's chained context rule, not by a plain ligature.
            var viewer = new MarkdownViewer { Markdown = "\U0001F9D1\u200D\U0001F91D\u200D\U0001F9D1" };

            var paragraph = Assert.IsType<Paragraph>(viewer.CachedBlocks[0].Block);
            Assert.Single(paragraph.Inlines.OfType<InlineUIContainer>());
        });
    }

    [Fact]
    public void Renders_emoji_as_text_when_color_emoji_is_disabled()
    {
        StaThread.Run(() =>
        {
            var viewer = new MarkdownViewer { Markdown = "Launch :rocket:", ColorEmoji = false };

            var paragraph = Assert.IsType<Paragraph>(viewer.CachedBlocks[0].Block);
            Assert.Empty(paragraph.Inlines.OfType<InlineUIContainer>());
            Assert.Contains("\U0001F680", GetText(paragraph));
        });
    }

    [Fact]
    public void Composes_multi_person_families()
    {
        StaThread.Run(() =>
        {
            // Three-person families compose into one picture through the font's contextual single
            // substitutions; before those were handled these stayed monochrome text.
            var viewer = new MarkdownViewer { Markdown = "\U0001F468\u200D\U0001F469\u200D\U0001F467" };

            var paragraph = Assert.IsType<Paragraph>(viewer.CachedBlocks[0].Block);
            Assert.Single(paragraph.Inlines.OfType<InlineUIContainer>());
        });
    }

    [Fact]
    public void Keeps_sequences_it_cannot_compose_as_text()
    {
        StaThread.Run(() =>
        {
            // This font exposes no composition for four-person families, so it stays as text rather
            // than drawing the parts stacked on top of each other.
            const string family = "\U0001F468\u200D\U0001F468\u200D\U0001F467\u200D\U0001F466";
            var viewer = new MarkdownViewer { Markdown = family };

            var paragraph = Assert.IsType<Paragraph>(viewer.CachedBlocks[0].Block);
            Assert.Empty(paragraph.Inlines.OfType<InlineUIContainer>());
            Assert.Contains(family, GetText(paragraph));
        });
    }

    private static Hyperlink? FindHyperlink(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Hyperlink hyperlink:
                    return hyperlink;
                case Span span:
                    var nested = FindHyperlink(span.Inlines);
                    if (nested is not null)
                        return nested;
                    break;
            }
        }

        return null;
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

    private static string GetText(Block block)
    {
        var builder = new StringBuilder();
        AppendBlock(block, builder);
        return builder.ToString();    }

    private static void AppendBlock(Block block, StringBuilder builder)
    {
        switch (block)
        {
            case Paragraph paragraph:
                AppendInlines(paragraph.Inlines, builder);
                break;
            case Section section:
                foreach (var child in section.Blocks)
                    AppendBlock(child, builder);
                break;
            case List list:
                foreach (var item in list.ListItems)
                    foreach (var child in item.Blocks)
                        AppendBlock(child, builder);
                break;
            case Table table:
                foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var child in cell.Blocks)
                                AppendBlock(child, builder);
                break;
        }
    }

    private static void AppendInlines(InlineCollection inlines, StringBuilder builder)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    builder.Append(run.Text);
                    break;
                case LineBreak:
                    builder.Append(' ');
                    break;
                case Hyperlink hyperlink:
                    AppendInlines(hyperlink.Inlines, builder);
                    break;
                case Span span:
                    AppendInlines(span.Inlines, builder);
                    break;
            }
        }
    }
}
