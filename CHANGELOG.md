# Changelog

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
