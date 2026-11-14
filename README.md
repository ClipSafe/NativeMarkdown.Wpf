# NativeMarkdown.Wpf

A fast, native Markdown renderer for WPF. Markdown is parsed with [Markdig](https://github.com/xoofx/markdig)
and rendered into a WPF `FlowDocument`.

The viewer caches the visual block for every top-level Markdown block and only rebuilds the blocks
whose source changed. Typing in a live editor therefore updates just the edited paragraph: no full
document reload, no flicker and no debounce timer.

This is the preview engine behind [ClipSafe Notes](https://github.com/ClipSafe), published so other
WPF apps can use it.

## Features

- CommonMark through Markdig, including tables, task lists, footnotes, definition lists, emphasis
  extras, GitHub-style autolinks and emoji shortcodes (`:rocket:`).
- Color emoji rendered from the system emoji font's own `COLR`/`CPAL` layers as vector art, including
  skin tones and ZWJ sequences the font composes. Scaled with the surrounding text and cached per emoji,
  with no image assets to ship and no extra dependency.
- Incremental block updates with scroll position preservation.
- Native text selection, copy and printing support from `FlowDocument`.
- Preview mouse-wheel scrolling advances 15% of the viewport per notch without animation.
- Links with a `LinkClicked` event; http, https and mailto links fall back to the system handler.
- Local, remote and `data:` URI images. Local files are decoded once and frozen, so the file is not
  locked. Remote images download asynchronously and never block typing.
- Themeable colors and fonts through dependency properties, so `DynamicResource` theme swaps work.
- Centered long-form layout with a configurable maximum content width.

## Requirements

- .NET 8 or newer with WPF (the package targets `net8.0-windows`).

## Install

```powershell
dotnet add package NativeMarkdown.Wpf
```

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

`LinkClicked` receives the link exactly as written in the Markdown. Set `Handled` in the event to
replace the default behavior.

## Notes and limitations

- Color emoji are drawn as vector art from the `COLR`/`CPAL` tables of the system emoji font, which WPF's
  text stack cannot colorize on its own. The renderer applies the font's own substitution rules (ligatures
  and chained context) and positioning adjustments to compose sequences such as skin tones, keycaps and
  multi-person ZWJ emoji. Two cases stay as monochrome text because the Windows emoji font provides no
  reachable composition for them: multi-person families and couples, and flags. Emoji are also inline
  images, so copying text that contains them omits the emoji characters.
- SVG images are not decoded. PNG, JPEG, GIF, BMP and other WIC-supported formats work.
- HTML is disabled by design; raw HTML is displayed as text.
- Large documents are still subject to `FlowDocument` layout cost. A note of a few kilobytes
  updates in a few milliseconds; multi-hundred-block documents reflow in tens of milliseconds.


## License

MIT. See [LICENSE](LICENSE).
