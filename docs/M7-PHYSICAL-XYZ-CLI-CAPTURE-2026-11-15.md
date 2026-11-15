# AutoFight M7 — Bounded headless physical XYZ trace capture

**Date:** November 15, 2026 (America/New_York)  
**Verified machine/repository:** AMD-DESKTOP, `C:\work\AutoFight`, branch `main`  
**Starting HEAD:** `bec02f736429abd172b28cd8c1adcf2d5ac5e3f9` (M6)  
**Runtime:** C# / .NET 10; AutoFight 0.5.7  
**Scope:** Explicitly invoked, read-only native physical RIFT player XYZ observations. No automated gameplay input, focus changes, VMware, UI reload or RR2 runtime dependency.

## Practical advance

M4 recorded pinned physical XYZ from the first WPF tab, M5 assessed A/B coordinate plateaus, and M6 strictly replayed saved reports. M7 provides an alternative **command-line acquisition path** so the operator can leave physical RIFT focused while manually moving the player. It does not automatically move or focus the character.

The new `--physical-xyz-trace-capture <pid> <start-utc-ticks> <seconds> <new-output.json>` command:

1. Validates numeric exact-process identity and a **10–120-second** duration.
2. Checks a **new, absolute local-drive .json destination** and its existing parent directory **before** process discovery or any native RIFT read. Existing files, UNC/device paths, alternate streams and other invalid filenames are rejected.
3. Resolves that exact PID + process-start ticks against a fresh physical-host RIFT inventory.
4. Uses the AutoFight-owned `PhysicalRiftPositionReader.ReadPinnedAsync`, strict source/player/build/age preflight and comparison tracker. Samples approximately **once per second after each read**, with monotonic global-window timing, per-read deadline, Ctrl+C cancellation and no catch-up burst.
5. Records up to the M4 history bound, including rejected/unavailable outcomes, without exporting rejected/stale/future XYZ values. An identity discontinuity, timeout, exception or cancellation marks the capture incomplete.
6. Persists the M4 canonical JSON format **only** at the explicit filename, through M4's same-directory atomic no-overwrite exporter. A new incomplete report may be saved for diagnostic replay, but exits **2** and is never called a completed capture. Empty reports are not saved.
7. Prints a bounded JSON summary of acquisition status **separately** from the M5 historical A/B assessment. `0` means the requested recording window completed and the new file was saved, **not** that the player moved. `2` means invalid/incomplete/failure.

All `producerFreshnessQualified`, `gameplayMovementProven` and `canAutomate` flags remain **false**. Even A3→B3 represents observed coordinate-value plateaus only; no independent proof of gameplay motion causation or producer liveness.

## User-operated acceptance (AMD-DESKTOP PowerShell)

From `C:\work\AutoFight`:

```powershell
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --clients
```

Read the **current** physical RIFT `Identity` (`PID:start-UTC-ticks`) returned by that command. Do **not** reuse old PID/ticks from earlier development sessions. Choose a **new** .json basename in an already-existing local directory, then replace the example PID/start values with your current ones:

```powershell
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --physical-xyz-trace-capture <PID> <START-UTC-TICKS> 30 "C:\work\AutoFight\manual-xyz-capture-001.json"
```

During the 30-second recording, leave the character stationary for at least **three stable A readings**, manually focus RIFT and briefly walk to another position, then remain stationary for at least **three stable B readings**. The program never sends a movement key; **Joey is the operator**. Choose a different unused filename if the example already exists.

Afterward, replay the exact saved file **offline**:

```powershell
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --physical-xyz-trace-assess "C:\work\AutoFight\manual-xyz-capture-001.json"
```

Inspect `assessmentState`, exact source identity, A and B XYZ/time, distance, rejected/unavailable outcomes, and status. A failed/incomplete capture is diagnostic evidence only. Keep manual JSON outside Git unless explicitly curated.

## Verification — observed, not estimated

| Check | Workbridge receipt | Result |
| --- | --- | --- |
| Final .NET 10 Release solution | `af-m7-parent-final-solution-build-20261115-01` | PASS, 0 warnings/errors |
| Core regression | `af-m7-parent-core-specs-20261115-01` | **149/149** |
| Windows/native + M7 regression | `af-m7-parent-windows-specs-20261115-01` | **215/215** |
| Guest regression | `af-m7-parent-guest-specs-20261115-01` | **15/15** |
| WPF nonshown smoke | `af-m7-parent-wpf-smoke-20261115-01` | **7/7** categories |
| Independent Windows rerun | `af-m7-independent-windows-specs-20261115-01` | **215/215**, review PASS |
| CLI help/invalid args/path/wrong process | `af-m7-cli-help-smoke-20261115-01` and related negative smokes | Help 0; rejections 2, no game read |
| Full runner→M4 SaveNew→M6 Load→M5 Assess roundtrip | M7 synthetic Windows specs | PASS |

**Total: 379/379 .NET specifications and 7/7 synthetic WPF smoke categories.** Ten new M7 tests cover A3/B3, stationary coordinates, 0ms read, stale/future redaction, PID/player discontinuities, timeout/error, cancellation, 120s bounds, destination no-clobber and canonical export/import/assessment roundtrip.

## Outstanding acceptance gate

No **live** M7 CLI capture or Joey-operated physical RIFT A→B trace was performed in this milestone. The code/test result does not close that gate. A live report must be examined for same PID/start/build/player, three A samples, a coordinate difference, three B samples and uninterrupted acquisition, and then replayed through M6. Neither a synthetic pass nor saved coordinate plateaus authorize autonomous movement. No GitHub push or gameplay input was performed during M7.
