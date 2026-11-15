# NativeMarkdown.Wpf

A fast, native Markdown renderer for WPF. Markdown is parsed with [Markdig](https://github.com/xoofx/markdig)
and rendered into a WPF `FlowDocument`. 

The viewer caches the visual block for every top-level Markdown block and only rebuilds the blocks
whose source changed. Typing in a live editor therefore updates just the edited paragraph: no full
document reload, no flicker and no debounce timer.

## Features

- CommonMark through Markdig, including tables, task lists, footnotes, definition lists, emphasis
  extras, GitHub-style autolinks and emoji shortcodes (`:rocket:`).
- Color emoji rendered from the system emoji font's own `COLR`/`CPAL` layers as vector art, including
  skin tones and ZWJ sequences the font composes. Scaled with the surrounding text and cached per emoji,
  with no image assets to ship and no extra dependency.
- Incremental block updates with scroll position preservation.
- Large documents are built in slices, so opening or pasting a big note keeps the window responsive
  instead of freezing while the whole document is laid out. Slices pause while the scroll position is
  changing, so scrolling an opening note is never competing with the render.
- Ctrl+wheel zooms the preview (50% to 300%) and works whether or not a scrollbar is present. An
  optional badge shows the current level and fades out of the way on its own.
- Native text selection, copy and printing support from `FlowDocument`.
- Preview mouse-wheel scrolling advances 15% of the viewport per notch without animation.
- Links with a `LinkClicked` event; http, https and mailto links fall back to the system handler.
- Local, remote and `data:` URI images. Local files are decoded once and frozen, so the file is not
  locked. Remote images download asynchronously and never block typing.
- Themeable colors and fonts through dependency properties, so `DynamicResource` theme swaps work.
- Centered long-form layout with a configurable maximum content width.

## Requirements

- .NET 8 or newer with WPF (the package targets `net8.0-windows`).

## Usage

```xml
<Window
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:md="clr-namespace:NativeMarkdown.Wpf;assembly=NativeMarkdown.Wpf">
    <md:MarkdownViewer
        Markdown="{Binding NoteText, UpdateSourceTrigger=PropertyChanged}"
        BasePath="{Binding VaultPath}"
        AccentBrush="{DynamicResource AccentBrush}"
        Background="{DynamicResource PageBackgroundBrush}"
        BorderBrush="{DynamicResource BorderBrush}"
        CodeBackground="{DynamicResource SurfaceBrush}"
        Foreground="{DynamicResource TextBrush}"
        SecondaryForeground="{DynamicResource TextSecondaryBrush}" />
</Window>
```

Assign `Markdown` on every keystroke, for example from a `TextBox` bound with
`UpdateSourceTrigger=PropertyChanged`. Throttling or debouncing is not required.

In a live editor, also set `RenderOnInputIdle="True"`. The render then runs after pending input is
handled instead of inside the keystroke, and several fast keystrokes collapse into one render. On a
169 KB note that takes the cost of a keystroke from ~69 ms to under a millisecond. A viewer that is
loaded but not visible (for example a collapsed preview pane) skips rendering entirely until it is
shown again.

The document zooms with Ctrl+wheel, or from code through `Zoom`. Add `ShowZoomIndicator="True"` to
show the current level in the bottom right corner whenever it changes; the badge ignores pointer input
and hides itself after a moment, so it never gets in the way of reading or selecting text.

## Properties

| Property | Description |
| --- | --- |
| `Markdown` | Markdown source to render. |
| `BasePath` | Directory used to resolve relative image paths. |
| `RenderLocalImages` | Enables local file system images. Default `true`. |
| `LoadRemoteImages` | Enables http/https images. Default `true`. |
| `SelectionEnabled` | Allows text selection and copy. Default `true`. |
| `ContentMaxWidth` | Maximum content width in device independent pixels. Default `820`. `0` uses the full width. |
| `ContentPadding` | Minimum content padding. Default `36,32,36,96`. |
| `Background` | Viewer background. |
| `Foreground` | Headings, emphasis, code and table header text. |
| `SecondaryForeground` | Body text and secondary text. |
| `AccentBrush` | Links, quote bars and task markers. |
| `CodeBackground` | Inline code and code block background. |
| `TableHeaderBackground` | Table header background. |
| `BorderBrush` | Rules, table borders and the H1 underline. |
| `FontFamily`, `MonoFontFamily`, `FontSize` | Text fonts and body size. |
| `EmojiFontFamily` | Font whose color tables render emoji. Default `Segoe UI Emoji`. |
| `ColorEmoji` | Renders emoji as color vector art. Default `true`. |
| `RenderOnInputIdle` | Queues renders behind pending input instead of running inside the property change. Default `false`. |
| `Zoom` | Zoom level as a percentage, clamped to 50 to 300. Default `100`. |
| `ShowZoomIndicator` | Shows the zoom level in the bottom right corner for a moment after it changes. Default `false`. |

`LinkClicked` receives the link exactly as written in the Markdown. Set `Handled` in the event to
replace the default behavior.

`RenderCompleted` fires when a render finishes, including one that was built in slices, and
`IsRendering` reports whether a render is still being applied.

## Emoji in plain text

Sidebars, titles and other read-only text can show the same color emoji without a `MarkdownViewer`:

```xml
<TextBlock markdown:EmojiText.Source="{Binding NoteTitle}" TextTrimming="CharacterEllipsis" />
```

`EmojiText.Source` fills the block's inlines with emoji art. Text without emoji is assigned to
`TextBlock.Text` instead, so `CharacterEllipsis` trimming stays exact. `EmojiText.FontFamily` chooses
the emoji font and `EmojiText.IsEnabled` turns the behavior off.

## Notes and limitations

- Color emoji are drawn as vector art from the `COLR`/`CPAL` tables of the system emoji font, which WPF's
  text stack cannot colorize on its own. The renderer applies the font's own substitution rules (ligatures
  and chained context) and positioning adjustments to compose sequences such as skin tones, keycaps and
  multi-person ZWJ emoji. Two cases stay as monochrome text because the Windows emoji font provides no
  reachable composition for them: multi-person families and couples, and flags.
- Emoji are inline images, but the character is kept in the document at an invisible size, so
  selecting and copying the preview keeps the emoji instead of dropping it.
- SVG images are not decoded. PNG, JPEG, GIF, BMP and other WIC-supported formats work.
- HTML is disabled by design; raw HTML is displayed as text.
- Large documents are still subject to `FlowDocument` layout cost. A note of a few kilobytes
  updates in a few milliseconds; multi-hundred-block documents reflow in tens of milliseconds.
