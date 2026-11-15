# AutoFight M1 — First live physical RIFT native XYZ

> **Later same-day continuation:** [M2 read-only XYZ continuity and desktop observer](M2-PHYSICAL-XYZ-CONTINUITY-UI-2026-11-15.md) implemented the comparison tracker, 4/4 newer live acquisitions, and the first/default WPF physical XYZ tab. This M1 receipt remains the historical first-read record; see M2 for the latest tests and next acceptance gate.

**Date:** November 15, 2026 (America/New_York)
**Machine:** AMD-DESKTOP (independently identified by Workbridge)
**Repository:** `C:\work\AutoFight`, `main`
**Pre-milestone HEAD:** `3045925d929c6744c36f660434b5bd7f1229b708`
**Application version:** `0.5.7` (read from `src/Directory.Build.props`)
**Scope:** Read-only native XYZ candidate acquisition and a short bounded acquisition watch.
**Result:** **PASS** for exact-build live native XYZ acquisition; **NOT YET QUALIFIED** for passive producer freshness, movement, or the 480-second continuous telemetry gate.

## 1. Observed game/process identity

AutoFight's own `--clients` enumerated one current physical RIFT process:

| Identity property | Verified evidence |
| --- | --- |
| Computer | `AMD-DESKTOP` |
| Game process | `rift_x64.exe`, PID `37928` |
| UTC process creation | `2026-11-15T12:13:36.7439797Z` |
| UTC start ticks | `639303416167439797` |
| Windows session | `1` |
| Window title | `RIFT` |
| Image | `C:\Program Files (x86)\Glyph\Games\RIFT\Live\rift_x64.exe` |
| Image SHA-256 | `1935A05C9DB6D468E34E0055014A4A99C1B5AFCEB1695884E24600F5C20A4BE8` |

The executable hash **exactly matched** RR2's previously verified physical-RIFT native-position recipe. That RR2 source was a development reference: no RR2 service, assembly, source checkout, controller or running receiver is required by AutoFight's new native reader.

All six AutoFight addon files installed in the canonical physical OneDrive RIFT addon directory matched source bytes, but **in-game addon loading and its nonce binding remain unverified**. No `vmware-vmx` process was seen in the read-only host check. The older VMware Lab 2 unbound-addon milestone is separate and remains unresolved.

## 2. Implemented production behavior

- `src/Rift.Automation.Windows/PhysicalRiftPositionReader.cs`: standalone native Windows reader using the proven current-build player-system/entity-table/position-component route. It uses `PROCESS_VM_READ` and query/synchronize rights only, never write/injection/input privileges.
- The reader checks PID, UTC process creation, executable SHA-256, module mapping, player key, bounded table traversal, component vtable, entity backlink, finite XYZ and re-read anchor continuity. It checks process/module identity again and rehashes the executable after acquisition.
- A cooperative per-process native-read file lease matches the RR2 naming convention to avoid simultaneous participating samplers; it is a file-lock protocol, not an RR2 runtime dependency. Lease wait is bounded.
- `src/Rift.Automation.Desktop/App.PhysicalXyz.cs` and the CLI entry in `App.xaml.cs`: `--physical-xyz <pid> <start-utc-ticks>` obtains one candidate with a 15-second deadline.
- `src/Rift.Automation.Desktop/App.PhysicalXyzWatch.cs`: `--physical-xyz-watch <pid> <start-utc-ticks> <seconds>` accepts 2–30 seconds, reacquires roughly once per second, emits JSON per sample and a terminal summary, and supports cancellation.
- `tests/Rift.Automation.Windows.Specs/PhysicalRiftPositionSpecs.cs` plus harness registration: tests exact build pin, invalid process identity and pre-cancelled reads.

A `Candidate` is **a new anchored read of current process memory**, not a guarantee that RIFT's producer refreshed its position between acquisitions. The output explicitly sets `passiveFreshnessQualified=false`, `sustainedQualification=false` and `canAutomate=false`.

## 3. Actual live read receipts

| UTC observed time | Workbridge job | Result | RIFT XYZ | Native acquisition |
| --- | --- | --- | --- | --- |
| 20:43:24 | `af-native-xyz-first-physical-20261115-01` | Exit 0 / Candidate | X=7191.6084, Y=873.9848, Z=3019.1848 | 114.5 ms |
| 20:45:09 | `af-native-xyz-repeat-physical-20261115-02` | Exit 0 / Candidate | X=7191.6084, Y=873.9848, Z=3019.1848 | 101.5 ms |
| 20:48:43–20:48:47 | `af-native-xyz-five-second-watch-20261115-01` | Exit 0 / 5 of 5 Candidate | Same XYZ and player key in all five | 103.0, 80.9, 89.1, 79.4, 82.4 ms |
| 20:53:04–20:53:06 | `af-native-xyz-watch-deadline-live-20261115-01` | Exit 0 / 3 of 3 Candidate | Same XYZ and identity after duration hardening | 105.3, 79.7, 84.1 ms |

Five-second watch summary: requested 5 seconds, 5 attempts, **5 current-build candidates**, **0 unavailable**, **0 identity changes**, **0 errors**, **not cancelled**; measured wall-clock watch 5531 ms. A follow-up hardening bounded waiting and individual read deadlines by the **remaining total watch time**. The post-hardening 3-second watch returned **3/3 candidates, no faults, and 3001.1 ms elapsed**. Both report `candidate-sequence-observed`, not qualified native freshness. A stationary player can legitimately return the same coordinates during distinct successful acquisitions.

Negative checks deliberately rejected invalid identity/arguments:

- `af-native-xyz-final-wrong_epoch-20261115-01`: one-off read with UTC start ticks off by one returned `IdentityMismatch`, null XYZ/hash and exit **2**.
- `af-native-xyz-wrong_epoch_watch-20261115-01`: watch with wrong process-start ticks stopped after one `IdentityMismatch`, zero candidates, exit **2**.
- `af-native-xyz-invalid_duration-20261115-01`: 1-second watch argument rejected before game access, exit **2**.

The first and repeated reads were noninteractive: **no gameplay input, mouse focus, keypress, addon reload, VM lifecycle, guest credential operation, or RR2 service invocation**.

## 4. Build and regression results

| Verification | Workbridge job | Outcome |
| --- | --- | --- |
| Final .NET 10 Release solution build (including bounded-watch hardening) | `af-native-xyz-final-deadline-build-20261115-01` | Exit 0, **0 warnings**, **0 errors** |
| Core synthetic/replay specs | `af-native-xyz-final-core-20261115-01` | **149/149 passed** |
| Windows telemetry/native specs | `af-native-xyz-final-windows-20261115-01` | **160/160 passed** (3 new) |
| GuestReader specs | `af-native-xyz-final-guest-20261115-01` | **15/15 passed** |
| WPF nonshown smoke (post-watch build) | `af-native-xyz-smoke_post_watch-20261115-01` | **6 of 6 passed** |

An independent code review verified RR2 lookup parity, process/lease isolation, fail-closed data validity and the absence of input or write privileges. Review caught and corrected three issues before the first successful live run: nullable unverified SHA evidence, process-liveness exit-code ambiguity, and the need to hash the executable again after sampling. The native reader remains bound to the exact observed executable build.

## 5. Reproduce safely on AMD-DESKTOP

In Windows PowerShell, from `C:\work\AutoFight`:

```powershell
dotnet build .\Rift.Automation.slnx -c Release
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --clients
# Copy PID and start ticks from the returned client Identity field (PID:startTicks).
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --physical-xyz <pid> <start-ticks>
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --physical-xyz-watch <pid> <start-ticks> 5
```

The sample PID and start ticks above are **historical**. Never reuse them after a RIFT restart. A different executable hash is explicitly unsupported until its offsets and semantics are reverified. This command performs no game input.

## 6. Remaining acceptance gates

1. **Passive source freshness:** Compare independent source evidence while the player position is knowingly changing or while an addon produces advancing timestamps/sequence; distinguish stationary-but-new acquisition from stale cached memory. Do not label an unchanged position stale merely because it is equal, but do not infer producer freshness solely from repeated reads.
2. **Bounded sustained native collection:** Optimize revalidation for repeated samples without weakening process identity or coordinator lease safety. Instrument p50/p95/max read duration, faults, sequence/restart behavior and CPU; do not automatically run the existing 480-second guest-frame qualification against a native source with different liveness semantics.
3. **Desktop UI integration:** Expose this physical-native source as an explicit, read-only player field alongside existing owned GuestReader telemetry. Retain evidence category/status, not a single generic unqualified `hasXYZ`.
4. **Old Lab 2 addon route:** Only if Lab 2 is explicitly resumed, first verify VM/game identity and in-game addon binding; the Reader-to-host receipt did **not** establish an addon frame. A read-only raw `RR2A1` diagnostic could distinguish unloaded vs unbound without a chat command.
5. **Movement:** Requires independent current position-change evidence and a separately supervised input authorization; this milestone sends none.

**Bottom line:** AutoFight now reads real position coordinates from a currently running physical RIFT process without any other project at runtime. Five repeated acquisitions passed, with correctly bounded claims and explicit remaining qualification work.
