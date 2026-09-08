# Change Log

All notable changes to the **Claude Code for Visual Studio** extension will be documented in
this file.

## [0.7.2] - 2026-09-08

Another live bug-fixing round, this time centered on the context-usage indicator, session-resume
fidelity, and the whimsical "working" status line.

* **Fixed the context-usage/compact button never appearing, at any threshold.** The lookup used to
  find the CLI's context-window size for the current model didn't match how the CLI actually keys
  that data, so the indicator was permanently hidden regardless of your configured threshold.
  Fixed, and the "Show Threshold (%)" option now also applies immediately to an already-open chat
  panel instead of requiring a reopen.
* **Fixed Shift+Wheel horizontal scroll not working in code/output blocks, diff previews (both the
  Edit-file tool card and the permission-prompt preview), and the raw-output panel** - previously
  only supported on attachment previews.
* **Fixed a resumed session (especially a long one) sometimes loading scrolled to the top instead
  of the bottom**, and fixed the transcript occasionally jiggling/jumping while you were scrolled
  up reading history mid-turn.
* **The whimsical "working" status line now animates a proper typewriter effect** - each phrase
  types in character by character, with the pause before the next phrase starting only once typing
  finishes - and its icon now blinks continuously while work is actually in progress (including
  while compacting), replacing a one-shot "Done" flash that added little. Also fixed the line
  itself occasionally appearing on a brand-new session with nothing actually happening.
* **Fixed resuming a session after running `/compact` showing raw CLI bookkeeping text as if it
  were a real chat message** - the compact summary, the command echo, and the "Continue from where
  you left off." auto-nudge no longer leak into the visible transcript.
* New commissioned welcome-screen art for a brand-new empty session, replacing the old plain
  accent-mark badge.

## [0.7.1] - 2026-09-06

Live bug-fixing round, mostly bugs found while dogfooding across two different projects in the
same day.

* **Fixed pasting a real screenshot doing nothing at all**, with the right-click "Paste" menu
  item visibly greyed out. The clipboard-format fix alone wasn't enough: WPF's TextBox disables
  its own Paste command entirely whenever the clipboard holds only an image, so Ctrl+V and the
  menu item never even reached the paste handler. Also fixed the underlying format-detection gap
  that caused it in the first place - many real screenshot/browser sources only synthesize the
  classic clipboard bitmap format, which silently fails once the source app is gone; the real
  "PNG" format is now read directly instead.
* **Fixed dragging an image into the chat box also opening it in Visual Studio's own Image
  Editor** and stealing focus (flipping a docked Properties tab to the front if one shares that
  slot) - the drop event wasn't marked handled, so it kept bubbling past the chat control into
  VS's own shell-level drop handling.
* **Fixed markdown tables rendering as raw pipe-and-dash source text** instead of an actual
  table. Two stacked causes: the rendering library's XAML-string API has no table support at all
  (switched to its native FlowDocument API, which does), and its table parser doesn't let a table
  interrupt an in-progress paragraph - very common when a reply leads with a bold intro line
  directly above the table with no blank line between them, now handled automatically.
* **Fixed table borders being invisible on a dark theme** (a hardcoded black default meant for a
  plain white page) and switched from a full grid box to a lighter, horizontal-only separator
  look with a heavier header underline.
* **Fixed "Thinking" sections that expand to reveal nothing.** The CLI's extended-thinking API
  can return a thinking block that's empty except for a signature, for a round that simply didn't
  produce visible reasoning - those are now hidden entirely instead of showing an openable-but-
  empty card.
* **Long command/output text in a tool-call card no longer wraps** across several lines - it now
  stays on one line with a horizontal scrollbar, the way a real terminal or code editor would
  show it, so long paths and wide diagnostics stay readable.

## [0.7.0] - 2026-09-06

First stable release since the 0.6.x beta series - a large batch of fixes and a few new
capabilities from an extended round of live day-to-day use.

* **Fixed: editing a file with real Windows (CRLF) line endings** could fail with "the text it
  replaces isn't in the file as it currently stands" on any edit spanning more than one line -
  the CLI's own `old_string`/`new_string` are always plain LF, even against a CRLF file on disk.
* **Reworked permission prompts across every choice-style card** (file-edit approval, terminal
  hand-off, `/feedback`, remote-control toggle): 1/2/3 keyboard shortcuts now actually resolve,
  numbered options consistently show "1." with a period, and "Tell Claude what to do instead" is
  now an always-visible textbox with placeholder text instead of a button you had to click first
  to reveal it.
* **Fixed the diff tab stealing keyboard focus for real.** Opening a diff tab is a real Visual
  Studio document window, so it was stealing VS's own pane activation, not just focus inside the
  tool window - a prior fix addressed the wrong layer and needed re-fixing. This also applies to
  the manual "Open diff tab" button.
* **Fixed the code-block background still showing light/washed-out** under an otherwise-correct
  dark header, plus a squared-off, properly-sized copy button and a larger code-block header
  title.
* **`Allow PowerShell?` and other PowerShell tool-call permission prompts now show the actual
  command and its description**, instead of dumping the raw tool-call JSON as both the summary
  and the code block.
* **Fixed message queuing**: a message sent while Claude is still working now stays pinned where
  it was sent instead of visually drifting toward the bottom of the transcript as the response
  keeps streaming, and a rare case where a queued message's real response could get misdirected to
  the wrong place in the transcript (reading as "no response at all") is fixed.
* **Fixed the per-message "…" rewind/fork button getting clipped off** when the docked tool window
  is narrowed, and fixed narrowing/resizing the docked tool window from forcing the transcript to
  snap to the bottom.
* **Sent-message image thumbnails are now a compact chip** (matching the composer's own staging
  chip) instead of rendering up to 260x200 inline, and the full-size image preview window now
  scales an oversized screenshot down to fit instead of showing scrollbars - it also supports
  Ctrl+Wheel to zoom in and Shift+Wheel to scroll horizontally once zoomed in, and clicking a
  staged (not-yet-sent) image thumbnail now opens the same preview.
* **Fixed system-notice text** (e.g. a diff-tab failure reason) getting cut off mid-sentence
  instead of wrapping.
* **Added:** real SVG icons throughout the UI (mic/send/stop/tools/add/session history/settings/
  new session/copy/done/warning, plus the Claude logo mark), replacing emoji/glyph placeholders.
* **Added:** real syntax highlighting and a code-editor-style header for code blocks, active
  file/selection context is now a real attachment chip (not raw `@`-syntax inserted into the
  composer), and a bare filename Claude mentions in prose now auto-links to the real file when one
  exists in the workspace.

## [0.6.4] - 2026-09-05

* **Fixed: a tool-call card showing a command followed by its output** (e.g. "Run command")
  could leave the output's code block on the old, unfixed light-grey background even after
  0.6.3's highlight-color fix. Root cause: a real crash in the markdown post-processor that was
  being silently swallowed - inserting the first code block's copy button invalidated the walk
  over the rest of the message, so every block after the first one in a multi-block message
  quietly skipped all of its fixups (background, foreground, copy button). This had been
  happening since the copy-button feature shipped; it's now fixed for good, with regression
  tests.

## [0.6.3] - 2026-09-05

More live-feedback fixes from continued day-to-day use of 0.6.2.

* **Fixed: the code-block copy button now shows an icon** instead of the literal word "Copy"
  (with checkmark/warning icons for the copied/failed states).
* **Fixed: code highlighting used a plain grey tint that didn't actually stand out.** Inline
  `code` spans and fenced code blocks (including tool-call "Run command" output) now use the
  extension's own accent color at low opacity instead.
* **Added: the chat header now shows the active session's title**, matching the official VS
  Code extension's own header.
* **Fixed: the Ctrl+Alt+Y shortcut didn't focus the input box** when the chat panel was already
  the visible tab — it activated the tool window but left keyboard focus nowhere useful, so
  typing right after using the shortcut did nothing.

## [0.6.2] - 2026-09-05

More fixes and one new feature from continued day-to-day use of 0.6.1.

* **Fixed: sluggish resizing and lag switching to/from the docked chat tab.** The message
  transcript now uses real UI virtualization instead of rendering every message at once.
* **Fixed: "Delete Session" behavior now matches the official VS Code extension** — it hides the
  session from history rather than deleting its underlying transcript, so it can't be confused
  with actually destroying data.
* **Fixed: low-contrast markdown rendering.** Inline code highlighting was barely visible against
  the background, and headings didn't follow the current VS theme's text color the way body text
  already did.
* **Added: a context-window usage indicator**, matching the official VS Code extension — a badge
  showing how much of the context window is in use, click to compact. Unlike that reference, the
  button is disabled (not silently broken) while Claude is still responding. The badge's visibility
  threshold (default 50%) is now also configurable in the extension's settings.

## [0.6.1] - 2026-09-05

Follow-up fixes to 0.6.0's session-history scoping, found while testing that build live.

* **Fixed: session history was still missing most of a project's real sessions.** The previous
  fix only scoped this extension's own small local record of sessions it had personally run — it
  didn't yet look at Claude Code's actual session history for that folder. History now also picks
  up every session ever run for the current workspace, regardless of whether it was started from
  this extension, a terminal, or another editor.
* **Fixed: renaming a session that had never been opened through this extension before could lose
  the new name** the next time history refreshed.

## [0.6.0] - 2026-09-05

A batch of fixes from real day-to-day use of the extension, plus a first accessibility pass.

* **Fixed: session history showed every session on the machine, not just this workspace's.**
  History now only lists sessions started from the currently open solution/folder.
* **Fixed: changing the model, permission mode, or thinking level while Claude was still
  responding silently dropped the change.** It's now applied as soon as the current response
  finishes, instead of only taking effect after a manual restart.
* **Fixed: switching to a different docked tab and back could interrupt an in-progress
  response.** The chat panel no longer treats a tab switch the same as actually closing it.
* **Fixed: messages sent while Claude was still responding to an earlier one could render out of
  order**, making the conversation confusing to read back. They now appear next to the response
  that actually answers them.
* **Fixed: expanding a "Thinking" or tool-call section always scrolled the chat to the bottom.**
* **Improved:** the "Try again" button is now labeled "Resend," to match what it actually does
  (resends your original message as a new turn).
* **Improved: accessibility.** Icon-only buttons throughout the chat panel (Send, Stop, New
  session, History, Settings, and every panel-close button) now have real labels for screen
  readers, instead of announcing nothing meaningful.

## [0.5.0] - 2026-09-02

Fixes found during a live manual QA pass against the 0.4.0 build, plus one small feature
requested during that same pass.

* **New: clickable file/line references.** The `@path#Lstart-Lend` references the Active File and
  Selection chips write into the composer are now live links once a message has been sent —
  clicking one opens that file and selects the referenced lines.
* **Fixed: dictation could not be stopped by clicking the mic a second time.** The click that
  starts dictation was suppressing the click meant to stop it; tap-to-toggle now works both ways.
* **Fixed: tool-call output blocks rendered with a stark white background in the dark theme.**
* **Fixed: permission and choice-card keyboard shortcuts (`1`/`2`/`3`) stopped working after a
  previous card was answered by mouse.** Keyboard focus now returns to the composer whenever a new
  card appears.
* **Improved:** the live dictation status line now wraps instead of hard-truncating to one line.

## [0.4.0] - 2026-09-01

A full pass at parity with the official "Claude Code for VS Code" extension, driven by a live
side-by-side audit against it.

* **New: rewind and fork conversations.** Pick any earlier point in a session and restore your
  code to how it looked then, continue from there in a new forked conversation, or both — with a
  preview of exactly which files will change and a confirmation before anything on disk moves.
* **New: a native side-by-side diff tab.** Alongside the existing inline diff card, a proposed
  edit now also opens in a real Visual Studio diff editor tab, with Previous/Next-difference
  navigation built in.
* **New: real session titles.** History rows now show the same generated title Claude Code itself
  assigns to a session, instead of a truncated first message. Renaming a session yourself still
  always wins.
* **New: MCP servers panel** and **New: Manage plugins panel**, both reachable from Customize,
  showing your configured MCP servers and installed/available plugins and marketplaces without
  leaving the IDE.
* **New: a `+` add menu** on the composer — upload a file from disk, insert `@` to add context, or
  ask Claude to fetch a URL or search the web.
* **New: automatic model fallback.** Optionally set a fallback model in Options; when Claude
  switches models mid-session (due to load, a refusal, or a usage-credit boundary) the transcript
  now shows a clear notice explaining why.
* **New: voice dictation.** Tap or hold the new mic button (or `Ctrl+D`) to dictate into the
  composer using Windows' own offline speech recognizer — nothing is sent anywhere for this.
* **New: a Running sessions tab** in History, listing other Claude Code sessions active on this
  machine (including background agents) with the option to open one here or in a terminal; and a
  **Cloud tab** to hand off to a cloud session by ID or link.
* **New: terminal hand-off cards** for Memory, Agents, Hooks, Output Styles, and Permissions, and
  a new **Open Claude in Terminal** command, matching the official extension's Customize menu.
* **New: `/btw`, `/feedback`, and `/remote-control`** slash commands.
* **Redesigned visual style** to match the official extension more closely — a consistent type
  scale and corner radii throughout, and the accent color confined to exactly the surfaces it
  should be (send button, focus ring, selection, and this extension's own deliberately-kept user
  message bubble).
* **Improved: a large batch of UX polish** — model and permission-mode pickers now explain what
  each option does; permission prompts show the full file path, accept number-key selection
  (`1`/`2`/`3`), and can redirect Claude with a typed reason instead of just denying; the command
  palette gained a filter box; tool-call groups summarize count and failures; code blocks in
  responses gained a copy button; attachment chips show file dimensions and type; and new sessions
  show a proper empty state instead of a blank panel.
* **Fixed: the Active File / Selection context chips** silently doing nothing — or, worse,
  attaching the wrong file — when the active tab was a Markdown Preview tab rather than a code
  editor.

## [0.3.0] - 2026-08-26

* **New: IDE companion server.** The extension now runs a local companion server the CLI can
  connect to, the same way the official VS Code extension does — giving Claude live diagnostics
  from Visual Studio's own Error List, awareness of your open editors/active file/selection, and
  a real inline diff review flow (a proposed edit opens in a native VS diff window with
  Accept/Reject) instead of only ever rendering inside the chat.
* **New: answerable questions.** The `AskUserQuestion` tool now renders as real radio buttons or
  checkboxes (single- and multi-select) you can actually answer, instead of a dead-end Allow/Deny
  card.
* **Fixed: permission prompts for built-in tools (Edit/Write/Bash/…) not appearing at all.** Root
  cause was a missing CLI flag; this also fixes the inline diff/permission flow for proposed edits.
  Diff previews inside permission cards are now a real line-level diff instead of a raw dump.
* **New: `/compact` support.** Shows a "Compacting…" status and a "Compacted chat · N tokens
  freed" result, instead of silently doing nothing useful.
* **New: message queuing.** You can keep typing while Claude is still working — each message
  queues and runs in order, matching the official extension, instead of the input being blocked.
* **New: retry on failure.** If a turn fails or the CLI process exits unexpectedly (including
  hitting a usage limit), a "Try again" resends your exact message once you're ready.
* **New: a real Account & Usage panel** — shows your actual account/subscription info and live
  5-hour/weekly rate-limit usage, instead of the empty placeholder it showed before.
* **Fixed: inline code in chat responses** rendering as a harsh, theme-incorrect solid block
  regardless of your IDE theme.
* **Fixed: Stop now sends a real interrupt** instead of killing and restarting the CLI process;
  resuming a past session now restores the full visible transcript, not just the session ID.
* **Fixed: an invalid default permission-mode value** the CLI would have rejected; the permission
  mode list now matches the CLI's real options (added Manual and Don't Ask, which were missing).
* **New: additional CLI flag coverage** under Tools → Options → Claude Code — additional allowed
  directories, allowed/disallowed tools, system prompt append/replace, and MCP config file
  loading.
* **New: Extra High** added to the thinking/effort level options.
* Slash commands picked from the `/` menu now run immediately instead of requiring a manual
  Enter; `/usage` now opens the usage panel locally instead of sending a wasted message to Claude.

## [0.2.0] - 2026-08-26

* **Renamed the extension's underlying identity.** The GitHub repo (and this project's own
  folder) is now `Teron_ClaudeCode_VS` (was `ClaudeCode_CLI_GUI`), and the technical identity
  (namespace, `AssemblyName`/`RootNamespace`, VSIX `Identity Id`/`Publisher`) is now
  `TeronClaudeCodeVS` (was `ClaudeCodeGUI`). The **Claude Code for Visual Studio** display name
  and all features are unchanged. Pre-1.0 with no prior releases, so there's no existing-install
  migration concern.
* **Relicensed from Apache-2.0 to GPL-3.0**, matching the license used across the rest of this
  developer's public extensions and applications.
* **New: Self-Update via GitHub Releases.** The extension now checks its own GitHub Releases
  (never the VS Marketplace) once a day for a newer version and offers to download and install
  it via an in-IDE notification — **Tools → Claude Code: Check for Updates** runs this check on
  demand. No VS Marketplace publishing is used or planned.
* Repository branch model is `dev`/`release` (unchanged from before this rename).

## [0.1.0] - 2026-06-28

* **Initial feature set**: streaming chat with Markdown rendering; tool call visualization for
  `Read`/`Edit`/`Write`/`Bash`/`Grep`/`Glob`/`WebFetch`/`Task`/`TodoWrite`/MCP tools; inline
  Allow/Deny permission prompts; a consolidated `/` command menu for model, permission mode, and
  thinking budget, plus live session usage; model switching (Default/Sonnet/Opus/Haiku/Fable);
  permission mode switching (Default/Accept Edits/Plan Mode/Bypass Permissions); thinking budget
  control; "Add Active File"/"Add Selection" context references; slash command autocomplete;
  session persistence with crash-recovery `--resume`; a raw NDJSON output panel for debugging.
