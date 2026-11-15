# AutoFight M2 — Physical RIFT live XYZ continuity and desktop observer

> **Later same-day successor:** [M3 retained coordinate-change history](M3-PHYSICAL-XYZ-CHANGE-HISTORY-2026-11-15.md) observed a different pinned physical XYZ across timed snapshots and added bounded, source-labeled WPF change history that survives Stop. This M2 receipt remains the earlier live comparison/desktop milestone; see M3 for the latest operator test procedure.

**Date:** November 15, 2026 (America/New_York)  
**Machine:** AMD-DESKTOP, verified through Workbridge  
**Repository:** `C:\work\AutoFight`, branch `main`  
**Pre-milestone HEAD:** `e5870a1ec6722ca13c28a0a5951dec9400c0199b`  
**Application:** C# / .NET 10 WPF, version `0.5.7`  
**Scope:** Read-only physical RIFT native position comparison and on-demand desktop presentation.

**Outcome:** **PASS** — bounded, pinned native position reacquisition and deterministic coordinate comparison; 7 nonshown WPF smoke categories. **NOT PASSED:** passive producer freshness, visible WPF click-through, eight-minute live qualification, verified player movement or input feedback. No game input, focusing, addon reload, VMware operation or RR2 runtime call occurred.

## 1. Implemented

- `src/Rift.Automation.Windows/PhysicalRiftPositionChangeTracker.cs` — pure .NET position comparison. Pins exact PID, process-start UTC ticks, current executable SHA-256 and player unit key; retains a single baseline; uses caller-provided monotonic elapsed time. Reports `Baseline`, `Unchanged`, `Changed`, `Unavailable`, `InvalidCandidate`, `IdentityChanged`, `OutOfOrder`, `StaleAcquisitionGap`, `Cancelled`, or `LimitReached`. Reports absolute coordinate differences and Euclidean 3D distance for confirmed *value changes*. The 4,500-ms acquisition gap is **not** game-producer freshness evidence. Resets on invalid/unavailable data, uses a bounded sample count and never authorizes automation.
- `src/Rift.Automation.Desktop/App.PhysicalXyzWatch.cs` — 2–30-second CLI emits `coordinateComparison` per native sample with status, absolute axis deltas, 3D distance and `gameplayMovementProven=false`; summary includes change count and continuity faults. Identity or continuity faults terminate the watch; unavailable/errors cannot produce a passing candidate sequence. Terminal error reasons distinguish identity, gap, unavailable and read errors.
- `src/Rift.Automation.Desktop/MainWindow.xaml` and `MainWindow.PhysicalXyz.cs` — the first/default tab is **Physical RIFT – Live XYZ**. Explicit **Refresh → select process → Start read-only XYZ → Stop**; no automatic process selection or sampling. Start rescans selected PID/start/path; native reader revalidates the executable hash, game process and player. ~1-Hz sequential background acquisitions show XYZ, capture age, Baseline/Unchanged/Changed comparisons and 3D deltas. A position is displayed only while recent, identity-pinned and valid; Stop, close or faults remove stale coordinates. It is separate from legacy VMware UI and does not request gameplay input.
- `MainWindow.Clients.cs` hooks physical watch cancellation before window teardown. `App.xaml.cs` adds physical-panel synthetic checks to `--smoke-test`.
- `tests/Rift.Automation.Windows.Specs/PhysicalRiftPositionChangeSpecs.cs` plus harness registration cover stationary and changed values, missing/invalid readings, wrong process/player/build identity, ordering and gaps, cancellation and bounded limits.

## 2. Fresh real read-only verification

At **21:16:53 UTC / 4:16:53 PM EST**, AutoFight `--clients` returned one detected physical `rift_x64.exe`:

| Identity | Verified value |
| --- | --- |
| Computer | `AMD-DESKTOP` |
| PID / process UTC-start ticks | `37928` / `639303416167439797` |
| Window / session | `RIFT` / Windows session 1 |
| Current executable SHA-256 | `1935A05C9DB6D468E34E0055014A4A99C1B5AFCEB1695884E24600F5C20A4BE8` |
| Player unit key | `239816685251645959` |
| Acquired XYZ | **X=7191.6084, Y=873.9848, Z=3019.1848** |

At **21:19:15–21:19:19 UTC / 4:19 PM EST**, job `af-physical-xyz-m2-live-watch-20261115-01`, exit **0**, ran a requested four-second native watch:

| Native sample | Comparison | Read duration | X / Y / Z |
| --- | --- | ---: | --- |
| 1 | **Baseline** | 103.5779 ms | 7191.6084 / 873.9848 / 3019.1848 |
| 2 | **Unchanged**, Δ3D 0 | 82.5933 ms | same |
| 3 | **Unchanged**, Δ3D 0 | 81.7630 ms | same |
| 4 | **Unchanged**, Δ3D 0 | 83.6632 ms | same |

Summary: 4/4 candidates, 0 unavailable, 0 identity faults, 0 errors, 0 continuity faults, 0 observed coordinate-value changes, duration 4005.57 ms, `candidate-sequence-observed`. All reported `passiveFreshnessQualified=false`, `sustainedQualification=false`, `canAutomate=false`, `gameplayMovementProven=false`. There was no character movement or new independent producer timestamp during this watch; unchanged XYZ does **not** establish producer freshness.

## 3. Acceptance and test receipts

| Check | Workbridge job | Outcome |
| --- | --- | --- |
| Final .NET 10 Release solution build | `af-physical-xyz-m2-final-build-20261115-01` | Exit 0; **0 warnings, 0 errors** |
| Core offline regression | `af-physical-xyz-m2-core-20261115-01` | **149/149 passed** |
| Windows/VM and native continuity specs | `af-physical-xyz-m2-windows-20261115-01` | **169/169 passed**, including nine new comparison groups |
| GuestReader specs | `af-physical-xyz-m2-guest-20261115-01` | **15/15 passed** |
| Final nonshown WPF smoke | `af-physical-xyz-m2-final-wpf-smoke-20261115-01` | Exit 0; **7/7 categories passed** |
| Fresh physical process listing | `af-physical-xyz-continuity-clients-20261115-01` | Exit 0; exact PID/start match |
| Four-second physical native watch | `af-physical-xyz-m2-live-watch-20261115-01` | Exit 0; four candidate observations and zero faults |

An independent reviewer examined final UI, native comparison tracker and CLI watch source and found **no blocking issues**. Its nonblocking feedback led to narrower WPF smoke-check wording: the fixture covers the initial controls and formatter/comparison semantics, **not** timer-driven stale clearing or actual Stop/close interaction.

**Scope of WPF proof:** The smoke check constructs controls and exercises synthetic comparison/presentation predicates. A human-visible WPF click-through or screen inspection of the new panel has **not** been performed. No claimed live WPF interaction follows from these synthetic checks.

## 4. Reproduce without touching gameplay

From PowerShell on **AMD-DESKTOP**:

```powershell
Set-Location 'C:\work\AutoFight'
dotnet build .\Rift.Automation.slnx -c Release
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --clients
# Copy the exact current PID and process UTC-start ticks from --clients.
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --physical-xyz-watch <pid> <start-utc-ticks> 4
# Or launch WPF and explicitly use Physical RIFT – Live XYZ > Refresh > select > Start.
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release
```

The recorded PID/start pair is historical; recheck it on every session. Do not use this executable-offset recipe with a different RIFT build until reviewed.

## 5. Practical next gates

1. **Visible WPF acceptance:** Observe actual Refresh/selection/Start/Stop and automatic stale clearing in a visible Windows desktop session, without game input.
2. **Independent coordinate-change evidence:** With Joey manually moving the physical RIFT character and AutoFight observing read-only, record unchanged-before → changed → unchanged-after under one identity. Verify a genuine value change rather than claiming motion from repeated identical native reads.
3. **Producer/cache freshness:** Correlate native updates with a separate game/source sequence or independently timed addon data. An addon API invocation might itself refresh native caches; avoid interpreting correspondence alone as passive freshness proof.
4. **Longer bounded performance:** Measure actual sampling CPU and acquisition p50/p95, failures and reattach handling before trying sustained collection. Do not map the VMware 480-second policy directly onto an unrelated native producer without qualifying its semantics.
5. **Control loop only when separately requested:** Movement commands, addon reload, game focus, controller or casting remain outside this read-only milestone.

**Bottom line:** AutoFight can independently sample a physical RIFT character’s XYZ and report meaningful *coordinate-value* comparisons in both CLI and desktop code. No RR2 runtime dependence, input, navigation or movement result is claimed.
