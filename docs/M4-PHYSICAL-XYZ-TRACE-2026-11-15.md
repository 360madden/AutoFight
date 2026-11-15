# AutoFight M4 — Physical RIFT XYZ observation trace

**Date:** November 15, 2026 (America/New_York)  
**Machine/repo:** Independently verified AMD-DESKTOP, `C:\work\AutoFight`, `main`  
**Starting HEAD:** `2cce6ab9196cb1abc56c8d2037822be53a6c0824`  
**Runtime:** Standalone C#/.NET 10 WPF, version 0.5.7  
**Scope:** Read-only physical RIFT coordinates; no game input, focus changes, reload, VMware operation, or RR2 service.

## Implemented

- New AutoFight-owned `PhysicalRiftPositionTraceRecorder` keeps at most **600 completed native XYZ acquisition outcomes**. It tracks total/retained/evicted counts, UTC capture times, monotonic elapsed milliseconds, native/comparison statuses, XYZ, absolute deltas, 3D distance, and pinned PID/process-start/executable-build/player identity.
- A new session begins only after WPF Start re-verifies the selected physical RIFT PID/start/path. Previous trace remains available after Stop, stale display clearing or watch faults; a denied/canceled Start does not erase history.
- The recorder independently rejects stale/future (>2.5-second native capture age), invalid, out-of-order or identity-mismatched candidates and inconsistent comparison deltas. **All rejected XYZ and deltas are null** in the export.
- Returned unavailable/error/terminal identity outcomes are recorded; no-result read timeouts/exceptions are represented without user exception text. A user-cancelled in-flight read can terminate without a completed outcome. The WPF age-display timer does **not** invent a native acquisition outcome.
- Snapshot uses a detached read-only collection; versioned JSON has allowlisted diagnostic fields and no arbitrary paths, credentials or private nonce. Its explicit `passiveFreshnessQualified`, `gameplayMovementProven` and `canAutomate` flags are all **false**.
- The first/default **Physical RIFT – Live XYZ** tab has a bounded trace summary and **Save read-only trace** button. Save opens a dialog only on user click, snapshots the historical trace, and delegates to shared `PhysicalRiftPositionTraceExporter.SaveNew` in the Windows assembly. This writes a new `.json` file with same-directory `FileMode.CreateNew` staging and `File.Move(overwrite:false)`. Ordinary absolute paths only; device paths and reserved names rejected. No automatic export.
- Existing sticky last-20 `Changed` history still works, and source provenance remains identified by original PID/start/player key after Stop.

Source: `src/Rift.Automation.Windows/PhysicalRiftPositionTraceRecorder.cs`; `src/Rift.Automation.Desktop/MainWindow.xaml`; `src/Rift.Automation.Desktop/MainWindow.PhysicalXyz.cs`. New deterministic tests: `tests/Rift.Automation.Windows.Specs/PhysicalRiftPositionTraceSpecs.cs` (registered in Windows `Program.cs`).

## Verifiable acceptance

| Gate | Job | Result |
| --- | --- | --- |
| .NET 10 Release solution | `af-m4-physical-xyz-trace-release-build-20261115-01` | **PASS**, zero warnings/errors |
| Core | `af-m4-trace-final-core-20261115-01` | **149/149 PASS** |
| Windows/native/VM | `af-m4-trace-final-windows-20261115-01` | **179/179 PASS** |
| GuestReader | `af-m4-trace-final-guest-20261115-01` | **15/15 PASS** |
| WPF nonshown smoke | `af-m4-trace-final-wpf-20261115-01` | **7/7 PASS** categories |
| Independent read-only source review | Final SHA audit | **PASS**, no blocking findings |

**Initial M4 acceptance:** 343 specifications plus seven nonshown WPF smoke categories passed. WPF Save dialog, the actual filesystem export via user click, and a visible physical game movement trace have not been manually tested. The export writer was subsequently extracted and exercised in the M4.1 addendum below.

The earlier M3 real native XYZ readings showed a roughly 55.84-unit coordinate difference across 11 minutes under the same physical RIFT PID/start/build/player. This is **not** a motion-timed M4 trace. Neither this difference nor new software tests proves native producer freshness, actual input causation, or movement automation.

## M4.1 addendum — Export writer regression hardening (same day)

The JSON writer has been extracted from the WPF code into new `src/Rift.Automation.Windows/PhysicalRiftPositionTraceExporter.cs`, called only by the explicit Save handler. It validates the versioned, redacted report (source identity, counts, accepted/rejected entries and qualification-denial flags) and enforces a **1 MiB JSON ceiling** before file I/O. It checks ordinary absolute `.json` paths, rejects device namespaces/alternate streams/reserved filenames, stages to a uniquely created file, and publishes with no overwrite. Only its own staging file can be removed on failed save.

New `tests/Rift.Automation.Windows.Specs/PhysicalRiftPositionTraceExportSpecs.cs`, registered in Windows harness, performs real **isolated temp-directory filesystem tests**: UTF-8 no BOM/schema and authority fields, existing-destination byte-for-byte preservation, own-temp cleanup, invalid path rejection, forged/untrusted report rejection, invalid rejected XYZ rejection, and a complete **600-of-605** retained-entry JSON roundtrip. These are tests of export behavior; they do **not** open the user's Save dialog, read game memory or send gameplay input.

| M4.1 verification | Workbridge job | Result |
| --- | --- | --- |
| Full Release solution | `af-m41-physical-xyz-export-release-build-20261115-01` | **PASS**, 0 warnings/errors |
| Core | `af-m41-export-final-core-20261115-01` | **149/149 PASS** |
| Windows | `af-m41-export-final-windows-20261115-01` | **186/186 PASS** |
| Guest | `af-m41-export-final-guest-20261115-01` | **15/15 PASS** |
| WPF nonshown smoke | `af-m41-export-final-wpf-20261115-01` | **7/7 PASS** categories |

**350/350 automated specifications now pass.** The manual Save dialog click-through and continuous before/after movement trace remain external acceptance gates.

## Next acceptance: Joey's user-driven movement

On AMD-DESKTOP, in PowerShell:

```powershell
Set-Location 'C:\work\AutoFight'
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release
```

1. Select the first **Physical RIFT – Live XYZ** tab; **Refresh physical RIFT clients**, choose exact game PID/start, and click **Start read-only XYZ**.
2. Wait for several baseline/unchanged readings. Manually focus RIFT, press **W** briefly in a safe area, release and remain still **3–5 seconds**.
3. Return to AutoFight, inspect `Changed` and trace counts, then **Stop sampling**. Historical results should remain.
4. Explicitly click **Save read-only trace** and choose a **new** JSON filename. Inspect pinned identity and the before/during/after timeline; never interpret an isolated value change as proven input causation.

**Next M5 gate:** One uninterrupted read-only before/after trace under the same source identity, with visible user-driven click-through and no falsely accepted stale/mismatched coordinates. Producer freshness and controlled movement remain separate future gates. No GitHub push is part of M4.
