# Phase 25 - Code Reference Attachments, Bare-Filename Auto-Linking, User Bubble Restyle

Three items Kaloyan raised live while Phase 24 was in flight, explicitly deferred there and
implemented together here per his own "keep going and implement all three" go-ahead.

## 1. Active File / Selection are now real attachments

Clicking "Active File" or "Selection" used to insert literal `@path#Lstart-Lend` text directly
into the composer's TextBox. Kaloyan pointed out the official VS Code extension instead shows a
real attachment chip (a code-like glyph as its "thumbnail," the same way an image attachment shows
a pixel preview), and asked for the same, including that clicking the attached chip in a *sent*
message opens the referenced file/selection.

- `ChatSessionViewModel`: new `PendingCodeReferenceAttachment` (relative/full path, optional
  start/end line, computed `DisplayTitle` and `ReferenceText`), a `PendingCodeReferences`
  collection with `AddPendingCodeReference`/`RemovePendingCodeReference`, mirroring
  `PendingImages`/`PendingFiles` exactly.
- `ClaudeCodeChatControl.InsertContextReference` (called by the existing Active File/Selection
  click handlers - their own resolution logic is untouched) now stages an attachment instead of
  touching `InputBox.Text` at all.
- `SendMessageAsync`: each staged reference becomes a `CodeReferenceAttachmentViewModel` block in
  the visible bubble; the CLI still only understands plain text, so `ReferenceText` for every
  staged chip is prepended to what's actually sent (`textToSend`), kept out of the bubble's own
  `TextBlockViewModel` so the user sees a clean chip rather than raw `@` syntax. `_lastSentText`
  (used by "Resend") is set from `textToSend` too, so a resend stays verbatim.
- `CodeReferenceAttachmentViewModel.OpenCommand` reuses `MarkdownRenderer.OpenFileReferenceAsync`
  (promoted from `private` to `public` for exactly this reuse) - the same open-file codepath an
  inline `@path` link already used, so there's exactly one place that logic lives.
- New composer staging chip (mirrors the `PendingFiles` WrapPanel) and a new sent-message
  `CodeReferenceAttachmentTemplate` - a borderless `Button` styled as a card, since FlowDocument
  attachment chips elsewhere in this template set aren't otherwise clickable.

## 2. Auto-linking a bare filename in Claude's own prose

A second GitHub Copilot Chat screenshot showed plain filenames mentioned in prose ("...see
`ClaudeCodeToolWindow.cs`...") rendered as clickable links, not just user-typed `@mentions`.
Kaloyan asked whether this was Copilot-exclusive; it isn't - it only needs matching against a real
file index, which this extension already builds for the composer's own `@`-mention autocomplete.

- `ClaudeCodePackage.IndexedProjectFiles`: the same `string[]` `ClaudeCodeChatControl`'s
  `_projectFiles` field already holds, mirrored onto the package singleton so `MarkdownRenderer` -
  a static class with no control instance to reach - can read it too.
- `MarkdownRenderer.LinkifyBareFilenames`: a second linkify pass (after the existing `@mention`
  one, over freshly-requeried Inlines so an already-linked mention can't be double-processed) using
  a loose `word.ext`-shaped regex as a candidate filter - the real precision is in only linking a
  candidate whose filename exactly (case-insensitively) matches a real entry in
  `IndexedProjectFiles`. A version number, "e.g.", or a filename Claude invented that isn't
  actually in the workspace is left as plain text instead of risking a dead or wrong link.

**Not unit-tested beyond the graceful-degradation path** - this suite deliberately never
instantiates `ClaudeCodePackage` (see `ChatControl.cs`'s own doc comment: "`Instance` - null out
here - from being consulted"), so `IndexedProjectFiles` is always empty under test. Real matching
needs a live F5 pass, same caveat as Phase 24's VS classification colors.

## 3. User message bubble restyled to match GitHub Copilot Chat

Reverses ST-5's own standing decision (`Core/ClaudeCodeChatControl.xaml`'s `UserMessageTemplate`
comment) - Kaloyan's explicit call this session, after comparing against Copilot's own user
messages. The terracotta-filled, white-text bubble is now the same neutral
`CardBackgroundBrush`/`HairlineBrush` card language already used everywhere else in this UI (tool
call cards, attachment chips), with normal VS-theme text instead of hardcoded white. The
right-aligned, tailed **shape** is kept - that solves a separate, real problem (turn-boundary
readability in a narrow docked panel) unrelated to which color fills it.

The `FileAttachmentTemplate`/`CodeReferenceAttachmentTemplate` chips previously used a hardcoded
translucent-white background/hover, tuned to sit on top of a saturated accent fill - updated to the
same `CardBackgroundBrush`/`HoverBrush` neutral tokens so they still read correctly nested inside
the now-neutral bubble on both VS themes.

## Verification

Build clean. New `CodeReferenceAttachmentTests.cs` covers `PendingCodeReferenceAttachment`'s
`DisplayTitle`/`ReferenceText` computation (whole-file, single-line, line-range) and
staging/removal on `ChatSessionViewModel` directly - `SendMessageAsync` itself isn't exercised
(starts a real CLI session, which this suite avoids everywhere else too). One new
`MarkdownRendererTests` case covers the auto-link feature's graceful no-op when no project is
indexed. Full suite: 193/194 (same one pre-existing, unrelated, environment-dependent failure).

## Not yet live-verified

All three items need a real F5 pass: the attachment chip's actual look/click-to-open, whether a
real Claude response actually mentions a bare filename that gets linked correctly, and whether the
new neutral user bubble reads well against both VS themes in practice - Kaloyan's own plan for this
batch.

## Addendum - every attachment is now interactive, not just code references

Kaloyan's own follow-up, from a GitHub Copilot Chat screenshot of a *sent* message: image/file
attachments there are clickable too - an image opens a full-size preview. Extended to both existing
attachment kinds so nothing in a sent message is inert:

- `ImageAttachmentViewModel.OpenCommand` opens a new `Controls/ImagePreviewWindow` - a small,
  non-modal WPF window (`ScrollViewer` + `Image`, capped at 1200x900) showing the already-decoded
  full-resolution bitmap. Deliberately not a VS document tab: a pasted screenshot never had a file
  on disk to open one for, so an in-app viewer is the only thing that works for a pasted **and** a
  dropped image alike.
- `FileAttachmentViewModel` now carries the attachment's actual content (previously discarded -
  only `Title` was kept), and its `OpenCommand` re-materializes that content into a fresh temp file
  and opens it: a text/code file through the same `MarkdownRenderer.OpenFileReferenceAsync` real
  editor tab everything else uses, a PDF through the OS's own default viewer via
  `Process.Start(UseShellExecute: true)` (VS has no built-in PDF renderer to open one in). The
  original dropped-from path is never assumed to still exist, since it was never retained in the
  first place.
- Both templates gained `Cursor="Hand"` + a `MouseBinding` on their existing `Border` rather than
  restructuring into a `Button` (matches how little `CodeReferenceAttachmentTemplate` needed to
  change vs. how it looks).

Build clean, same 193/194. Not separately unit-tested (temp-file I/O and spawning a real window/
process aren't worth faking through a mock for what's fundamentally "click a button, something
external happens") - covered by the same live F5 pass as the rest of this phase.

## Addendum 2 - live-test fallout: chrome colors, attachment layout, filename disambiguation, and a new editor-actions dropdown

Kaloyan's first live F5 pass against everything above surfaced four more issues in one round,
plus one net-new feature request from a further GitHub Copilot Chat comparison. All fixed/added in
the same sitting, before any release.

### The fenced-code-block chrome looked worse than the flat highlight it replaced

Phase 24's header+body chrome used the app's own accent hue (`#D97757`) at low alpha for both the
header and body fill - explicitly chosen so it would "never need a separate light/dark value." Live
inside a real tool-call card this reads as a washed-out, oddly-tinted box instead of Copilot's own
reference screenshots, which show one flat, real code-editor surface with a thin divider line under
the header - not two different colored bands.

Fixed by reading the ACTUAL VS code editor's own default background straight out of the
classification format map - the same source Phase 24 already uses for per-token foreground colors
(`ClaudeCodePackage.GetClassificationForeground`) - via a new `ClaudeCodePackage.GetEditorBackground()`
(`IClassificationFormatMap.DefaultTextProperties.BackgroundBrush`, category `"text"`). The header
Paragraph and the Section body now share this exact same brush; the header's own existing bottom
border is the only seam between them, matching the reference exactly. Inline `code` spans (a couple
of words inside a prose line) were never part of this complaint and keep the original accent tint -
a solid opaque editor-background fill would look like a broken box mid-sentence at that size. Falls
back to a neutral (non-accent) translucent gray, never actually seen live, when the classification
service isn't reachable (the xUnit tests' fake package-less environment).

### Sent-message attachments stacked one per row instead of wrapping inline

`UserMessageTemplate`'s single `ItemsControl` over the whole `Blocks` list used the default
vertical panel, so every attachment (each its own block) took a full-width row - the exact opposite
of the composer's own staging chips, which already wrap horizontally, and of how Copilot lays out a
sent message. Fixed by splitting `ChatMessageViewModel.Blocks` into two computed, live-updating
views - `AttachmentBlocks` (image/file/code-reference) and `NonAttachmentBlocks` (everything else,
i.e. the message text) - and giving the bubble two `ItemsControl`s: the first over
`AttachmentBlocks` with a `WrapPanel` `ItemsPanel`, the second over `NonAttachmentBlocks` with the
normal vertical panel underneath it. The three attachment DataTemplates' margins changed from
bottom-only (`0,0,0,6`) to bottom+right (`0,0,6,6`) so wrapped chips get a horizontal gap too, not
just a vertical one when they wrap to a new row.

### Code-reference chips rendered visibly greyer than the file chip next to them

`CodeReferenceAttachmentTemplate` was the one of the three sent-message attachment templates built
as a real `Button` with its own `IsMouseOver`-triggered background swap (`CardBackgroundBrush` ->
`HoverBrush`), rather than a plain `Border` + `MouseBinding` like `FileAttachmentTemplate`. Live,
it could render stuck in the hover-lighter fill next to a normal-colored file chip - most likely
because clicking it fires `OpenCommand`, which shifts focus straight into a VS editor window, and a
`Button` never receiving a normal `MouseLeave` on the way out can stay visually "hovered" long after
the mouse has left. Fixed by rebuilding it as a plain `Border` + `MouseBinding`, structurally
identical to `FileAttachmentTemplate` - it has no hover-triggered visual state to begin with, so it
cannot diverge from the file chip's fill regardless of root cause.

### Two same-named files in different folders both linked to whichever was indexed first

`LinkifyBareFilenames`'s bare-filename regex only ever captured the trailing `word.ext` (no
directory segments), and its resolution was `indexedFiles.FirstOrDefault(leaf name matches)` - so
when a workspace listing mentioned both `TestConsoleApp\Program.cs` and
`TestProjectClaude2\Program.cs`, both mentions silently linked to whichever `Program.cs` happened
to be first in `IndexedProjectFiles`, with no way to tell from the (identical) leaf name alone which
one Claude actually meant.

Fixed two ways:
- The regex now optionally captures leading directory segments in the same run of text
  (`(?:[A-Za-z0-9_.\-]+[\\/])*` before the `name.ext`), using lookaround instead of `\b` at the
  edges since `\b` can't sit in front of a segment starting with `.` (`.vscode\launch.json`) - `.`
  isn't a word character, so whitespace-to-`.` is never a word boundary.
- New `ResolveIndexedFile`: a candidate carrying its own directory segments is matched against the
  whole relative tail of each indexed path (`PathEndsWithSegments`, guarded against a
  `TestConsoleApp` false-positive matching `OtherTestConsoleApp` via a path-separator-or-start-of-
  string boundary check), so "TestConsoleApp\Program.cs" and "TestProjectClaude2\Program.cs" now
  resolve to their own distinct files. A bare leaf name with no folder context still only resolves
  when it is unique across the whole project; an ambiguous one either way is left as plain text
  rather than guessing, matching this feature's existing "never a wrong link" philosophy.
- Four new tests (`MarkdownRendererTests`) call `ResolveIndexedFile` directly via the existing
  `Reflect` helper, since the auto-link pass itself is gated behind `ClaudeCodePackage.Instance`
  (null under xUnit) the same way the rest of this feature already is.

### New: the header's "Insert at Cursor" dropdown (Insert at Cursor / Insert in New File / Apply in Active Document)

A further GitHub Copilot Chat screenshot showed the header's copy button was only half the
picture - the pill to its left is a dropdown with three actions. Added as a genuinely new feature,
not a fix:

- `VsIdeToolHandlers.InsertAtCursorAsync` - replaces the active editor's current selection with the
  block's text, or inserts at the caret with no selection.
- `VsIdeToolHandlers.InsertInNewFileAsync` - `DTE.ItemOperations.NewFile(@"General\Text File")`
  (the same thing File > New File does) then seeds the new, still-unsaved buffer with the block's
  text - naming/location is left to a normal Save As rather than guessing a path.
- `VsIdeToolHandlers.ApplyInActiveDocumentAsync` - replaces the ENTIRE contents of the active
  document with the block's text. Deliberately as literal as Copilot's own button: no diff/merge
  intelligence, just a full-buffer replace the user can Ctrl+Z out of.
- All three are best-effort like every other editor action in this file - `false` with no active
  editor rather than throwing, since an old message's code block can easily be clicked with nothing
  focused.
- `MarkdownRenderer.BuildHeaderActionsFloater` replaces the old copy-only `BuildCopyFloater`: ONE
  Floater containing a horizontal `StackPanel` with the new "Insert at Cursor ▾" button (opens a
  `ContextMenu` with all three actions) and the existing copy button, in that order - deliberately
  one floater with an internal panel rather than two separate right-aligned Floaters, since this
  static, no-visual-tree renderer can't verify FlowDocument's own stacking order for multiple
  same-side floaters without a live layout pass.

Build clean, 198/198 (194 + the 4 new `ResolveIndexedFile` tests). The chrome color, attachment
layout, and dropdown feature are UI-only changes with no pure-logic surface to unit test - all three
need the same live F5 pass as the rest of this phase before a release decision.
