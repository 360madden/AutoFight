# AutoFight YEA — SESSION HANDOFF

> **November 15, 2026 — NEWEST M7 headless physical XYZ CLI capture:** [M7 native trace capture report](docs/M7-PHYSICAL-XYZ-CLI-CAPTURE-2026-11-15.md). On independently verified **AMD-DESKTOP** (`C:\work\AutoFight`), the CLI now supports `--physical-xyz-trace-capture <pid> <start-utc-ticks> <seconds> <new-output.json>`: bounded **10–120-second** read-only native XYZ history, exact physical PID/start, current-build/player/age checks, ~1Hz nonburst sampling, cancellation and native-read deadlines, redacted M4 no-overwrite export and M5 A/B summary. New destination rejected **before native RIFT access**; incomplete history never passes as complete. No automatic focus/movement/VM/RR2. **Final Release solution build 0 warnings/errors; 149 Core + 215 Windows + 15 Guest = 379/379 tests; 7/7 nonshown WPF smoke; independent review and its 215/215 Windows rerun PASS.** CLI help/invalid args/invalid destination/wrong process negative checks passed. **Next external gate:** Joey manually moves physical RIFT from stable A to stable B during a new 30s CLI capture, then runs M6 replay on the saved JSON; no live manual capture yet and no GitHub push.

> **November 15, 2026 — NEWEST M6 physical XYZ saved-trace offline replay:** [M6 strict importer/CLI report](docs/M6-PHYSICAL-XYZ-OFFLINE-TRACE-ASSESSMENT-2026-11-15.md). On independently verified **AMD-DESKTOP** (`C:\work\AutoFight`), the newly owned `PhysicalRiftPositionTraceImporter.Load` reads only canonical M4 JSON under 1 MiB, strict UTF-8/source/denial-flag validation, rejects duplicates, unknown fields and tampering, then feeds M5's pure assessor; `--physical-xyz-trace-assess <saved-json-file>` prints a bounded historical A/B JSON summary with sanitized failures and no game input. **Release solution build 0 warnings/errors**, **149 Core + 205 Windows + 15 Guest = 369/369** specs and **7/7** nonshown WPF smoke checks PASS, independent review PASS. CLI `--help` PASS and missing-file rejection exit 2 with redacted reason. Manual gate: Joey must capture a new live WPF A→B trace with his own brief W movement, save a unique JSON, then verify the M6 offline CLI matches WPF; no such live file has yet been accepted. **No GitHub push, RIFT input/focus, VMware or RR2 service**.

> **November 15, 2026 — NEWEST M5 physical read-only A/B assessor:** [M5 assessment, deterministic tests, and manual acceptance](docs/M5-PHYSICAL-XYZ-AB-ASSESSMENT-2026-11-15.md). On independently verified **AMD-DESKTOP**, new pure `PhysicalRiftPositionTraceAssessor.Assess` analyzes the M4 600-entry history for 3+ accepted identical A acquisitions, changed values, and 3+ distinct identical B acquisitions, strictly matching PID/start/build/player and monotonic gaps <4500 ms, no invalid/rejected/sequence/FIFO events. The first WPF tab now reports typed progress/Interrupted/SourceChanged/Truncated or historical A/B values/UTC/3D distance, **without any movement/freshness/input authority claim**. Final Release build **0 warnings/errors**; Core **149**, Windows **196**, Guest **15** = **360/360** specs, WPF synthetic smoke **7/7**, independent reviewer **PASS**. 5:06 PM actual read-only watch: same physical RIFT PID 37928/start ticks, **6/6 valid native XYZ** at 7189.175/871.5005/3074.915, 0 faults and no coordinate changes; WPF not open. **Pending live gate:** Joey manually launches WPF, starts recorder, moves W briefly after 3 A samples, waits for 3 B samples, stops and saves explicit JSON. No automated gameplay input/focus/VM and no GitHub push.

> **November 15, 2026 — NEWEST M4.1 export reliability:** The first-tab Save button now calls AutoFight-owned shared `PhysicalRiftPositionTraceExporter.SaveNew`, retaining explicit user-click and strict no-clobber behavior. Seven new Windows filesystem regression cases verify a full **600-entry JSON roundtrip**, UTF-8/schema, existing destination exact-byte preservation, own-temp cleanup, bad paths and forged/rejected-coordinate redaction. Final .NET 10 Release build **0 warnings/errors**, Core **149/149**, Windows **186/186**, Guest **15/15** = **350/350 specs**, WPF nonshown smoke **7/7**. No live RIFT input, focus or automatic export. See [M4 report + M4.1 addendum](docs/M4-PHYSICAL-XYZ-TRACE-2026-11-15.md); manual visible Save and Joey's continuous user-driven movement trace are pending.

> **November 15, 2026 — NEWEST M4 physical XYZ trace milestone:** [M4 report, tests and user-assisted acceptance](docs/M4-PHYSICAL-XYZ-TRACE-2026-11-15.md). On independently verified **AMD-DESKTOP** (`C:\work\AutoFight`), the first/default WPF Physical RIFT – Live XYZ tab now records a strictly source-pinned, bounded **600-outcome** native acquisition trace (UTC and monotonic timing, Baseline/Changed/Unchanged/rejection/unavailable, accepted XYZ and 3D deltas) and exposes **Save read-only trace** JSON only via user click. Its no-overwrite, ordinary-path exporter stages with CreateNew then Move(overwrite:false). Rejected/stale/future/mismatched raw XYZ is null in export. Existing sticky last-20 Changed history remains. Full .NET 10 Release build **0 warnings/errors**; Core **149/149**, Windows **179/179**, Guest **15/15**, WPF synthetic smoke **7/7**; independent final read-only review **PASS**. **Pending:** visible WPF click-through and one uninterrupted before/during/after trace while **Joey manually moves** RIFT. No programmatic input, focus, UI reload, VMware or RR2 runtime. No push. Reverify physical PID/start/build/player and current Git HEAD before further work.

> **November 15, 2026 — NEWEST M3 physical XYZ value-change and sticky-history milestone:** [M3 evidence and manual movement-test procedure](docs/M3-PHYSICAL-XYZ-CHANGE-HISTORY-2026-11-15.md). On verified **AMD-DESKTOP**, native C# read-only samples from unchanged physical RIFT PID 37928/start `639303416167439797`/exact EXE SHA/player key showed XYZ `7191.6084/873.9848/3019.1848` at ~4:19 PM versus `7189.1750/871.5005/3074.9150` at 4:30:07 PM: a **55.8386-unit 3D coordinate-value difference across ~11 minutes**, with no AutoFight game input and **no proof of when/why the change occurred**. A 4:31 PM follow-up watch returned **4/4 valid candidates at the new XYZ**, zero faults. The WPF Physical RIFT tab now retains the most recent **20 Changed events**, per-session change count and last timestamp/distance/XYZ; history remains after Stop/fault and preserves its original PID/start/player-unit-key label even when another client is selected. Actual live XYZ clears when stale or stopped. Read-only independent review **PASS**, Release build **0 warnings**, Core **149/149**, Windows **169/169**, Guest **15/15**, WPF nonshown smoke **7/7**. **Remaining external test:** Joey launches the visible WPF panel and manually moves his character while Start is sampling; record continuous before/during/after under one identity, then inspect sticky change history. No automatic gameplay input or UI focus change was used. Recheck current process identity/Git HEAD before next session; this local milestone has no GitHub push.

> **November 15, 2026 — preceding M2 physical XYZ continuity + desktop:** [M2 source/test/live evidence](docs/M2-PHYSICAL-XYZ-CONTINUITY-UI-2026-11-15.md) on verified **AMD-DESKTOP**. AutoFight now has a pure native position-change tracker and first/default on-demand WPF **Physical RIFT – Live XYZ** panel (Refresh, select exact PID/start, Start, live XYZ, Baseline/Unchanged/Changed and 3D delta, stale clear, Stop). CLI `--physical-xyz-watch` reports the same comparisons. Actual 4-second read-only watch `af-physical-xyz-m2-live-watch-20261115-01`: 4/4 candidate native acquisitions, identical XYZ (7191.6084, 873.9848, 3019.1848), **0 changed values / 0 faults**, 4:19 PM EST. Release .NET 10 build zero warnings; **149 Core, 169 Windows, 15 Guest = 333 tests**, plus **7/7 nonshown WPF smoke**, passed; independent code review PASS. Process PID 37928/start `639303416167439797` are **historical and must be rechecked**. User-driven coordinate change, passive internal producer freshness, visible WPF click-through, 480-second qualification and movement remain unverified. **No gameplay input was sent**; only the user should manually move the game character for the next optional read-only comparison gate. Source changes are scoped to `C:\work\AutoFight`; verify current Git HEAD before resuming.

> **November 15, 2026 — preceding native XYZ result:** [Physical XYZ milestone evidence](docs/M1-PHYSICAL-NATIVE-XYZ-2026-11-15.md) records standalone AutoFight-owned C# native position acquisition from physical RIFT on AMD-DESKTOP, with exact PID/start/executable SHA binding, RR2-compatible file lease, independent code and CLI. The first two real readings returned XYZ (7191.6084, 873.9848, 3019.1848), 114.5ms/101.5ms, exit 0; a wrong epoch was rejected with no coordinates. This is a current-build **candidate**, not proof of passive refresh, sustained 480s freshness, game movement or verified Lab 2 addon telemetry. No gameplay input. The November 6 Lab 2 handoff below remains historical. Always revalidate current Git HEAD and process identity.

> **November 6, 2026 newest first-XYZ handoff:** [Lab 2 Reader → addon binding → first XYZ](docs/HANDOFF-AUTOFIGHT-LAB2-FIRST-XYZ-2026-11-06.md) is the current machine-readable continuation packet. It records verified guest-to-host Reader transport but **zero matching addon frames/XYZ**, the reviewed one-shot private binding helper, AutoFight-owned VNC environment, LuaPad Preview 20 in Lab 2, pending native-chat focus and exact next actions. This supersedes older milestones below where they disagree; verify current Git HEAD and live process identity before any operation.

> **November 6, 2026 current handoff:** [Detailed two-lab AutoFight handoff](docs/HANDOFF-AUTOFIGHT-VMWARE-LABS-2026-11-06.md). Both VMware lab guest-to-host TCP 48761 paths now pass under an exact VMnet8 firewall rule, and both labs have saved Codex full-access/no-approval defaults. Lab1 AutoFight files are installed but no in-game addon load, current XYZ, or movement has been proved. The October 31 migration details below remain historical; recheck Git HEAD and live VM/game identity.

**Created:** October 31, 2026 (America/New_York)
**Status:** Repository separation complete; live bidirectional gameplay control remains unverified.
**Authoritative working directory:** **C:\work\AutoFight**
**Branch / verified baseline commit:** **main / 9aea36a91d839c54e4f14e45bb8054909aa9251b**
**Application version:** **0.5.7**, read from src/Directory.Build.props. Check that file again when resuming.

## New same-day Milestone 1 addendum (read first)

The baseline commit and five-project acceptance below describe the **earlier repository
migration**, not the latest implementation. Always inspect current Git HEAD,
AGENTS.md, docs/current-status.md and
docs/M1-standalone-guest-port-2026-10-31.md before resuming.

- AutoFight now has a seven-project solution with its **own guest-reader**
  Windows x64 executable and 16 selectively adapted RR2 source files. It
  compiles without RR2 projects/assemblies. The guest publisher includes
  one AutoFight GuestReader EXE and six AutoFight-owned Lua addon files.
  Binding is /afbind, and RR2A1/RR2L1 remain compatibility wire markers.
- The Windows assembly now owns an authenticated in-process observation
  listener, typed delivery/XYZ ingest with pinned identity/sequence guards,
  and a bounded synthetic keyboard executor. The WPF Refresh and pinned
  Telemetry buttons share the VM operation exclusion/cancellation gate.
- Current offline acceptance: Release solution build zero warnings/errors,
  **149/149 Core**, **141/141 Windows/VM**, **13/13 Guest**, WPF smoke five
  categories. Job IDs and port provenance are in the milestone document.
- This does **not** establish live RIFT XYZ, eight-minute sustained freshness,
  native guest input, a bundled/connected host control transport, or movement.
  The new guest reader and listener are **not yet integrated into the WPF live
  session**; the old controller bridge remains a legacy option.
- Read-only current historical pins for Lab 1 and Lab 2 both reported
  **reader_not_running**. No addon was installed, VM started/stopped, guest
  helper deployed, game scan started, or gameplay input sent during the port.
- Next: wire an AutoFight-owned host receiver to a reviewed private config
  and live UI; after separate lab permission deploy/bind the guest reader
  and obtain live XYZ without RR2 services. Then qualify 480 seconds and
  port the RR2 guest-control safety chain, before a separately authorized
  observed movement pulse.


## 1. Non-negotiable routing correction

The previous assistant mistakenly built AutoFight inside **C:\work\Rift API**, an older independent RIFT API/Lua collector research repository. Joey caught this on October 31 and authorized separating the application.

- All **new AutoFight code, application tests, documentation, packages, milestones and Git commits belong in C:\work\AutoFight only**.
- The standalone Git repository was initialized there, the necessary app files were selectively imported, and baseline commit **9aea36a** was made with a clean worktree.
- The old **C:\work\Rift API** repo now has a top-of-file redirect in AGENTS.md and README.md, committed as **752e133b8ecc9cbcb091f83fcfd9ce6b7f550693**. This older repo still owns **RiftApiCollector** and API-discovery research.
- The old repo's historical AutoFight source and exactly five unfinished v0.5.7 draft files were **preserved**, not deleted or reset. Its working tree remained dirty in those five source paths after the redirect commit. Do not clean it up, stage it, or treat it as the current AutoFight source without a separate explicit request.
- **C:\work\riftreader2** is another independent project; it supplies reference patterns and optional existing VMware telemetry/controller integration. Do not modify it merely because AutoFight needs telemetry.
- The original automation master prompt v2 named the wrong repo. The corrected directive is **docs/automation-platform-master-prompt-v3.md**. Treat v2 and copied historical status/release docs as evidence, not current ownership instructions.
- This was a **local Git migration**. No GitHub remote/push or off-machine backup was performed.

## 2. What is in the new AutoFight repository

A standalone .NET 10 Windows application with five projects:

- src/Rift.Automation.Core — typed observations, deterministic reducers, replay and analysis.
- src/Rift.Automation.Windows — process inventory, manual RAC TSV parser, pinned VM telemetry, bound controller/preflight, supervised pulse and sustained evidence.
- src/Rift.Automation.Desktop — WPF UI and CLI commands.
- tests/Rift.Automation.Specs — Core regression runner.
- tests/Rift.Automation.Windows.Specs — Windows, VMware, controller and export regression runner.

The standalone application no longer compiles source from the Rift API research folder. Its **RacTsvParser.cs** was copied into src/Rift.Automation.Windows and the old relative tool linkage was removed. The original Lua addon, unrelated Python tests, private configs, runtime artifacts and original mixed .git history were not copied.

The migration also finished an incomplete v0.5.7 evidence-export draft:

- Added missing WPF **Save redacted report** button and status text.
- Completed an injectable WPF save smoke path and fixed warnings-as-errors compilation.
- Added four focused Windows specs covering export envelope redaction, cancellation precedence, invalid/forged results, no-overwrite saves and cancellation.
- Produced a self-contained x64 Windows EXE from the new repository.

See **docs/MIGRATION-2026-10-31.md**, **docs/current-status.md**, **docs/release-autofight-0.5.7-2026-10-31.md**, **README.md**, and **AGENTS.md** for detailed provenance and instructions.

## 3. Verified acceptance at the handoff

These are **actual Workbridge command results from C:\work\AutoFight**, not extrapolations from the old repository.

| Check | Job ID | Final evidence |
| --- | --- | --- |
| Five-project .NET 10 Release build | af-m4p-specs-build-20261031-01 | Exit 0, zero warnings/errors |
| Core synthetic regression suite | af-migration-core-specs-20261031-01 | Exit 0, 149/149 |
| Windows/VM suite after export additions | af-m4p-export-specs-20261031-01 | Exit 0, 119/119 |
| WPF nonshown smoke | af-migration-wpf-smoke-20261031-01 | Exit 0, all five categories |
| Self-contained win-x64 single-file publish | af-migrate-selfcontained-publish-20261031-01 | Exit 0 |
| Published EXE --help from different cwd | af-migrate-exe-help-20261031-01 | Exit 0, AutoFight 0.5.7 |
| Published EXE --smoke-test from different cwd | af-migrate-exe-smoke-20261031-01 | Exit 0, five categories |
| Published EXE SHA-256 | af-migrate-exe-hash-20261031-01 | Exit 0 |

**Built executable:**
C:\work\AutoFight\artifacts\local\autofight-0.5.7-win-x64-singleexe\Rift.Automation.Desktop.exe

**Published output:** exactly one EXE, 139,984,037 bytes.
**SHA-256:** 87487b8a511585450af022128869611678f30ff36507a8f1b516966e6a24ce23

This is a locally accepted development executable, not an installer, signed release, clean-machine result or visible UI click-through test. Do not mistake a WPF smoke test for proof of a displayed interactive window.

**Live Workbridge at migration:** 0.16.4-dev, workspace C:\work, six **command-job slots**. These are not AI-agent slots. At handoff creation the connection and Git repo were rechecked: AutoFight was clean on main at 9aea36a.

## 4. Functional capability boundary — do not overclaim

Implemented/tested in code:

- Synthetic observation and replay with typed missing/stale/invalid semantics.
- Read-only Windows RIFT process metadata and manual RAC-TSV/1 file import.
- Existing authenticated VMware Lab 1/Lab 2 controller status, source-bound telemetry reads, short watches and read-only binding preflight.
- Fixed **480,000-ms (8-minute)** source-pinned telemetry qualification, roughly 500-ms polls, strict accepted-progress gaps **below 4,500 ms**, including the terminal interval.
- A guarded **single supervised** input-pulse path using existing RR2 authorization/arm artifacts and selected-lab/game identity validation; controller results remain distinct from observed game results.
- User-triggered export of a bounded, redacted historical JSON qualification result; exported evidence is never a present-tense input permit.

Still **not established**:

- A fresh *current* eight-minute successful live observation in AutoFight. Both previously reviewed RR2 reader epochs reported **reader_not_running**; their present state must be rechecked, not assumed.
- Any AutoFight command causing and independently confirming actual RIFT displacement or spellcasting. **No real game input by AutoFight was demonstrated** at this handoff.
- Current-build native memory reading for player/target/ability/camera fields, automated target selection, combat rotations, facing, LOS, movement/navigation, or three-client coordination.
- Current scene/collision geometry. Geometry G1 found historical assets/research, not a calibrated current navmesh.
- A visible desktop session acceptance or clean-machine Windows installation.

A controller HTTP 200, inserted key, fresh XYZ sample, or historical RR2 success alone does **not** prove a completed bidirectional gameplay action.

## 5. Immediate next work — practical functionality first

Joey explicitly wants a **functional** app, not more low-value architecture/process scaffolding. The critical next milestone is a **single-client, bidirectional movement loop**, using already-running RIFT inside VMware Workstation.

1. **Re-establish the baseline.** Read this handoff, AGENTS.md, docs/current-status.md, relevant roadmap/architecture, src/Directory.Build.props; inspect Git status and current Workbridge health. Do not assume prior paths/configs are still live.
2. **Check existing Lab 1 and Lab 2 read-only.** Independently verify RIFT guest and controller identities and current RR2 reader/source-pin status. The WPF default JSON path strings in MainWindow.xaml are historical suggestions, not proof of current pins.
3. **Coordinate a fresh reader/source pin with the RiftReader2 owner.** RR2 owns VM lifecycle, guest credentials, reader renewals and controller grants. Avoid changing that repository or restarting any guest as an incidental AutoFight operation.
4. **Measure sustained source progress.** Run AutoFight's eight-minute qualification on a reviewed fresh pin per lab; retain the redacted report, with explicit success/failure and gap metrics. A pass alone still grants no input authority.
5. **Prove one controlled action/feedback cycle.** After current binding, readiness, foreground/lease/arm and explicit authorization, select the correct lab, send one bounded movement pulse and compare independently attributed before/after game position. Never blindly retry uncertain input.
6. **Then implement actual targeting, casting and measured movement.** Use qualified telemetry and action feedback, explicit target/cast lifecycle, ability profiles, range/facing checks and bounded recovery. Geometry/navigation remains gated on calibrated assets and LOS/collision evidence.
7. Keep functional milestone commits and concise status receipts in **AutoFight**, with independent review and deterministic regression tests. Use maximum genuinely useful available reasoning agents, with exclusive file ownership; do not run cloud/guest Codex CLI.

**Success criterion for the next milestone:** an unambiguous lab/client identity, a reproducibly observed command-to-game-position change, clear recorded timing/limits, tests, documentation and a scoped AutoFight commit. If the live reader is blocked, report the exact blocker and do not label a synthetic result as game control.

## 6. Useful commands (Windows PowerShell)

~~~powershell
Set-Location 'C:\work\AutoFight'
git status --short
dotnet build .\Rift.Automation.slnx -c Release
dotnet run --project .\tests\Rift.Automation.Specs\Rift.Automation.Specs.csproj -c Release -- selftest
dotnet run --project .\tests\Rift.Automation.Windows.Specs\Rift.Automation.Windows.Specs.csproj -c Release
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --smoke-test
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --help
~~~

For **read-only** diagnostics after reviewing actual source/config paths (do not replace placeholders by guesses):

~~~powershell
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --vm-preflight lab1 <reviewed-source-pin.json> <host-config.json>
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --vm-qualify-feedback lab1 <reviewed-source-pin.json>
~~~

The qualification command deliberately takes **eight minutes** only when a valid running source exists; stopped/unbound sources fail promptly. Always capture the actual Workbridge job ID, output, final status and exit code.

## 7. Handoff cautions and ownership

- Work on **C:\work\AutoFight**, not C:\work\Rift API.
- Preserve the older research repository and its five original v0.5.7 WIP file hashes until a separate cleanup decision; it now has redirect commit 752e133.
- Keep all private host config/lease/arm files and raw captures out of Git and out of handoff text.
- No addons, VMs, guest credentials or other repositories were modified by the migration beyond the two documented **Rift API** redirect files.
- Do not claim any off-machine backup, GitHub push, current live reader, game input, visible UI validation or completed combat automation without new evidence.
- Every new session should resume from the **actual current Git HEAD and version**, not assume this checkpoint stays latest.

**The repository-routing error is resolved. The next engineering focus is proving real feedback-driven control, not repeating the migration.**
