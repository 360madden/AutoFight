# AutoFight M5 — Physical RIFT before/after XYZ trace assessment

**Date:** November 15, 2026 (America/New_York)  
**Machine/repo:** Verified `AMD-DESKTOP`, `C:\work\AutoFight`, `main`  
**Starting HEAD:** `26ef0b65ef1fbae183f9c9fd622a5fe11bb1ec20` (M4.1)  
**Runtime:** Standalone C#/.NET 10 WPF 0.5.7  
**Scope:** Strict **read-only** interpretation of an existing 600-outcome native XYZ trace. No RIFT movement, UI focus, keyboard input, memory writes, VMware operation, new file export format, or RR2 service.

## Why M5 matters

M4 records and safely exports historical XYZ outcomes. Without interpretation, Joey would have to examine up to 600 JSON entries manually to determine whether a test captured stable coordinates both before and after he pressed W himself. M5 adds a pure, bounded assessment over the existing immutable in-memory report. It **does not** infer that AutoFight or Joey caused a coordinate change, or that the game producer independently advanced.

## Implementation

- New `src/Rift.Automation.Windows/PhysicalRiftPositionTraceAssessor.cs`: deterministic `PhysicalRiftPositionTraceAssessor.Assess(report)`, no OS, clock, filesystem, network or input access. Returns a typed `PhysicalRiftPositionTraceAssessment`.
- A/B positive requires **at least three consecutive accepted equal XYZ acquisitions at an earlier A**, a distinct observed coordinate value/change, then **three consecutive accepted equal XYZ acquisitions at a different final B**. Intermediate distinct values are permitted while transitioning. The final A/B positions, UTC capture times, monotonic elapsed times and computed 3D displacement are diagnostic only.
- Entire retained sequence must have exact, unchanging PID/start/build/player provenance; contiguous sample indexes; finite accepted XYZ; coherent Baseline/Changed/Unchanged delta fields; strictly increasing monotonic time and **every neighboring acquisition gap less than 4,500 ms**. All rejected/unavailable/identity faults interrupt, and FIFO eviction or missing sequence refuses an end-to-end conclusion. A fault after observing valid A/B preserves earlier A/B data as *historical diagnostics* but sets the overall state to Interrupted/SourceChanged.
- Typed assessment states: `InsufficientEvidence`, `OneCoordinatePlateau`, `AwaitingFinalPlateau`, `TwoCoordinatePlateausObserved`, `Interrupted`, `SourceChanged`, `Truncated`. The explicit `passiveFreshnessQualified`, `gameplayMovementProven` and `canAutomate` fields are **false in every state**.
- Updated first/default WPF **Physical RIFT – Live XYZ** tab: a color-coded A/B assessment under the existing trace summary, with the next three-sample gate while recording; green observed-values-only A/B result; red interruption/source/truncation reasons. Historical assessment remains after Stop; a newly verified Start resets the session. The recorder, exporter, 600-entry cap, sticky last-20 change evidence and JSON schema are unchanged.

## Regression acceptance

New `tests/Rift.Automation.Windows.Specs/PhysicalRiftPositionTraceAssessmentSpecs.cs` is registered in the Windows suite. Deterministic cases cover A3→B3, multiple intermediate changes before B, A3→B1→A3 not being a false completed A/B sequence, a single plateau, 3+2 incomplete, post-success native fault retaining only historical diagnostics, process/player swaps, forged counters/deltas/source, strictly <4500ms boundary (4499 passes; 4500 fails), monotonic and sample-index errors, FIFO600 eviction, and all authority flags false. WPF construction-only smoke exercises initial, successful A/B and interrupted assessment formatting without opening a visible window or touching RIFT.

| Check | Workbridge job | Actual result |
| --- | --- | --- |
| .NET 10 Release solution | `af-m5-xyz-ab-assessor-solution-build-20261115-01` | **PASS**, 0 warnings/errors |
| Core regression | `af-m5-assessor-final-core-20261115-01` | **149/149 PASS** |
| Windows/native/VM regression | `af-m5-assessor-final-windows-20261115-01` | **196/196 PASS** |
| GuestReader regression | `af-m5-assessor-final-guest-20261115-01` | **15/15 PASS** |
| Nonshown WPF smoke | `af-m5-assessor-final-wpf-20261115-01` | **7/7 PASS** categories |

**Total: 360/360 automated specifications and 7/7 WPF nonshown smoke categories.** Passing offline tests does not verify actual visible WPF click-through, a current user-driven movement event, native producer refresh or controlled movement.

## Current physical live-source receipt (read-only)

At about **5:06 PM EST** on independently verified AMD-DESKTOP, `--clients` found physical `rift_x64.exe` PID `37928` with creation UTC ticks `639303416167439797` (same historical process identity). `--physical-xyz-watch ... 6` returned **6/6 Candidate**, zero identity/continuity/unavailable/read faults, no coordinate-value changes, XYZ **7189.175 / 871.5005 / 3074.915**, same current-build SHA and player key. AutoFight WPF had **no open window** at the time. Workbridge jobs `af-m5-readonly-clients-20261115-01` and `af-m5-readonly-xyz-watch-20261115-01` both exited 0. These results show acquisition continuity at one coordinate value, **not** producer freshness or motion.

## Required next user-assisted acceptance

On AMD-DESKTOP in PowerShell:

```powershell
Set-Location 'C:\work\AutoFight'
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release
```

1. In **Physical RIFT – Live XYZ**, Refresh clients, select the actual physical RIFT process, click **Start read-only XYZ**.
2. Wait for **at least three equal A readings** (the assessor should show one plateau).
3. Manually switch to RIFT and press W briefly in a safe location. Release and stay at the new position for **at least 3–5 seconds** to allow three accepted B readings.
4. Return to AutoFight. Check that **Two repeated coordinate values OBSERVED** appears in green with A/B UTC and 3D displacement; if not, use the typed incomplete/interrupted/source state to diagnose. Click **Stop sampling**.
5. Click **Save read-only trace** and select a NEW JSON filename; do not overwrite prior evidence. Examine the JSON and the A/B result without claiming input causation.

**M5 coding is complete; physical A/B acceptance remains pending Joey's manual game action.** A/B repeated values alone never prove passive producer freshness or an AutoFight-induced move. No GitHub push was performed.
