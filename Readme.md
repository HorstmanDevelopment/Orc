# Orc

Orchestrator that runs Claude Code across multiple registered git repos. Ships with **Orchitect**, an autonomous enhancement agent that proposes and submits small incremental tasks on its own.

## Layout

- [Orc.Core/](Orc.Core/) — library. Domain logic, no UI.
  - [Configuration/](Orc.Core/Configuration/) — `*Options` bound from `appsettings.json`.
  - [Pipeline/](Orc.Core/Pipeline/) — task execution stages (`IStage`) and the `TaskRunner` that chains them.
  - [Repos/](Orc.Core/Repos/) — repo registry + `GitClient` wrapper.
  - [Tasks/](Orc.Core/Tasks/) — `JsonTaskStore` persists task records as JSON files under `workspace/data/tasks/{running,succeeded,failed}/`.
  - [Claude/](Orc.Core/Claude/) — `ClaudeClient` shells out to the `claude` CLI.
  - [Orchitect/](Orc.Core/Orchitect/) — autonomous enhancement loop (analysis → plan → submit → record).
  - [Process/](Orc.Core/Process/) — `IProcessRunner` abstraction over `System.Diagnostics.Process`.
  - [Hosting/](Orc.Core/Hosting/) — `OrchestratorService` hosted service + DI wiring.
- [Orc.Cli/](Orc.Cli/) — entry point (`orc.exe`). Spectre.Console TUI.
  - [Tui/](Orc.Cli/Tui/), [Forms/](Orc.Cli/Forms/) — dashboard, menus, modal forms.
- [Orc.Tests/](Orc.Tests/) — xUnit. Uses `TempWorkspace` for filesystem-backed tests.
- [docs/orchitect.md](docs/orchitect.md) — user-facing guide for the Orchitect agent.

## Stack

- .NET 10.0, C# with nullable + implicit usings enabled.
- `Microsoft.Extensions.Hosting` for the host, `Microsoft.Extensions.Options` for config.
- Spectre.Console for the TUI. xUnit for tests.
- No DB. State lives under `workspace/data/` as JSON + log files.

## Build / test / run

```powershell
dotnet build
dotnet test
dotnet run --project Orc.Cli       # or: orc (once published)
```

Runtime config: `Orc.Cli/appsettings.json` at dev time, `workspace/config/appsettings.json` at runtime. Override any setting with `ORC_<Section>__<Key>` env vars.

## Conventions

- Interfaces live next to their primary implementation (e.g. `IGitClient` + `GitClient` in [Orc.Core/Repos/](Orc.Core/Repos/)).
- Pipeline stages are small, single-purpose, and composed by `TaskRunner` — add new behavior as a new `IStage` rather than extending an existing one.
- Process invocation goes through `IProcessRunner` so tests can stub it.
- Filesystem state is the source of truth; in-memory caches are rebuilt from disk on startup.
- Task IDs from Orchitect are prefixed `orchitect_<repo>_<enhId>_s<stepN>_<stamp>` — user-submitted tasks don't carry that prefix.
