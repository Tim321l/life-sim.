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
| 4 | Legacy system (multi-generation) | ⬜ Not started |
| 5 | Avalonia desktop UI | ⬜ Not started |
| 6 | Blazor WASM web UI | ⬜ Not started |

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
