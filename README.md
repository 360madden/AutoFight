<p align="center">
  <img src="docs/assets/autofight-hero.svg" alt="AutoFight — RIFT telemetry, navigation, and control research by 360madden" width="100%" />
</p>

<div align="center">

[![Platform: Windows 11](https://img.shields.io/badge/Windows-11%20x64-0078D4?style=for-the-badge&logo=windows11&logoColor=white)](#-quick-start)
[![Framework: .NET 10](https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](#-architecture)
[![Language: C#](https://img.shields.io/badge/C%23-WPF-239120?style=for-the-badge&logo=csharp&logoColor=white)](#-architecture)
[![Lua addon](https://img.shields.io/badge/RIFT-Lua%20addon-2C2D72?style=for-the-badge&logo=lua&logoColor=white)](#-architecture)
[![State: research preview](https://img.shields.io/badge/Status-Research%20Preview-F2A900?style=for-the-badge)](#-verified-status)

**An independent RIFT observation and automation workbench**

*From source-verified telemetry toward measured movement, navigation, and eventually combat.*

[What works](#-verified-status) · [Architecture](#-architecture) · [Quick start](#-quick-start) · [Roadmap](#-next-milestones) · [Developer handoff](SESSION_HANDOFF.md)

</div>

> [!IMPORTANT]
> **AutoFight is an active development project—not a finished autonomous RIFT player.** On November 15, its own current-build native reader obtained real XYZ candidates from physical RIFT on AMD-DESKTOP without any RR2 service or gameplay input. The first/default desktop tab now supports explicit read-only native XYZ sampling and baseline/coordinate-difference comparisons. **The first-tab WPF observer now also retains up to 600 pinned native-read outcomes and offers explicit no-overwrite JSON trace export.** Passive producer freshness, matching VMware addon telemetry, sustained 480-second qualification, a user-observed continuous movement trace, and measured game movement remain unverified. The earlier Lab 2 Reader-to-host transport alone is not game data.

## ✨ What is AutoFight?

AutoFight is a modular **C# / .NET 10 / WPF** Windows project exploring a reliable, observable path from RIFT game state to independently verified actions. The physical Windows RIFT client on AMD-DESKTOP is the current read-only XYZ target; earlier VMware lab integrations remain available as separate historical/development paths. The design emphasizes **one clear owner for input**, explicit game/session identities, bounded operations, and evidence that an action actually changed the world.

| 🧩 Component | Purpose | Current implementation |
| :--- | :--- | :--- |
| 🖥️ **Desktop workbench** | On-demand physical RIFT XYZ, identity-pinned comparisons, VM diagnostics and replay | First/default physical XYZ WPF tab and CLI commands |
| 🧠 **Core engine** | Typed observations, freshness, event continuity, deterministic analysis | Standalone .NET library with regression tests |
| 📡 **Host receiver** | Accept authenticated, identity-bound updates from a guest | Independent AutoFight in-process listener and watch CLI |
| 📍 **Physical native XYZ** | Read current-build RIFT memory, classify consecutive coordinates | Exact-process C# read-only reader + pure comparison tracker; no game input |
| 🕵️ **GuestReader** | Inspect the pinned RIFT process for addon-produced frames | AutoFight-owned Windows executable and scanner |
| 🌙 **RIFT Lua addon** | Emit identifiable game observations within permitted addon APIs | Six source-controlled Lua assets |
| 🕹️ **Control foundation** | Preflight, ownership, bounded pulse and response attribution | Legacy supervised bridge and synthetic executor; *no verified game motion* |

**Project boundary:** AutoFight owns its source code, tests, addon, GuestReader, host receiver, and independent build products. [RiftReader2](https://github.com/360madden/RiftReader2), the historical RiftReader projects, and other research repositories are references or optional *development* integrations—not required source checkouts for the owned data path. Some older desktop diagnostic/control UI still supports an optional legacy RR2 bridge while migration continues.

## 🚦 Verified status

<sub>Evidence snapshot: November 15 physical native XYZ; November 6–7 VMware evidence remains historical. Revalidate process identities on every use.</sub>

| Area | Status | Evidence / limitation |
| :--- | :---: | :--- |
| .NET 10 solution + WPF smoke | 🟢 | Release build and deterministic offline test suites previously passed |
| AutoFight-owned addon + GuestReader | 🟢 | Built independently of RR2 assemblies; six addon files verified on Lab 2 disk |
| Guest → host transport (Lab 2) | 🟢 | Authenticated Reader deliveries observed in a bounded live watch |
| In-game addon load and private binding | 🟡 | On-disk assets verified; active addon/bound nonce not yet established |
| Fresh XYZ from matched addon frame | 🔴 | 40/40 host polls unavailable; Reader reported no matching frame |
| 480-second continuous telemetry | 🔴 | Acceptance logic exists; no AutoFight-owned live passing result |
| Actual controlled movement with XYZ change | 🔴 | No verified input-to-position feedback loop |
| Physical native read-only XYZ provider | 🟢 | AutoFight-owned reader observed two different XYZ positions ~55.84 units apart under unchanged game/player identity; later 4/4 samples matched the new position. Value-change observed, producer freshness and action cause unqualified |
| Physical XYZ WPF and coordinate comparisons | 🟢 | First/default on-demand tab with sticky change count, last 20 timestamped deltas, bounded 600-outcome trace, and M5 A/B repeated-coordinate assessment under pinned source; 7 synthetic smoke categories passed, visible click-through pending |
| Physical XYZ headless CLI recorder | 🟢 | M7 explicit 10–120s exact-PID/start read-only capture, ~1 Hz, new no-overwrite JSON, M5 A/B assessment, no game input; 10 deterministic tests and independent review pass; Joey-operated live A→B capture pending |
| AFT2 target-health diagnostic | 🟡 | Owned Lua/GuestReader/physical-host candidate path retains identity, HP and provenance; synthetic-only, never qualified for combat |
| Targeting, navigation, spell rotation | 🔴 | Future work; no qualified autonomous combat |

**Interpreting the evidence:** A network-delivered Reader status is **not** necessarily a game telemetry frame. A game input acknowledgment is **not** proof the character moved. The project deliberately records these as separate acceptance gates.

## 🏗️ Architecture

```mermaid
flowchart TD
    G["🎮 RIFT in VMware Lab"] --> L["🌙 AutoFight Lua addon"]
    L --> R["🔎 AutoFight GuestReader"]
    R -->|"Authenticated, pinned observations"| H["📡 AutoFight host receiver"]
    H --> V["🖥️ WPF / diagnostic CLI"]
    V --> C["🧠 Core continuity + evidence rules"]
    C -. "Future qualified feedback loop" .-> A["🕹️ Bounded input controller"]
    A -. "Only after explicit readiness" .-> G
    P["🎮 Physical RIFT on AMD-DESKTOP"] --> N["🧭 Native XYZ reader"]
    N -->|"Read-only candidate and coordinate deltas"| V
```

- **Process and build identity:** measurements must match the correct game image, process start, lab, and Reader instance.
- **Explicit freshness:** advancing transport is tracked separately from advancing producer data and accepted XYZ.
- **Standalone runtime direction:** the owned telemetry path has no RiftReader2 project reference or RR2 runtime service requirement; legacy UI/controller migration is still ongoing.
- **Operational safety:** input actions have explicit bounds, cancellation and ownership; invalid, stale, or uncertain observations fail closed.

See the [architecture decision record](docs/automation-platform-architecture.md) for contracts and the [technical roadmap](docs/automation-platform-roadmap.md) for acceptance gates.

## 🚀 Quick start

### Requirements

- Windows 11 x64
- **.NET 10 SDK**, matching the repo's `global.json` (SDK feature band `10.0.302`, newer patch permitted)
- PowerShell and Git
- VMware and RIFT are **not required** for builds, offline tests, or WPF smoke checks

From **Windows PowerShell**, inside a clone of this repository:

```powershell
git clone https://github.com/360madden/AutoFight.git
Set-Location .\AutoFight
dotnet build .\Rift.Automation.slnx -c Release
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release
```

### 🧪 Tests

Run from the repository root in Windows PowerShell:

```powershell
dotnet run --project .\tests\Rift.Automation.Specs\Rift.Automation.Specs.csproj -c Release -- selftest
dotnet run --project .\tests\Rift.Automation.Windows.Specs\Rift.Automation.Windows.Specs.csproj -c Release
dotnet run --project .\tests\Rift.Automation.Guest.Specs\Rift.Automation.Guest.Specs.csproj -c Release
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --smoke-test
```

Tests exercise synthetic/offline cases. A passing smoke test does **not** establish a visible desktop experience or a successful live game input.

### RIFT game-update compatibility preflight (offline)

Before and after a RIFT client update, compare a **caller-specified executable file** against AutoFight's exact-build native XYZ fingerprint without opening RIFT or scanning game memory:

```powershell
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --physical-build-check "C:\Program Files (x86)\Glyph\Games\RIFT\Live\rift_x64.exe"
```

This disk-only command emits redacted JSON: `MatchesPinnedBuild` (exit 0), `RequiresRequalification` (exit 2), or an unavailable/invalid state (exit 2). Its read has a 30-second command deadline and never updates the embedded SHA or native offsets. Even an exact file match is **not** live telemetry qualification or combat authority. See the [2026-11-17 pre-update baseline and post-update checklist](docs/GAME-UPDATE-READINESS-2026-11-17.md).

### Native XYZ diagnostic (physical RIFT)

The new standalone reader is **read-only**. Run `--clients` first to obtain the exact PID and `Identity` (`PID:process-start-UTC-ticks`), then use those values in the native commands:

```powershell
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --clients
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --physical-xyz <pid> <start-utc-ticks>
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --physical-xyz-watch <pid> <start-utc-ticks> 5
```

To recheck an explicitly saved M4 trace without opening RIFT or the WPF window, run `dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --physical-xyz-trace-assess "C:\path\to\saved-trace.json"`. The command validates canonical redacted JSON under 1 MiB and reports the typed M5 A/B assessment with **no producer-freshness or movement-attribution claim**; a real saved trace remains required. See the [M6 replay receipt](docs/M6-PHYSICAL-XYZ-OFFLINE-TRACE-ASSESSMENT-2026-11-15.md).

For a **headless user-operated A→B capture**, run `--clients` to obtain the current exact physical PID and process-start UTC ticks, then invoke `--physical-xyz-trace-capture <pid> <start-utc-ticks> 30 "C:\work\AutoFight\manual-xyz-capture-001.json"` from the same Release desktop CLI. The target must be a **new .json** in an existing local directory; the recorder never overwrites. Remain still for three A readings, **manually move in RIFT**, then remain still for three distinct B readings. The command records read-only XYZ while the game stays focused, and can be replayed with M6. Exit 0 means a completed saved capture, not proven movement; interruption/error exits 2 and any partial history remains explicitly incomplete. See the [M7 CLI capture receipt](docs/M7-PHYSICAL-XYZ-CLI-CAPTURE-2026-11-15.md).

The executable's SHA-256 must exactly match the pinned current-build recipe. An accepted `Candidate` means an anchored, newly acquired memory reading—not independently proven producer freshness, movement, or permission to automate. The watch is explicitly bounded, emits **Baseline/Unchanged/Changed** coordinate comparisons and 3D deltas, and never controls RIFT.

For the on-demand desktop observer, launch AutoFight without CLI arguments. In the first tab **Physical RIFT – Live XYZ**, click **Refresh physical RIFT clients**, select the exact RIFT process, then **Start read-only XYZ**; use **Stop sampling** to cancel. The desktop acquires about once per second, clears stale or invalid **live** coordinates and never moves or focuses the game.

For a user-driven movement test, wait for a few stable readings, manually focus RIFT, move to a different position, **stay there for 3–5 seconds**, then return to AutoFight. The **Retained coordinate-change evidence** panel preserves the total change count and latest 20 UTC-stamped coordinate differences, including 3D distances and the original PID/start/player identity, even after Stop; select text and press Ctrl+C to copy. The separate **Read-only XYZ trace** section retains up to **600 completed native acquisition outcomes** (accepted, rejected, unavailable), including elapsed times and source identity, and the **Save read-only trace** button explicitly exports historical JSON to a newly selected file without overwriting an existing file. The shared writer is covered by Windows regression tests for a complete 600-entry JSON roundtrip, unsafe filename rejection and exact-byte no-overwrite behavior. **M5** adds an on-screen read-only assessment of the trace: wait for at least three equal A readings, manually change position and wait for three equal B readings. It then shows the observed A/B coordinates, UTC times and 3D distance, or typed interruption/incomplete reasons. This never proves the game producer advanced or that AutoFight caused movement. These are historical *value-change* observations, not proof of producer freshness or an AutoFight-controlled move. Synthetic smoke has passed; a visible interactive click-through remains outstanding. See [M1 first native reading](docs/M1-PHYSICAL-NATIVE-XYZ-2026-11-15.md), [M2 live tracker](docs/M2-PHYSICAL-XYZ-CONTINUITY-UI-2026-11-15.md), [M3 retained movement-test evidence](docs/M3-PHYSICAL-XYZ-CHANGE-HISTORY-2026-11-15.md), [M4 bounded read-only XYZ trace](docs/M4-PHYSICAL-XYZ-TRACE-2026-11-15.md), [M5 A/B trace assessment](docs/M5-PHYSICAL-XYZ-AB-ASSESSMENT-2026-11-15.md), [M6 offline saved-trace replay](docs/M6-PHYSICAL-XYZ-OFFLINE-TRACE-ASSESSMENT-2026-11-15.md), and [M7 headless physical XYZ capture](docs/M7-PHYSICAL-XYZ-CLI-CAPTURE-2026-11-15.md).

### 📦 Optional self-contained desktop build

```powershell
dotnet publish .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false -o .\artifacts\local\autofight-win-x64
```

This creates a local unsigned executable—not a GitHub release or installer. The separately built GuestReader and private lab configuration are only needed for advanced, explicitly configured live observation. **Do not commit local tokens, reader nonces, or host/lab configuration files.** See [GuestReader notes](docs/M1-standalone-guest-port-2026-10-31.md).

## 🗺️ Next milestones

| Priority | Milestone | Acceptance target |
| :---: | :--- | :--- |
| **P1** | First trustworthy Lab 2 XYZ | Prove addon loaded/bound and host receives fresh, identity-matched position |
| **P2** | Physical XYZ comparison and freshness qualification | Two distinct positions and new sticky desktop history verified Nov 15; next capture a continuous **user-driven** movement event, visible UI behavior and independent producer freshness |
| **P3** | Freshness and movement verification | Sustain the 480-second window; separately observe one reviewed small movement pulse |
| **P4** | Standalone input path | Remove the remaining legacy controller runtime requirement |
| **P5** | Navigation and combat | Add target/ability state, movement planning, collision/LOS and recovery only on qualified feedback |

## 📚 Project documentation

| Document | What you'll find |
| :--- | :--- |
| [Session handoff](SESSION_HANDOFF.md) | Most recent continuation links and repository routing |
| [Current status](docs/current-status.md) | Verified implementation, test receipts, and outstanding gates |
| [M3 retained XYZ-change evidence](docs/M3-PHYSICAL-XYZ-CHANGE-HISTORY-2026-11-15.md) | Latest source-pinned coordinate difference, bounded 20-event history, user movement-test steps |
| [Live physical XYZ continuity + WPF milestone](docs/M2-PHYSICAL-XYZ-CONTINUITY-UI-2026-11-15.md) | Latest 4/4 read-only watch, comparison semantics, 333 tests and desktop scope |
| [Lab 2 first-XYZ handoff](docs/HANDOFF-AUTOFIGHT-LAB2-FIRST-XYZ-2026-11-06.md) | Exact source/Reader transport outcome and next lab steps |
| [Architecture](docs/automation-platform-architecture.md) | Contracts, identity, state and provider arbitration |
| [Roadmap](docs/automation-platform-roadmap.md) | Development milestones and acceptance gates |
| [Storage Guardian](docs/STORAGE-GUARDIAN.md) | Independently reviewed repository storage audit, optional local history, safe cleanup preview and guarded manual generated-file cleanup |
| [Developer instructions](AGENTS.md) | Project ownership, engineering and build requirements |

---

<div align="center">

**AutoFight** · Created and maintained by **[360madden](https://github.com/360madden)**

<sub>Built to turn telemetry into verifiable behavior—one measured milestone at a time.</sub>

</div>
