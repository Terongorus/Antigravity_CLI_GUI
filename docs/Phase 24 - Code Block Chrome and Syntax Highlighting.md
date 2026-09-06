# Phase 24 - Code Block Chrome and Syntax Highlighting

Requested live by Kaloyan from GitHub Copilot Chat screenshots: our fenced code blocks were a flat
accent-tinted rectangle with a floating corner copy button, no per-token coloring at all. Copilot's
own blocks are a real code-editor card - a header strip (filename/language + copy action) over
properly tokenized, colored text. Scoped explicitly via `AskUserQuestion` to include real syntax
highlighting, not just the header chrome.

## Color source: the real VS editor, not a hand-picked palette

Kaloyan's own steer mid-implementation: "Use the color tokens provided by the Visual Studio
theme." `ClaudeCodePackage.GetClassificationForeground(string classificationTypeName)` resolves
`IClassificationTypeRegistryService`/`IClassificationFormatMapService` via `IComponentModel` and
reads `IClassificationFormatMap.GetTextProperties(...).ForegroundBrush` from the shared `"text"`
category format map - the same live, theme- and Fonts-and-Colors-aware source the real editor and
(per Kaloyan) GitHub Copilot Chat's own code blocks use. No new NuGet dependency: these assemblies
(`Microsoft.VisualStudio.Text.Logic`, `.Text.UI.Wpf`, `Microsoft.VisualStudio.Language.StandardClassification`)
already ship transitively via the existing `Microsoft.VisualStudio.SDK` reference, confirmed via
reflection against the real installed packages before writing any code that assumed it.

## Tokenizing: hand-rolled, not a new dependency

`Controls/SyntaxHighlighter.cs` is one generic regex engine (comment/string/number/keyword
alternation, built per language profile) rather than pulling in AvalonEdit or ColorCode - matches
this codebase's own stated preference (see `Sta.cs`: "four dozen lines instead of a dependency").
Six profiles: csharp (shared by java/go/rust/cpp/c/kotlin/swift as a reasonable approximation),
javascript (js/ts/jsx/tsx), python, bash (sh/shell/zsh), json, powershell. Unlisted languages fall
back to a single Plain token - i.e. today's un-tokenized behavior, unchanged.

## Chrome: a header+body Section, not a recolored Paragraph

`MarkdownRenderer.WalkBlocks` now detects a fenced/indented code block (Markdig.Wpf's own
light-background tell, same heuristic as before) and replaces it wholesale with a `Section`
(card background/border) containing a header `Paragraph` and the original content `Paragraph`,
instead of just recoloring the one Paragraph in place. The header shows:
- the target file's name as a clickable `Hyperlink` (opens it in a real VS editor tab, reusing the
  existing `OpenReferenceAsync` machinery) when the block is about one specific file - threaded in
  via a new `Render(string markdown, string? primaryFilePath)` overload, wired from
  `ToolCallViewModel.DetailDocument` and `PermissionRequestViewModel`'s constructor using the
  already-computed `ToolPresentation.GetFullPath`/`FullPath`;
- otherwise the language name (e.g. "bash", or "text" if unknown);
- a copy button on the right (same glyph/behavior as before, moved off the code body).

Language-per-block correlation: Markdig.Wpf's `ToXaml()` throws away the original AST (including
each fence's language info-string) by the time it's plain WPF TextElements. A second, independent
`Markdig.Markdown.Parse` of the same source text extracts every `CodeBlock`'s `Info` in document
order into a `Queue<string?>`, dequeued one-per-code-block as `WalkBlocks` encounters each one -
same document-order traversal Markdig.Wpf itself used to render them, so the positions line up.

Diff blocks (`` ```diff ``, from Edit's own generated markdown) keep the existing green/red
line-coloring instead of being tokenized, but now get the same header chrome as everything else -
including a clickable filename, since Edit calls have a real `file_path` too.

## A second real bug, found via this phase's own test

Writing a diff-coloring regression test surfaced that **Markdig.Wpf renders an entire fenced code
block as one single `Run` with embedded `\n` characters, not one `Run` per line.** WPF's text
layout still wraps that Run at each `\n`, so it always *looked* right - but the pre-existing
`ApplyDiffColors` (checking/coloring per-`Run`) was actually coloring the **whole block one color**,
based on whichever line happened to be first, never real per-line +/- coloring. This predates this
phase entirely; it was just never caught before. Fixed by splitting each Run on `\n` into its own
colored Run rejoined with explicit `LineBreak`s.

Relatedly, moving code-block handling out of `FixupParagraph` (which used to unconditionally clear
Markdig.Wpf's black-foreground default on every paragraph, code blocks included) into `WalkBlocks`
directly meant an **unsupported language's block would render literal black text** regardless of
VS theme, since nothing else was clearing that default anymore for the un-tokenized fallback path.
Fixed by clearing it explicitly right where the new code-block branch starts, before either the
diff or token-color path runs.

## Verification

Build clean. `tests/TeronClaudeCodeVS.Tests/Phases/MarkdownRendererTests.cs` rewritten for the new
Section-based structure and extended: header-shows-language, header-links-to-file-not-language,
csharp keywords/strings actually split into separate Runs (proves tokenizing ran, not just "didn't
crash"), and the diff-still-colors-per-line regression test that found the bug above. Full suite:
187/188 (same one pre-existing, unrelated, environment-dependent failure every run this session has
had - a deleted fixture transcript, rendered as Failed instead of Skipped by this local runner).

## Not yet live-verified

The actual on-screen look (header strip contrast, hyperlink click really opening the file, real
per-theme classification colors in a live VS instance vs. the `null`-service fallback path tests
exercise) needs a real F5 pass - all of this was verified through the compiled renderer directly,
the same technique (and the same caveat) as every other MarkdownRenderer phase this session.

## Queued from the same conversation, not yet started

Kaloyan raised several related but separate ideas while this was in flight, explicitly deferred to
their own pass rather than folded in here:
- Active File / Current Selection chips redesigned as real attachments with thumbnails (a code-like
  glyph, matching how image/document attachments already look), click-to-open like the existing
  `@file#Lstart-Lend` links.
- Auto-linking bare filenames Claude's own prose mentions (not just user-typed `@` references)
  against the real project file index already built for `@`-mention autocomplete.
- Restyling the user-message bubble to match GitHub Copilot Chat's own look/color - a deliberate
  reversal of the standing ST-5 decision ("keep the terracotta bubble against baseline's no-bubble
  style"), on Kaloyan's own explicit say-so this session.
