# Captured CLI output used as test fixtures

Real output from the real Claude Code CLI, kept verbatim so the harnesses under `../scripts` can be
re-run later without needing the session that produced it to still exist.

## `rewind-session-original.jsonl` / `rewind-session-forked.jsonl`

Captured 2026-08-30, CLI v2.1.251, while establishing FEAT-1's mechanisms before any of it was
built. A throwaway session in a scratch directory was given two turns:

1. *"Create a file note.txt … whose entire contents are the single word ALPHA."*
2. *"Now change note.txt so its entire contents are the single word BETA."*

`rewind-session-original.jsonl` is that session's transcript. It is the fixture for
`SessionCheckpointStore.ReadRewindPoints`, and it is worth having precisely because of what is
*not* obvious in it:

* it holds **four** `user` records but only **two** real prompts — the other two are tool-result
  relays, which a naive reader lists as rewind points the user never typed;
* the second turn edits a file the CLI was **already** tracking, so it has **no
  `file-history-delta` of its own** — the case that made an earlier, delta-only reading of this
  store return the right file from the wrong point in its history;
* the second prompt's preceding chain entry is an `assistant` record, which is the id
  `--resume-session-at` needs, and is not always the record's own `parentUuid`.

`rewind-session-forked.jsonl` is what
`--resume <id> --fork-session --resume-session-at <that assistant uuid>` produced from it, plus one
further trivial turn. It is the evidence for the fork half: a different session id, the first turn
kept intact, the BETA turn gone, and the original left untouched.

## `transcript-replay-compact-and-local-command.jsonl`

Captured 2026-09-08, CLI v2.1.261, from a real dogfooding session in a scratch C# project (not this
repo) - a short back-and-forth with two real `/compact` runs triggered mid-session. Fixture for
`TranscriptReplayTests`, kept because of exactly what it demonstrates a naive reader gets wrong:

* the CLI's own `isCompactSummary` recap line ("This session is being continued from a previous
  conversation…") is a synthetic `type:"user"` record, not something a human typed;
* running `/compact` locally also writes three more `type:"user"` records with **no** `isMeta` or
  `isCompactSummary` flag at all - `<local-command-caveat>…</local-command-caveat>`,
  `<command-name>/compact</command-name>…`, and `<local-command-stdout>Compacted </local-command-stdout>`
  - distinguishable only structurally: their `message.content` is a bare string, never the array
  shape a real typed prompt uses;
* resuming with no new human input gets the CLI's own auto-sent "Continue from where you left off."
  nudge, which **is** flagged `isMeta:true`;
* and a real human prompt ("What?", "List the working directory's contents") sent immediately after
  one of the above must still come through - proof the fix filters specific record shapes, not
  "anything near a compact".

## `agents-*.json` — FEAT-9, `claude agents --json --all`

Both are verbatim output from the real CLI (`--version` prints 2.1.246; the binary's own embedded
`VERSION` constant reads 2.1.251 — the two disagree, and this is the same binary Phase I was
measured against). They exist as a pair because **the field set changes with the session's state**,
and one capture cannot show that:

| file | what it holds |
|---|---|
| `agents-live-background.json` | taken while a background agent was still alive: `pid`, `id`, `cwd`, `kind`, `startedAt`, `sessionId`, `name`, `status`, `state` |
| `agents-all.json` | the same agent after `claude stop`: **no `pid`, no `status`** — the process is gone, so those two simply are not there |

Both also carry two live *interactive* sessions, which have neither `id` nor `status` nor `state`.
So across the two files every optional field appears both present and absent, which is the point:
a parser that requires any of them passes on one file and fails on the other.

The two interactive rows are the sessions that were open on this machine at the time; their ids are
real but long dead. Nothing here is hand-written — that is what makes the "no `pid` after stop"
claim evidence rather than an assumption.

**What `--all` actually does**, measured rather than read: with the agent alive, both `claude agents
--json` and `--json --all` returned it. Only after `claude stop` did the plain form drop it while
`--all` kept it. So `--all` means "include agents whose process has exited".
