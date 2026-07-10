# HK Life Sim

A Hong Kong life-simulation game (BitLife-style, text/choice-driven). The player lives
one life per playthrough across HK-specific milestones (DSE, career, housing,
emigration), in one of four eras: `1960s`, `1980s`, `2000s`, `2024plus`. On death, a
Legacy record carries assets/flags to the next generation. Core logic is a pure C#
library shared by an Avalonia desktop UI and a Blazor WebAssembly web UI.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned in `global.json`)

## Build & test

```bash
dotnet restore
dotnet build -warnaserror
dotnet test
```

## Run

```bash
dotnet run --project src/HKLifeSim.Cli -- --era 2024plus --seed 42 --auto
dotnet run --project src/HKLifeSim.Cli -- --validate-content
```

`--era` accepts `1960s`, `1980s`, `2000s`, or `2024plus`. Omit `--auto` to play
interactively (numbered choices read from stdin). Event money effects are authored at
2024plus baseline and automatically scaled per era via `InflationScaler`.

Add `--generations N` (auto mode) to play a multi-generation family lineage in the same
era, printing an inheritance summary after each life. In interactive mode, you're
prompted "開展下一代?" after each death instead.

## Desktop app

```bash
dotnet run --project src/HKLifeSim.Desktop
```

Saves to `%APPDATA%/HKLifeSim/autosave.json` (Windows) after every choice; relaunching
and choosing 繼續上次 on the Setup screen resumes the exact saved state.

## Solution layout

```
src/
  HKLifeSim.Core/      pure game logic, zero UI dependencies
  HKLifeSim.Cli/        console runner (dev/test harness)
  HKLifeSim.Desktop/    Avalonia desktop client
  HKLifeSim.Web/        Blazor WebAssembly client
data/                   era/event JSON content
tests/
  HKLifeSim.Core.Tests/ xUnit v3 test suite
```

## Branch & release workflow

- One branch per phase: `phase-0` … `phase-6`, PR to `main`, merge only once CI is green.
- Commit style: [Conventional Commits](https://www.conventionalcommits.org/)
  (`feat:`, `fix:`, `test:`, `content:`).
- Tag `v0.{phase}` at each phase merge for clean rollback points.

## Phase status

| Phase | Description | Status |
|---|---|---|
| 0 | Repository, CI & quality gates | ✅ Done |
| 1 | Core domain + persistence | ✅ Done |
| 2 | Event engine + JSON schema + 2024plus pool + CLI runner | ✅ Done |
| 3 | Multi-era pools + inflation scaling | ✅ Done |
| 4 | Legacy system (multi-generation) | ✅ Done |
| 5 | Avalonia desktop UI | ✅ Done |
| 5.5 | Activity & stamina system (Core + CLI) | ✅ Done |
| 6 | Blazor WASM web UI | ✅ Done (see note below) |
| 7 | Digivice character widget (Desktop + Web) | ⬜ Not started |

**Note on Phase 6**: Setup/Game/Obituary are all fully built (character creation,
era selection, the full annual event loop, a 6-tab action dashboard, financial
ledger, NPCs/children/careers, a live SVG radar chart, and `localStorage`-backed
autosave with working resume-on-refresh). However, `HKLifeSim.Web`'s "active
action" gameplay (career, hobbies, relationships, finances) is implemented
directly in `GameSessionService.cs` rather than through `HKLifeSim.Core` — only
the yearly random-event pipeline (`EventEngine`/`LifecycleSystem`/`LegacySystem`)
goes through Core. This means Web and CLI/Desktop run two different rule sets
for "active actions"; in particular, Web's own Activities/Hobbies tabs predate
and do not use the Phase 5.5 `HKLifeSim.Core.Activities.ActivityManager`. This
is a known, working design choice, not a defect — flagged here so it isn't
mistaken for missing work in a future phase.

## Quality gates

- `Directory.Build.props`: nullable enabled, warnings-as-errors, `AnalysisLevel=latest-all`.
- `Directory.Packages.props`: central package version management — csproj files must
  not pin versions.
- `HKLifeSim.Core` must never reference Avalonia, Blazor, `System.Console`, or any
  UI/IO framework.
- CI enforces build, tests, and (from Phase 1) an 85% line-coverage gate on
  `HKLifeSim.Core`, gated by the `COVERAGE_GATE` repository variable.
- From Phase 2, CI also runs content validation (`--validate-content`) and a
  simulation fuzz job across all eras and seeds 1–50.
