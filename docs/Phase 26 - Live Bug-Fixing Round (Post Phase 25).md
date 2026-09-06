# Phase 26 - Live Bug-Fixing Round (Post-Phase 25)

A single extended live-testing session following Phase 24/25: Kaloyan ran the extension against
real sessions, real diff-tab edits, real permission prompts, and real attachments, reporting
issues one after another (often mid-turn, sometimes with a raw CLI JSONL log attached as
evidence) as he found them. Organized below by area rather than strictly chronologically, since
several threads were interleaved live.

## 1. Diff-tab CRLF/LF mismatch

Editing any file with real Windows (CRLF) line endings via a multi-line `Edit` tool call failed
with "the text it replaces isn't in the file as it currently stands" in the diff tab. Root-caused
by scanning real `~/.claude/projects/*/*.jsonl` session transcripts: the CLI's own
`old_string`/`new_string` are always bare-LF, even against a CRLF file on disk.

- `Core/VsDiffTab.cs`'s `Replace` now normalizes both the haystack and the needle/replacement to
  LF before matching, then restores the original CRLF convention on the result if the original
  file used it.
- Two new `DiffTabTests` cases cover a CRLF file matched against bare-LF `old_string`/
  `new_string`, both directions.
- Incidentally found and fixed: `DiffTabTests`' own cleanup was deleting the *entire* shared
  `%TEMP%\TeronClaudeCodeVS-difftab` root, which threw `UnauthorizedAccessException` when real,
  currently-open diff tabs from live testing had files open there too. Now only deletes the
  test's own subdirectories.
- **Confirmed live** across 4 real edit prompts.

## 2. Permission-prompt UX overhaul (propagated to every choice-style card)

Kaloyan found three issues on the file-edit permission card and explicitly asked that the fix
"propagate to ALL other prompt types" - `PermissionTemplate`, `ChoiceCardTemplate`, and the
`ChoiceCardViewModel` construction sites (terminal hand-off, `/feedback`, remote-control toggle)
all needed the same treatment:

1. **1/2/3 keyboard shortcuts not resolving** - see the diff-tab focus-steal fix below; this
   turned out to be a symptom of that deeper bug, not a keybinding bug in its own right.
2. **Numbered options need a period** ("1. Allow", not "1  Allow" double-space) - fixed on the
   permission card and all three `ChoiceCardViewModel` construction sites.
3. **Redirect textbox redesign** - "Tell Claude what to do instead" was a toggle button that
   revealed a textbox on click. Per Kaloyan's own critique ("It's a waste to have a button that
   needs to be clicked.. either way we get the text field"), replaced with an always-visible
   textbox using placeholder text (a `DataTrigger` on the bound `TextBox.Text == ""`), matching
   the composer's own placeholder pattern. `PermissionRequestViewModel.IsRedirectVisible`/
   `ToggleRedirectCommand` removed as dead code.

**Diff-tab focus steal (two attempts, the first one insufficient):** Kaloyan reported that after
this fix, 1/2/3 still didn't resolve on an edit-permission card unless he manually re-clicked the
tool window first. First attempt reordered `AutoOpenDiffTab` to run before `PendingPermissionRequest`
was set - insufficient, because a diff tab is a *real VS document window*: opening it steals VS's
own pane activation, not just WPF-level element focus, and that activation doesn't complete
synchronously within `VsDiffTab.Open`, so no reordering of WPF-level calls could win that race.
Fixed for real with `ClaudeCodePackage.ReactivateToolWindowAndFocusInput()` - `ShowToolWindowAsync`
to reclaim the pane, then a `DispatcherPriority.ContextIdle`-deferred `FocusInput()` - the same
pattern the existing global "focus chat" keybinding already used. Wired into both the automatic
and the manual "Open diff tab" button paths. **Confirmed live** across 4 real edit prompts, and
Kaloyan explicitly asked that the existing auto-open-diff-tab behavior be *retained*, not removed,
while this was being fixed.

## 3. Code-block chrome, round 3 (and the PowerShell tool-call fix)

The code-block background chase from Phase 24/25 continued to fail live twice more:

- Querying `IEditorFormatMapService`'s "Plain Text" → Background rendered solid white on a dark
  theme, despite being the documented, reflection-confirmed correct API shape - the second failed
  VS-SDK background lookup in a row (the first, `IClassificationFormatMap`, failed the same way in
  Phase 25's own fallout). **Abandoned VS SDK entirely** for a theme-agnostic translucent overlay
  (`Color.FromArgb(0x22, 0x80, 0x80, 0x80)`, matching `ChatTheme.xaml`'s existing card-background
  alpha pattern) - correct on any theme by construction rather than by querying for "the real"
  background.
- A second, more specific bug on the same pass: the code block's content `Paragraph` still showed
  light even after the header picked up the new background. `ClearValue(BackgroundProperty)`
  doesn't reliably fall through to the parent `Section`'s fill for a Markdig.Wpf-generated
  `Paragraph` - fixed by painting the paragraph explicitly instead.
- Copy button rebuilt: was `23x17px` (asymmetric `Padding="5,2,5,2"`) with a hard 90-degree hover
  box. Now a fixed `22x22` square using an XAML-parsed rounded `ControlTemplate`
  (`s_flatIconButtonTemplate`).
- Code-block header title font size bumped `11px` → `12px`.
- **PowerShell tool calls rendering as raw JSON**: `ToolPresentation.cs` only special-cased the
  literal tool name `"Bash"`; the CLI uses `"PowerShell"` on Windows for the same kind of call.
  Aliased everywhere except `GetDisplayName` (kept distinct there, more informative than Bash's
  generic "Run command"), with the code fence language made conditional (` ```powershell ` vs.
  ` ```bash `). Confirmed via real transcript scanning that no other shell-tool names exist
  (`Bash`: 12,813 real calls, `PowerShell`: 2,866, nothing else) - a Python invocation is just a
  `Bash`/`PowerShell` call whose command happens to be `python ...`, already covered.
- **Confirmed live**: copy button shape/highlight, background (both a command block and its
  Output block), title size, and the PowerShell command/description display all confirmed
  correct in the same live pass.

## 4. Message-queueing bugs (two, the second caused by the first fix)

- **Bug 1 - a message sent mid-turn visually "pushed to the bottom"**: `_currentAssistantMessage`
  persists across an entire multi-tool-call turn, only nulled by `ResetTurnState()` on a genuine
  `result` event. A message sent while a turn was still streaming landed in the transcript, but
  the turn's own container (positioned *before* it) kept growing, visually shoving the just-sent
  message downward with every new response chunk. Fixed with a new `_turnContinuedMidStream` flag
  that forces the next `EnsureAssistantMessage()` call to insert a fresh container at the end of
  `Messages` instead of growing the old one. **Confirmed live.**
- **Bug 2 - three sent messages got no visible response at all**: caused by Bug 1's own fix. The
  original `_pendingUserMessages` queue (which matches a genuinely new top-level turn to the user
  message that triggered it) was being enqueued into unconditionally, including messages absorbed
  mid-stream that never get a dedicated future turn of their own - leaving a stale, never-dequeued
  entry that a *later*, unrelated genuinely-new turn wrongly dequeued instead of itself,
  misplacing that later turn's real response near the stale entry. Root-caused from a real raw CLI
  JSONL log Kaloyan attached (`d:\Downloads\Raw.txt`), which proved three real, distinct responses
  existed in the stream despite none rendering visibly. Fixed by only enqueueing when not
  currently mid-stream. **Confirmed live** ("The stale queued messages issue has been resolved").

## 5. Resize-driven visual bugs

- **Per-message "…" rewind/fork button clipped when the docked tab is narrowed**: the sent-message
  bubble's `Grid` used an `Auto`-sized column that doesn't shrink with its parent - being
  right-aligned, the overflow cropped off the *left* edge, hiding the button that sits there
  instead of the bubble's text reflowing. Fixed with a new `WidthMinusOffsetConverter` binding the
  bubble's `MaxWidth` to the ancestor `ScrollViewer`'s `ActualWidth` (capped at the old 460px
  maximum via `ConverterParameter='460,120'`).
- **Resizing the docked tab snapped the transcript to the bottom**: `OnChatScrollChanged`'s
  heuristic keyed off `ExtentHeightChange` alone, which a width-driven text re-wrap also
  triggers (narrower content wraps taller) - indistinguishable from new content arriving by that
  signal alone. Fixed by early-returning when `e.ViewportWidthChange != 0`, since a genuine
  new-message growth never touches the viewport's own width. A *different* cause of the same
  symptom already fixed once before (the card-expander toggle, per Phase 22).
- **Confirmed live**, both fixes, plus the composer's own attachment-chip wrapping fix from
  Phase 25 re-confirmed live in the same pass.

## 6. Image attachment UX round

Four related fixes to how image attachments look and behave, found across one live pass:

1. Sent-message image thumbnails rendered up to `260x200` inline in the transcript, far larger
   than the composer's own `34x34` staging chip next to it. Shrunk `ImageAttachmentTemplate` to
   match - which then needed the name/dimensions text added back in a follow-up pass, since the
   plain-thumbnail version lost information the composer's chip still showed.
   `ImageAttachmentViewModel` now carries `Name` (from `PendingImageAttachment.Name` at send time)
   and a computed `DimensionsText`.
2. The image preview window (opened by clicking a sent image's thumbnail) didn't scale an
   oversized screenshot down to fit - `Window` had `MaxWidth`/`MaxHeight` with
   `SizeToContent="WidthAndHeight"`, but that only capped the *window*; the `Image` (`Stretch=
   None`) still rendered at native resolution inside it, producing scrollbars instead of a
   scaled-down fit. Moved the cap onto the `Image` itself (`Stretch=Uniform`,
   `StretchDirection=DownOnly`).
3. Added Ctrl+Wheel zoom (via a `LayoutTransform` `ScaleTransform`, so the `ScrollViewer`'s extent
   actually grows and shows scrollbars once zoomed in) and Shift+Wheel horizontal scroll,
   confirmed against the standard cross-app convention for both.
4. The composer's own staged thumbnail (before sending) is now clickable to open the same
   full-size preview - previously the only way to check what you'd actually attached was to send
   the message and look at the transcript afterward.
- **Confirmed live**, all four, across two separate live-test rounds.

## Verification

Every fix in this phase went through the same loop: `dotnet build` (0 warnings/errors) and
`dotnet test tests/TeronClaudeCodeVS.Tests` (202/202, aside from one confirmed-unrelated,
pre-existing flaky test - `DiffTabTests.The_checkpoint_store_reads_both_a_delta_backed_and_a_snapshot_only_history`,
fails identically in isolation and is untouched by any change in this phase) before every commit,
followed by Kaloyan's own live F5 pass. Nothing in this phase shipped on the strength of a clean
build alone.
