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
