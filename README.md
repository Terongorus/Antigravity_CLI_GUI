# Claude Code for Visual Studio

A Visual Studio extension that brings [Claude Code](https://docs.claude.com/en/docs/claude-code) into a
native tool window — chat with Claude, watch it read/edit files and run commands in your
project, and approve or deny each action, all without leaving the IDE.

This extension does not reimplement Claude Code. It is a thin, modern UI around the official
`claude` CLI, driven in its headless `stream-json` mode. Authentication, model access, and
billing are all handled by the CLI exactly as they would be for the
[official VS Code extension](https://code.claude.com/docs/en/vs-code) or the terminal —
installing/logging in once via `claude` makes it available here too.

## Features

- **Streaming chat** with full Markdown rendering — code blocks get real syntax highlighting and
  a code-editor-style header, inline `code`/fences respect your IDE theme
- **Tool call visualization** — expandable cards for `Read`, `Edit`, `Write`, `Bash`, `PowerShell`,
  `Grep`, `Glob`, `WebFetch`, `Task`, `TodoWrite`, MCP tools, and more, each with an icon, a
  one-line summary, and full input/output/diff detail on demand
- **Transcript view modes** — Summary, Normal, Thinking, and Verbose, controlling how much
  thinking/tool-call detail is shown by default
- **Live status line** while a turn is running — elapsed time, token usage, running-task count,
  and current status (waiting on Claude, running a tool, etc.)
- **Inline permission prompts** — Allow/Deny `can_use_tool` requests right in the chat, with
  1/2/3 keyboard shortcuts, a real line-level diff preview for file edits (opened automatically in
  a native VS diff tab), and a redirect textbox to tell Claude what to do instead
- **Answerable questions** — the `AskUserQuestion` tool renders as real radio buttons or
  checkboxes (single- and multi-select) instead of a dead-end approval card
- **IDE companion server** — gives the CLI live diagnostics from Visual Studio's own Error List,
  awareness of your open editors/active file/selection, and a real inline diff review flow: a
  proposed edit opens in a native VS diff window with Accept/Reject, instead of only ever
  rendering inside the chat
- **Rewind and fork** — jump back to any earlier point in the conversation, or fork a new branch
  from it, via the per-message "…" menu
- **MCP servers and plugins panels** — see connected MCP servers and installed plugins and their
  status right in the tool window
- **Voice dictation** and **remote control / cloud session hand-off**
- **Attachments** — paste or drag in screenshots (click any thumbnail, staged or sent, for a
  full-size preview with Ctrl+Wheel zoom and Shift+Wheel horizontal scroll), drop in text/code/PDF
  files, or attach the active file/selection as a real reference chip; a bare filename Claude
  mentions in its own prose auto-links to the real file when one exists in your workspace
- **Consolidated `/` command menu** (matching the VS Code extension) for switching the model,
  permission mode, and thinking budget, viewing live session usage (turns/cost/tokens, plus
  real account/subscription info and 5-hour/weekly rate-limit bars), and running slash commands —
  picking one runs it immediately, and `/usage` opens the usage panel locally with no API cost
- **Model switching** — Default, Sonnet, Opus, Haiku, Fable
- **Permission mode switching** — CLI Default, Accept Edits, Manual, Don't Ask, Plan Mode, Auto,
  Bypass Permissions
- **Thinking/effort control** — Standard, Low, Medium, High, Max, Extra High, applied via
  `--effort`
- **Context window usage indicator**, shown once you're getting close to the effective window
- **Generated session titles**, and a session-history list to switch between past conversations
- **Slash command autocomplete**, sourced live from the running session
- **Message queuing** — you can keep typing while Claude is still working; each message queues
  and runs in order, staying pinned where it was sent as the response keeps streaming
- **`/compact` support** — shows a "Compacting…" status and a "Compacted chat · N tokens freed"
  result, instead of a silent no-op
- **Retry on failure** — if a turn fails or the CLI process exits unexpectedly (including hitting
  a usage limit), a "Try again" resends your exact message once you're ready
- **Session persistence** — model/permission/thinking changes and crash recovery transparently
  `--resume` your conversation, restoring the full visible transcript from the CLI's own history
- **Extra CLI flag coverage** under **Tools → Options → Claude Code** — additional allowed
  directories, allowed/disallowed tools, system prompt append/replace, and MCP config file
  loading, on top of the CLI/model/permission/effort settings already there
- **Raw output panel** for debugging the underlying NDJSON protocol
- **Self-Update via GitHub Releases:** checks [GitHub releases](https://github.com/Terongorus/Teron_ClaudeCode_VS/releases)
  (not the VS Marketplace) once a day and offers to download and install the latest `.vsix`
  directly — **Tools → Claude Code: Check for Updates** triggers this on demand.

## Requirements

- Visual Studio 2022
- The [Claude Code CLI](https://docs.claude.com/en/docs/claude-code) installed and logged in
  (`claude` on your `PATH`, or installed via the official VS Code extension / `~/.claude/local`)

If the CLI can't be found automatically, set an explicit path under
**Tools → Options → Claude Code**.

## Usage

1. Open the tool window: **View → Other Windows → Claude Code** (or use the toolbar/Solution
   Explorer entry points).
2. Type a message and press **Enter** to send (**Shift+Enter** for a new line).
3. Use the **/** menu (bottom toolbar) to switch model, permission mode, or thinking budget,
   view session usage, or run a slash command. Use the **✚** header button to start a
   **New Session**.
4. When Claude wants to run a tool that requires approval, an inline card appears with
   **Allow** / **Deny** buttons.
5. Toggle **Raw** to see the underlying CLI event stream.

## Known limitations

- Plan Mode still shows the plan inline in chat rather than as a dedicated reviewable document.
- No UI yet for worktrees — not yet surfaced natively.
- Installing a new plugin or adding a marketplace still hands off to the terminal (`claude plugin
  install`/`claude plugin marketplace add`); the Plugins/Marketplaces panel is otherwise native.

## Building from source

```bash
dotnet build TeronClaudeCodeVS.csproj
```

Press F5 in Visual Studio to launch an experimental instance with the extension loaded, or
double-click the generated `bin/Debug/net481/TeronClaudeCodeVS.vsix` to install it into your main
Visual Studio.
