# Phase 27 - Session Recovery and Live Bug-Fixing Round

The prior session ("Latest - Session history review and v0.7.1 verification") never wrote up its
own work: it got stuck in a rapid compact→resume→compact loop near the end of 2026-09-07 (5
compactions in under 10 minutes) and the next day's continuation opened on a malformed chat
bubble instead of a real reply, with no summary ever recorded. This phase starts by reconstructing
that lost work from raw evidence rather than assuming it, then continues with a live-verification
pass that found several more real bugs - some in the recovered work itself, one in the recovery
process's own tooling.

## 1. Recovering the lost session

Read the CLI's own on-disk transcript for that session directly
(`~/.claude/projects/.../9595da74-....jsonl`, 2822 lines) plus `git diff` on the working tree -
nothing was actually lost from disk, only the narrated record of it. Reconstructed 8 real,
uncommitted changes this way (see items 2-8 below) and confirmed the tree still built clean and
passed all 219 tests before touching anything further.

**Root cause of the malformed bubble, found in the same pass:** `ViewModels/TranscriptReplay.cs`,
which rebuilds a resumed session's visible history from that same on-disk transcript, only ever
skipped `isSidechain` lines. It never checked `isCompactSummary` - the CLI's own synthetic
`type:"user"` recap line written after an auto-compact (`isCompactSummary: true`,
`isVisibleInTranscriptOnly: true`) was being replayed as if a human had typed the entire "This
session is being continued from a previous conversation..." block. Fixed by skipping it the same
way `isSidechain` is skipped, and `Load(workingDirectory, sessionId)` was split into a thin path
lookup plus a new `internal static LoadFromPath(string path)` seam for direct testing (see item 9).

## 2. Context-usage/compact button never appeared, at any threshold

`OnTurnCompleted` looked up `result.ModelUsage` (keyed by the CLI's full resolved model id, e.g.
`"claude-sonnet-4-5-20250929"`) using `SelectedModel.Value`, which is null for "Default" or a
short alias like `"sonnet"` - never a real key. `_contextWindowSize` was never populated, so
`EffectiveContextWindow` stayed 0 and the indicator was permanently hidden regardless of the
configured threshold. Fixed by capturing the CLI's own resolved model id from `system:init` into
`_resolvedModelId` and keying the lookup off that instead. New `ContextIndicatorTests.cs` drives
the same two private handlers a real session drives, with real-shaped mismatched ids, so this
exact mismatch shape can't ship unnoticed again.

A sibling bug in the same area: the "Show Threshold (%)" option only ever got read once, in
`OnLoaded` - changing it in Tools → Options while a chat panel was already open did nothing until
the tool window was closed and reopened. Fixed with a new `ClaudeCodeOptionsPage.SettingsApplied`
event (raised from `OnApply` on Apply/OK), subscribed by the chat control to re-push the setting
into the view model live.

## 3. Shift+wheel horizontal scroll

Missing in three more places it was needed: code/output blocks in chat replies
(`Controls/MarkdownRenderer.cs`), diff previews in Edit-file tool cards and permission prompts
(`Controls/DiffViewer.xaml.cs`), and the raw-output panel. WPF has no built-in Shift+wheel
convention; each spot got its own `PreviewMouseWheel` handler. The outer message-list handler
(registered `handledEventsToo:true`) also had to learn to bow out on `Keyboard.Modifiers ==
ModifierKeys.Shift`, or it would force a vertical scroll on the same tick a nested snippet box
just used to scroll itself horizontally.

## 4. Resumed-session scroll position and mid-turn jiggling

- A bulk transcript replay (hundreds of messages in one synchronous burst) outran the message
  list's virtualization badly enough that even a capped "retry ScrollToEnd() every LayoutUpdated
  tick" loop wasn't enough against this repo's own real, months-long dev transcript - the extent
  estimate kept climbing past any reasonable tick budget. Fixed by turning
  `VirtualizingPanel.IsVirtualizing` off just long enough for one real full-measure layout pass,
  calling `ScrollToEnd()` (now exact), then restoring virtualization immediately after.
- Separately, the "was I at the bottom" heuristic re-derived position from every individual
  `ExtentHeightChange` event, which can misfire under virtualization when an off-screen item's
  estimated height self-corrects - and was only excluding `ViewportWidthChange`, not
  `ViewportHeightChange` (caused by the background running-tasks status strip growing/shrinking in
  the same layout). Both now excluded; the "should follow new content" bit is only recomputed from
  a genuine user-scroll event.

## 5. Whimsical working-status line: typewriter effect, then two follow-on rounds

The session's main feature ask: animate the whimsical "Percolating…"-style status line so each
character types in, with the hold/timeout before the next phrase starting only once typing
finishes. Two independent timers - the pre-existing 1s elapsed-time timer and a new 35ms timer
driving a typing→holding state machine - reuse the existing bindable text property directly, so no
XAML binding change was needed. Placement was corrected after an initial attempt folded it into
the top status strip instead of its own standalone line above the composer, matching baseline.

Two more rounds followed once this was live:

- **A brand-new, never-touched session could show a whimsical word with nothing actually
  working.** Several permission/tool-response code paths call the same "start the whimsical
  cycle" method assuming a turn is already in flight, rather than starting one themselves - if any
  ever fired while nothing was actually busy, the timer would start with nothing to ever stop it,
  since the only place that cleared it was the busy-flag's own true→false transition. Fixed with an
  unconditional reset called both from that transition and from session start, so a (re)started
  session can never inherit leftover state.
- **The one-shot "Done" flash after a successful turn was removed outright** ("a useless state...
  has no reason to be there at all") and the same pending/done icon pair repurposed into a
  continuous blink while work is actually in progress, toggled on a fixed cadence inside the
  existing 35ms tick. A follow-on bug in the same feature: the "Compacting…" status bypassed the
  tick loop entirely (stopped the timer, set the text directly), freezing the icon instead of
  blinking it - fixed with a `ShowFixedWorkingVerbText` helper that pins the text but keeps the
  timer (and therefore the blink) running.

## 6. New welcome art and asset cleanup

New commissioned SVG welcome art (`Resources/welcome-art-{light,dark}.svg`) replaces the old plain
accent-mark badge on a blank new session, rendered at runtime via the new `SharpVectors.Reloaded`
package (too large for the hand-ported-`Geometry` convention the rest of the icon set uses). The
old `Resources/logo_icon.png` was removed, replaced by new `claude-logo-{pending,done}.svg` source
art used to hand-port the working-status line's icon pair.

## 7. Resuming after a real `/compact` still leaked CLI bookkeeping into the chat

Found live, testing item 1's own fix: after a real `/compact` run, reloading the session showed
not just the compact recap but three more synthetic lines as if a human had sent them - the raw
`<local-command-caveat>`/`<command-name>`/`<local-command-stdout>` wrapper the CLI writes for a
locally-run slash command, plus its own auto-sent "Continue from where you left off." nudge on a
no-new-input resume. None of these carry the `isCompactSummary` flag the first fix checked for;
the local-command lines carry no flag at all (distinguishable only structurally - bare string
content instead of the array shape a real prompt uses), and the nudge is separately flagged
`isMeta:true`. Both are now skipped the same way, in `TranscriptReplay`'s `"user"` branch. New
`TranscriptReplayTests.cs` runs against a freshly captured real fixture (a short dogfooding
session with two real `/compact` runs) and asserts a real prompt sent immediately after one of
these sequences still comes through - proof this isn't a blanket "hide everything near a compact"
filter.

## Verification

Every item in this phase was live-tested by Kaloyan in the EXP instance, not just built/tested -
including two items (7, and the compacting-blink half of item 5) that were only found *because*
the live pass caught something the build+test loop couldn't. Items 4's two scroll fixes were
explicitly signed off as "complete for now, future improvement possible" rather than fully
polished. `dotnet build` clean throughout; `dotnet test tests/TeronClaudeCodeVS.Tests` finished at
220/220 (219 existing + `TranscriptReplayTests`), with one pre-existing flaky STA/clipboard test
(`AttachmentTests.The_real_Paste_command_still_pastes_plain_text`) confirmed to pass in isolation
and unrelated to anything touched this phase.
