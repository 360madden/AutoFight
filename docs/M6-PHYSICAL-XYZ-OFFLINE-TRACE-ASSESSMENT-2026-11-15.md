# AutoFight M6 — Reproducible offline physical XYZ trace assessment

**Date:** November 15, 2026 (America/New_York)  
**Machine/repository:** Independently verified AMD-DESKTOP, `C:\work\AutoFight`, branch `main`  
**Starting HEAD:** `0da199111e9fb88a1bb5b3c5c8bc526e56545df5` (M5)  
**Runtime:** C#/.NET 10, AutoFight 0.5.7  
**Scope:** Read-only validation and assessment of a previously saved M4 physical XYZ JSON file. No RIFT process access, game input, UI focus, VMware interaction, or filesystem writes.

## What M6 adds

- New `PhysicalRiftPositionTraceImporter.Load(path)` reopens AutoFight’s own `autofight-physical-xyz-trace/1` export as a historical report, using a single bounded file handle and strict UTF-8.
- Enforces the exporter’s **1,048,576-byte (1 MiB)** maximum. Accepts only ordinary fully-qualified local drive `.json` paths; rejects device namespaces, UNC, alternate streams, reserved names and malformed paths.
- Requires **byte-for-byte canonical JSON** matching the existing `PhysicalRiftPositionTraceReport.ToJson()` serializer. Duplicate, unknown, omitted, or reordered fields, numeric-enum substitutions, and forged `true` authority flags are rejected. Reuses the existing M4 report validator; no export JSON schema change.
- The CLI `--physical-xyz-trace-assess <saved-json-file>` invokes the unchanged M5 pure A/B assessor and emits a bounded JSON summary: historical source identity, state/reason, A and B XYZ/UTC/monotonic timings, sample counts and 3D distance. It emits fixed error codes (exit 2) for invalid files without printing paths or raw file data. Valid but incomplete/interrupted/truncated historical traces exit 0 with their **typed non-success state**, never an action permit.
- Canonical JSON checking is a **format/integrity check, not a digital signature or provenance authentication**. Even a valid observed coordinate difference does not prove passive game-producer freshness, user/gameplay motion cause, or the capability to automate. All three authority flags remain false.

## Command (AMD-DESKTOP PowerShell)

```powershell
Set-Location 'C:\work\AutoFight'
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --physical-xyz-trace-assess "C:\path\to\saved-trace.json"
```

Use an actual M4 **Save read-only trace** JSON file. The placeholder path is not a source receipt. Command output is offline historical analysis, not a live RIFT check.

## Verification

| Check | Workbridge job | Actual result |
| --- | --- | --- |
| .NET 10 Release solution | `af-m6-full-solution-build-20261115-01` | PASS, 0 warnings/errors |
| Core synthetic suite | `af-m6-final-core-20261115-01` | 149/149 PASS |
| Windows/native/VM suite (post-review directory regression fix) | `af-m6-rechecked-windows-specs-20261115-01` | 205/205 PASS |
| GuestReader suite | `af-m6-final-guest-20261115-01` | 15/15 PASS |
| WPF nonshown smoke | `af-m6-final-wpf-20261115-01` | 7/7 PASS categories |
| CLI help | `af-m6-cli-help-20261115-01` | PASS, command listed |
| CLI missing-file error | `af-m6-cli-missing-20261115-01` | Exit 2, sanitized `file_unavailable` |
| Independent read-only reviewer | M6 reviewer | PASS, no blocking defects |

The nine new importer regression cases include actual AutoFight writer→reader→M5 A/B equivalence, 600-entry/5-eviction Truncated state, error-only historical report, forged flags/source/counters, duplicate/unknown fields, malformed JSON, invalid UTF-8, oversize file, rejected-coordinate redaction, unsafe path families and missing/directory inputs. Source data remain unchanged.

**Total: 369/369 .NET specifications plus 7/7 WPF smoke categories.** This is offline/synthetic acceptance; visible WPF Save click-through and the first actual saved user-driven A/B trace are still outstanding. A valid-file end-to-end CLI invocation with Joey’s own saved capture awaits that capture.

## Remaining physical acceptance

1. On AMD-DESKTOP, run the WPF **Physical RIFT – Live XYZ** read-only recorder, capture three stable A readings, manually press/release W while RIFT is focused, then remain still for at least three stable distinct B readings.
2. Stop the recorder, select **Save read-only trace**, choose a new `.json` name (the exporter never overwrites).
3. Run the M6 CLI command against that exact file; compare its A/B state, timestamps and displacement with WPF, and preserve the source JSON outside Git.
4. Treat any reported A/B pair as observed coordinate-value evidence, not an authenticated proof of input causation or producer liveness.

**No GitHub push, game input, VMware operation, or installed addon changes were performed in M6.**
