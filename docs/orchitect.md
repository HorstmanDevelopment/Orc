# Using Orchitect

Orchitect is an autonomous enhancement agent that runs alongside the Orc orchestrator. For every repo you've registered, it identifies improvements, plans them as small incremental steps, and submits one step at a time as a normal task for the orchestrator to execute. Modifications are throttled by a daily quota.

This guide covers day-to-day operation.

---

## 1. What Orchitect does

For each registered repo, in a loop:

1. **Analyze** — runs Claude in read-only mode (`Read`, `Glob`, `Grep`) to identify 3–7 enhancement candidates.
2. **Plan** — picks the highest-priority candidate, runs Claude again to plan **one focused incremental step**.
3. **Submit** — enqueues that step as a task. The orchestrator picks it up, creates a branch (`orc-task/<id>`), runs Claude in write mode, and commits.
4. **Record** — on completion, marks the step as Completed or Failed. If the step committed changes, the daily quota counter ticks up. If two steps fail in a row, the enhancement is Abandoned.
5. Repeat until the daily quota is hit, then sleep until UTC midnight.

You don't trigger anything. Orchitect starts with the host process and keeps going.

---

## 2. Starting and stopping

Orchitect runs in-process with the orchestrator. Launch the app:

```
orc
```

It starts both services. Hit `Quit` from the main menu to stop both.

To stop just Orchitect (keep the orchestrator and TUI running), use **Pause** — see §5.

---

## 3. Configuration

Edit `workspace/config/appsettings.json`, section `"Orchitect"`:

| Setting | Default | What it does |
|---|---|---|
| `MaxModificationsPerDay` | 5 | Hard cap on committed-change tasks across all repos per UTC day. |
| `MaxModificationsPerRepoPerDay` | 2 | Per-repo cap. Both caps must allow before a step is submitted. |
| `ConsecutiveFailureLimit` | 2 | Failed steps in a row before the current enhancement is Abandoned. |
| `ReadOnlyTools` | `["Read","Glob","Grep"]` | Tools the analysis + planner Claude calls are allowed to use. |

Restart the app for changes to take effect. There's no hot-reload.

Override at runtime via environment variable (any setting):

```powershell
$env:ORC_Orchitect__MaxModificationsPerDay = "10"
orc
```

---

## 4. Daily quota

What counts as a modification: a task **submitted by Orchitect** that finishes in `Succeeded` state **and committed at least one change**. Failed runs, refresh failures, and no-op Claude runs (no diff) do not count.

The counter is per UTC day. At UTC midnight it resets automatically — the loop wakes up in hourly chunks while sleeping, so the next-day pickup happens within an hour of midnight.

You can see today's tally in the TUI: **Maintenance → Orchitect status** shows global and per-repo counts.

The counter file is `workspace/data/orchitect/quota.json`. Don't edit it while the app is running.

---

## 5. Pause and resume

From the main menu: **Maintenance and System info → Pause Orchitect** (label flips to **Resume Orchitect** while paused).

Mechanism: a flag file at `workspace/data/orchitect/paused`. Each repo loop checks for it every 10 seconds. Pausing does **not** kill an in-flight step — the orchestrator finishes whatever it claimed. The loop won't submit anything new until you resume.

You can also pause externally by creating the flag file by hand; deleting it resumes. The contents don't matter.

---

## 6. Status panel

**Maintenance → Orchitect status** shows:

- A header line: paused state, current UTC date, total modifications today.
- One panel per repo with a table of enhancements (ID, priority, status, steps summary, title).

Enhancement statuses:
- `Identified` — produced by analysis, not yet started.
- `InProgress` — at least one step submitted; more may follow.
- `Completed` — planner indicated the work is done, or no further step is needed.
- `Abandoned` — hit the consecutive-failure limit.

Steps summary format: `<total> total, <n> ok, <n> fail, <n> active`.

---

## 7. State files (where things live)

```
workspace/data/orchitect/
  paused                            ← pause flag (presence = paused)
  quota.json                        ← daily counter
  repos/<repoName>/
    enhancements.json               ← per-repo state (the source of truth)
    analysis.md                     ← raw Claude analysis transcript
    history.log                     ← timeline of submissions / outcomes
```

These are JSON / plain text. Safe to read while the app runs; **only edit while the app is stopped** to avoid races.

---

## 8. Common operations

### Force a fresh analysis for a repo

Stop the app. Edit `workspace/data/orchitect/repos/<repo>/enhancements.json`:

- Set every `Identified` enhancement's `Status` to `"Abandoned"`.
- Set `LastAnalyzedUtc` to `null`.

Restart. Next loop iteration the repo has no active candidates → triggers analysis.

(A built-in "Re-analyze repo" TUI action is on the roadmap; today this is the manual path.)

### Reset today's quota

Stop the app, delete `workspace/data/orchitect/quota.json`, restart. The counter is fresh at zero for today.

### Skip a stuck enhancement

Stop the app. Edit `enhancements.json` for that repo, set the enhancement's `Status` to `"Abandoned"`. Restart.

### Stop a runaway repo entirely

Stop the app. Edit `enhancements.json` and mark every enhancement `Abandoned`, then pause Orchitect (`touch workspace/data/orchitect/paused`) before restarting. The repo loop will idle and not re-analyze until you resume.

### See the raw Claude output for the latest analysis

`workspace/data/orchitect/repos/<repo>/analysis.md` — that's the verbatim transcript from the most recent analysis call (including Claude's stdout and stderr).

### Inspect what Orchitect submitted

Orchitect-submitted task IDs are prefixed `orchitect_<repo>_<enhId>_s<stepN>_<stamp>`. Find them in:

- `workspace/data/tasks/{running,succeeded,failed}/<id>.json` — the task record (state + outcome + prompt).
- `workspace/data/artifacts/<id>/<repo>.log` — the full per-repo pipeline transcript (git refresh, branch, Claude run, commit).
- `workspace/data/orchitect/repos/<repo>/history.log` — Orchitect's per-repo timeline.

---

## 9. How Orchitect interacts with manual tasks

User-submitted and Orchitect-submitted tasks share the same queue. The orchestrator runs them one at a time in FIFO order. There's no special priority for either source.

Two cautions:

- The orchestrator refuses to start a task on a repo if a prior `orc-task/*` branch is still un-merged on `BaseBranch`. If you don't merge Orchitect's PRs / branches, its next step on that repo will fail the guard and count as a failed step (which can lead to abandonment).
- If you submit a manual task for a repo *while* Orchitect's step is in flight on the same repo, one of them loses the guard race and fails. Pause Orchitect before doing focused manual work on a repo.

---

## 10. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| No enhancements ever appear | Claude binary not found, or analysis hung. | Check the console logs for `Claude in <repo> exit=...`. Ensure `claude` is on PATH (or set `Claude.BinaryHint` in `appsettings.json`). |
| Analysis runs but `enhancements.json` stays empty | Claude returned prose instead of JSON. | Open `analysis.md` — if the model wrapped JSON in markdown fences or added commentary, our extractor is tolerant of fences but not of prose-only responses. Force a re-analysis. |
| Quota counter never increments | Steps run but commit nothing (no changes), or steps fail. | Open the per-repo artifact log for the latest task — look for `--- commit success=... hasChanges=... ---`. |
| Same enhancement keeps getting picked but never finishes | Planner is producing too-broad steps that fail to commit changes. | Manually abandon and let analysis propose a fresh angle. |
| Orchitect won't pick up changes to `appsettings.json` | Settings are read at startup. | Restart the app. |

---

## 11. Limitations to know about

- **Per-step planning only.** Each step is planned independently with a fresh Claude call. The planner doesn't see Claude's own commit history beyond what's already on disk in the working tree.
- **No merge wait.** Orchitect proceeds to the next step as soon as the prior one's branch is committed, not when it's merged into `BaseBranch`. If you batch-merge later, subsequent steps may plan against state that hasn't seen the prior work yet.
- **Single-repo per task.** Orchitect never submits multi-repo tasks. One enhancement step modifies one repo.
- **UTC-only quota rollover.** The day boundary is UTC midnight, not your local midnight.
- **No interactive prompts.** Analysis and planning are fully autonomous. If you want input on what gets worked on next, abandon what you don't want and let the planner pick another.

---

## 12. Quick reference

```
Pause                 → Maintenance → Pause Orchitect
Resume                → Maintenance → Resume Orchitect
View status           → Maintenance → Orchitect status
Daily caps            → workspace/config/appsettings.json → "Orchitect"
Per-repo state        → workspace/data/orchitect/repos/<repo>/enhancements.json
Today's counter       → workspace/data/orchitect/quota.json
Analysis transcript   → workspace/data/orchitect/repos/<repo>/analysis.md
Timeline              → workspace/data/orchitect/repos/<repo>/history.log
```
