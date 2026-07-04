# Orc — Project Snapshot

_High-level status of the project. Kept intentionally brief._

## What it is

Orc is a terminal app that runs Claude Code against your git repositories as an
autonomous coding orchestrator. You describe work (or let it plan its own), and it
runs Claude in each repo on an isolated branch, commits the result, and merges it back.

## Capabilities

- **Task queue** — enqueue coding tasks against one or more repos; they run in the background.
- **Orchitect** — autonomously analyzes repos against a "mission" and generates multi-step
  enhancement tasks, with per-day quota limits.
- **Repo management** — add/create repos, set missions, per-repo `automerge` toggle.
- **Safe git flow** — each task works on its own `orc-task/*` branch; on success it's
  merged into the base branch (or left for review when automerge is off).
- **Resilience** — tasks interrupted by a crash/shutdown are resumed on restart (same branch,
  same Claude session where possible) rather than lost; cancel and cleanup are supported.
- **Diagnostics** — TUI views for task history, failures (with transcripts), and interrupted
  tasks; persistent file logging.

## How it works (technical)

- **Two projects:** `Orc.Cli` (Spectre.Console TUI + host) and `Orc.Core` (all logic),
  wired with .NET generic-host dependency injection. `Orc.Tests` covers the core.
- **State is files on disk.** `JsonTaskStore` keeps each task as a JSON doc under
  `workspace/data/tasks/<state>/`, moving it between `pending → running → succeeded/failed`
  (plus `interrupted`). This doubles as the crash-recovery record.
- **Background services:** an `OrchestratorService` claims queued tasks and runs them; an
  `OrchitectService` generates tasks. They run alongside the TUI in the same host.
- **The pipeline.** Each task runs a fixed sequence of stages per repo:
  `Refresh → Guard → CreateBranch → RunClaude → Commit → Merge`. Claude is invoked as a
  headless subprocess (prompt over stdin, streamed JSON output).
- **Resume.** As stages complete, a per-repo checkpoint (branch, stage, Claude session id)
  is persisted with the task, so an interrupted run continues from where it stopped.

## Where we are

Core loop works end-to-end: queue → plan → run Claude → branch → commit → merge, with
cancellation, orphan reconciliation, resume, and failure diagnostics in place. Focus has
been on reliability and observability of the run lifecycle.
