# CLAUDE.md

Guidance for Claude Code when working in this repository. See also [Readme.md](Readme.md) (layout/conventions) and [snapshot.md](snapshot.md) (status).

## What Orc is

Orc is a terminal app (Spectre.Console TUI) that runs **Claude Code** against your registered git repositories as an autonomous coding orchestrator. You enqueue coding tasks (or let it plan its own), and for each it runs Claude in the target repo on an isolated `orc-task/*` branch, commits the result, and merges it back into the base branch.

It ships with **Orchitect**, an autonomous agent that analyzes each repo against a per-repo "mission" and generates multi-step enhancement tasks on its own, subject to per-day quota limits.

## Stack

- **.NET 10.0**, C# with `Nullable` + `ImplicitUsings` enabled (all three projects).
- `Microsoft.Extensions.Hosting` generic host + DI; `Microsoft.Extensions.Options` for config.
- **Spectre.Console** (0.49.1) for the TUI. **xUnit** for tests.
- No database. All state is JSON + log files under `workspace/data/` (gitignored).
- Packages are pinned at 9.0.0 for the MS.Extensions.* family even on net10.0 — keep them consistent when adding references.

## Projects

- **`Orc.Core/`** — all domain logic, no UI. Assembly `Orc.Core`.
  - `Configuration/` — `*Options` records bound from `appsettings.json`.
  - `Pipeline/` — task execution `IStage`s + `TaskRunner` that chains them.
  - `Repos/` — repo registry + `GitClient` wrapper.
  - `Tasks/` — `JsonTaskStore`, `TaskRecord`, resume/running-task registries.
  - `Claude/` — `ClaudeClient` shells out to the `claude` CLI (embeds `claude-settings.json`).
  - `Orchitect/` — autonomous enhancement loop (analysis → plan → submit → record).
  - `Process/` — `IProcessRunner` abstraction over `System.Diagnostics.Process`.
  - `Hosting/` — `OrchestratorService` + `OrchitectService` hosted services, `AddOrcCore` DI wiring.
- **`Orc.Cli/`** — entry point, produces `orc.exe` (AssemblyName `orc`). `Tui/` (Dashboard, panels) + `Forms/` (modal forms). `Program.cs` builds the host and adds the file logger + TUI hosted service.
- **`Orc.Tests/`** — xUnit tests for Core. Uses a `TempWorkspace` helper for filesystem-backed tests.

## Build / test / run

```powershell
dotnet build
dotnet test
dotnet run --project Orc.Cli        # or ./run.sh (bash) — suppresses host log noise on the TUI splash
```

- `run.sh` sets `ORC_Logging__*=Warning` env vars so DI/startup logs don't pollute the TUI. Prefer it for interactive runs.
- To run a single test: `dotnet test --filter "FullyQualifiedName~JsonExtractorTests"`.
- Runtime config: `Orc.Cli/appsettings.json` at dev time, `workspace/config/appsettings.json` at runtime.
- Override any setting with env vars prefixed `ORC_`, using `__` for the `:` section separator (e.g. `ORC_Claude__PermissionMode=acceptEdits`).

## How it works

- Two hosted background services run alongside the TUI in one host: `OrchestratorService` claims queued tasks and runs them; `OrchitectService` generates new tasks.
- **The pipeline** — each task runs a fixed stage sequence per repo:
  `Refresh → Guard(unmerged branch) → CreateBranch → RunClaude → Commit → Merge`.
  Claude runs as a headless subprocess (prompt over stdin, streamed JSON output parsed by `JsonExtractor`).
- **State is files on disk** — `JsonTaskStore` keeps each task as a JSON doc under `workspace/data/tasks/<state>/`, moving it `pending → running → succeeded/failed` (plus `interrupted`). This doubles as the crash-recovery record. In-memory caches are rebuilt from disk on startup.
- **Resume** — as stages complete, a per-repo checkpoint (branch, stage, Claude session id) is persisted with the task, so an interrupted run continues where it stopped (controlled by the `Resume` config section).
- **Orchitect** — see [docs/orchitect.md](docs/orchitect.md) for the full guide. Generated task IDs are prefixed `orchitect_<repo>_<enhId>_s<stepN>_<stamp>`; user-submitted tasks have no such prefix.

## Conventions

- Interfaces live next to their primary implementation (`IGitClient` + `GitClient` in `Repos/`).
- Pipeline stages are small and single-purpose — add new behavior as a **new `IStage`** rather than extending an existing one; wire it into `TaskRunner`.
- All external process invocation goes through `IProcessRunner` so tests can stub it — don't call `Process` directly.
- Filesystem state is the source of truth.
- Match the surrounding code's style; nullable is on, so honor nullability annotations.
