# Changelog

## 1.0.2 - 2026-11-15

- New `EmojiText` attached property draws color emoji inside a plain `TextBlock`, so sidebar rows,
  window titles and any other read-only text match the viewer. Text without emoji still goes to
  `TextBlock.Text`, which keeps `CharacterEllipsis` trimming exact.
- New `RenderOnInputIdle` queues renders behind pending input. A live editor no longer pays for the
  preview inside the keystroke, and several fast keystrokes collapse into one render.
- Large documents are built in slices, so opening or pasting a big note keeps the window responsive.
  Slices pause while the scroll position is changing.
- New `RenderCompleted` event and `IsRendering` property, so a host can tell when a render finished,
  including one built in slices.
- New `Zoom` property, clamped to 50 to 300 percent. Ctrl+wheel zooms whether or not a scrollbar is
  present, and the optional `ShowZoomIndicator` shows the level in the corner for a moment.
- Emoji composition is better: the renderer now applies the font's own single substitutions and
  positioning rules, so three person families and couples draw as one emoji instead of separate
  people. Four person families and flags are still drawn as text because the font has no compact
  picture for them.
- Emoji scanning skips text that cannot contain emoji, so ordinary paragraphs cost less.
- Emoji characters stay in the document at an invisible size, so selecting and copying the preview
  keeps them.

## 1.0.1 — 2026-11-14

- New package icon in the ClipSafe palette.
- Small documentation tidy-ups.

## 1.0.0 — 2026-11-14

First public release.

- Markdown renders into a native WPF `FlowDocument`. No WebView, no browser process, no HTML.
- Live previews stay smooth: only the part of the document that changed is rebuilt, so typing does not
  reload the page or flicker, and no debounce timer is needed.
- Markdown support covers CommonMark plus tables, task lists, footnotes, definition lists, emphasis
  extras, GitHub-style autolinks and emoji shortcodes.
- Emoji render in full color using the system emoji font, including skin tones and emoji the font
  joins into one picture. Nothing extra to download.
- Themeable through dependency properties: colors and fonts follow `DynamicResource`, so switching a
  light or dark theme keeps working.
- Images load from local files, the web and `data:` URIs. Local images are decoded once and released,
  so the app never holds a lock on your files.
- Text selection, copy and printing come from `FlowDocument`, and links raise `LinkClicked`.
- Content is centered at a configurable maximum width, with configurable padding.
