# Automation platform roadmap

Date: 2026-10-31
Status: dependency-gated plan, not a promise that every research capability is feasible.

## Current execution boundary

The current [master directive v3](automation-platform-master-prompt-v3.md) targets `C:\work\AutoFight`. The [prior v2](automation-platform-master-prompt-v2.md), historically committed in Rift API at `5582152`, mistakenly selected the old repo and is superseded. Its initial audit/design checkpoint is followed by Joey's instruction to continue autonomously. Proceed through reviewed, tested and committed observation-only increments. Do not infer authorization for live game input, addon installation, VM/controller changes or unrelated-repository writes.

The [architecture](automation-platform-architecture.md) governs proposed platform structure. [Status/evidence](automation-platform-status.md) alone records implementation and test completion. The older collector roadmap remains in the separate `C:\work\Rift API\docs\development-roadmap.md` research repository, not this application repository.

Current independent-repository acceptance and the migrated v0.5.7 JSON evidence export are recorded in [current status](current-status.md). Historical commit IDs and test runs below originated in the prior Rift API source repository.

## Recorded progress — 2026-10-30

M0/M1 audit and design are recorded; M2a Core/replay is committed as `9bf4634`, and M2b offline WPF/published observer as `bb8ac5b`. M3a now implements bounded synthetic measurement preparation and refreshes the native/Lua/geometry evidence: current acceptance is 149 platform specs, a fresh 280-test collector suite, clean solution build, nonshown desktop smoke and output-folder tests. M3a does not complete the M3 live-provider gate. Visible UI acceptance remains pending. No live provider, memory reader, target/combat/movement executor or geometry source is claimed. See the [exact status receipt](automation-platform-status.md), [analysis contract](automation-platform-replay-analysis-v1.md) and [provider feasibility](automation-platform-provider-feasibility.md).

M3b: Implemented and tested on-demand Windows RIFT process discovery plus a Windows desktop client list and selection details. A running RIFT client was not observed during the check; game-state acquisition is not qualified. This is a practical observation-only increment; the next functional gate is one current-build, verified data source, not additional speculative framework work.

M3c: The desktop can now import one manual RAC unit export and show real fields from a saved file while retaining missing-field and no-timestamp semantics. This is practical offline inspection—not continuous telemetry or qualified native observation. The next gate remains actual game-state freshness/identity validation for a current-build provider; avoid further framework-only slices without an identified use.

The existing VMware RIFT-Lab-1 and RIFT-Lab-2 guests **are running RIFT** as independently checked on October 30 at 21:31 EDT. The M3b client list enumerates host Windows processes only. Prefer read-only observation experiments on these existing labs, coordinated with the RiftReader2 lab owner; do not require another host RIFT launch or treat guest VM PIDs as game PIDs. Retain separate game/session identities, lab scope and data-provider qualification. This does not grant gameplay input, controller management or installed-addon changes.

M4a bidirectional groundwork: implemented and live-tested C# connections to *both* authenticated host controllers. The app can now transmit one supervised, bounded command using a preexisting RR2 grant and arm artifact, while controller status confirms the guest connection. The command was tested against an in-memory fake host; **no real game input has been sent by AutoFight**, and native XYZ/other feedback remains unqualified. Next build on actual guest telemetry and close the command→observed-feedback loop before adding combat rules or more framework.

M4b functional telemetry integration: AutoFight now consumes actual pinned RR2 receiver files and exposes XYZ only when timing, reader, game and source identity checks pass. Both sampled readers were terminated; no current position or input was accepted. The GUI input path requires fresh pre-action telemetry and observes the next matching position after controller acknowledgment. **To unlock real movement, first coordinate fresh reader/source-pin reactivation with the RR2 owner, then demonstrate advancing sustained feedback and a single bounded action.** Do not expand unrelated framework before this gate.

M4c optimized-scoped compatibility: RR2's candidate-region movement observation format and its anchored validity checks are now implemented directly in C#, including dual-source age/sequence reconciliation. Both VM controllers and guests answer, but the stopped readers and a previously failed Lab1 sustained-gap acceptance keep action feedback unqualified. **Next useful step is renewed running telemetry with a fresh source pin**; defer new combat or navigation features until the one-client command/feedback loop is demonstrated.

M4d monitoring: a functional Start/Stop two-VM live watch and shared C# continuity tracker distinguish real sequence progress from re-delivery, reader swaps, source loss and time regressions. This makes upcoming reader renewal measurable in AutoFight without modifying RiftReader2. **M3/M4 live qualification remains blocked on fresh reader/source pins and an uninterrupted accepted observation window.** After that, test one bounded supervised movement command with independent before/after position feedback.

M4e watch hardening (October 31): corrected outer-transport watermark tracking across accepted repeated producer frames, measured closed no-advance intervals before progression resets, and bounded the headless CLI delay at its deadline. Six added regressions and correction of one prior gap oracle brought Windows/VM acceptance to **48/48** with Core **149/149** and clean Release build; see [M4e receipt](automation-platform-status.md). Both lab pins still report terminated readers and no new XYZ, so the next functional step is unchanged: RR2 owner renews and reviews the live reader/source pins, then this app independently measures sustained advancement. A testable native Windows read component remains a future avenue, not a replacement for this evidence gate.

M4f diagnostic epoch hardening (October 31): a pinned receiver stream's addon epoch change no longer bypasses the transport high-water mark; retired epochs cannot restore current XYZ even inside newer receiver envelopes. Three new deterministic regressions pass (Windows/VM 51/51); no real telemetry was renewed. Subsequent bounded safety work is recorded as M4g receipt attribution, M4h complete-response deadline, M4i action outcome classification and M4j post-dialog identity/feedback continuity. These are prerequisites to, not substitutes for, fresh two-lab M4 feedback qualification.

M4g watch recovery on receipt loss (October 31): pin identity is validated before receipt I/O/parse, then known lab attribution survives missing, malformed, empty and oversized receipts; malformed pins cannot claim a lab. Deterministic unavailable→restored-receipt and invalid-pin precedence tests pass (Windows/VM 55/55). The CLI can now continue its timed watch on a valid pin during temporary receipt loss, although no fresh live reader was started. The complete controller deadline is now completed in M4h. The controller action-body gate is completed in M4i, the WPF confirmation/feedback recheck in M4j, and live feedback still requires RR2 owner renewal.

M4h complete controller request deadline (October 31): a single fixed five-second token now covers HTTP headers and streaming body, preventing indefinite pending UI state on cooperative stalled responses; cancellation and ambiguous delivery remain explicit and no retry is issued. A deterministic synthetic stream/clock regression and caller cancellation check pass (Windows/VM 57/57). Action response-schema validation is completed in M4i, and dialog/telemetry continuity in M4j. No live game action or reader activation was performed.

M4i actual controller action acknowledgment (October 31): live RR2 source confirms HTTP 200 can carry failed or uncertain guest executor results. The C# bridge now requires clean insertion and release, no cancellation/target loss/uncertainty/disarm/recovery/error, and exact nonduplicated fields; the old invented positive fixture was replaced. Synthetic HTTP200 adversarial tests pass (Windows/VM 60/60). The app never equates guest-reported insertion with RIFT response. The dialog/feedback continuity scope is addressed by M4j; live movement still awaits current reader/source evidence.

M4j post-confirmation continuity and feedback attribution (October 31): one supervised pulse now rechecks a fresh pinned source after a positive modal confirmation; changed reader/player/epoch/lab, stale or regressed observations abort before submission. The button is locked before prompting, request file paths are captured once, and any exception after a send attempt is conservatively ambiguous. Post-ack feedback runs through the existing continuity tracker (including redelivery high-water marks) and requires a matching source, lab and strict transport/producer sequence and clock advance. The additional synthetic checks bring Windows/VM acceptance to **71/71** with Core **149/149** and clean WPF/Release acceptance. No input or VM changes occurred. Next bounded safety question: the selected lab is not independently bound to an editable host config path; verify the RR2 status/config identity contract before permitting a real single pulse. The broader functional gate remains fresh sustained readers plus approved real feedback measurement.

M4k selected-lab binding (October 31): the pulse path now requires selected Lab 1/2 to match RR2's authoritative coordinated VM resource, configured guest IP/VMX and authenticated host/guest game-process identity, with held/ready controller and clean arm/foreground/input state. A final source reread must preserve the entire controller-verified IP/PID/start/path/hash tuple as well as ordered stream continuity. The old unbound CLI `--vm-pulse` syntax is removed and the version is 0.5.3. Offline fake-controller regressions pass (Windows/VM 83/83); no real action or VM change. This closes the known wrong-lab preflight gap but does not independently qualify the RR2 controller's epoch or moving actor; the immediate live gate remains owner-renewed readers and sustained feedback.

M4l read-only diagnostic (October 31): expose the strict bound preflight as `--vm-preflight <lab> <source-pin> <config>` and a two-lab WPF button without input, lease/arm access or automatic retries. Identity match and action readiness are reported separately; both source/config selections are captured once, a post-status source reread prevents stale green results, and the UI prevents preflight/pulse overlap and cancels checks on close. Final synthetic acceptance is Windows/VM 93/93, Core 149/149, .NET 10 Release build and WPF smoke clean with three independent reviewers accepting. Both historical reviewed lab pins were checked read-only and still report `reader_not_running` with expected exit 2. This is useful for inspecting the stopped-reader/guest-disarmed blockers; it is not live movement qualification or a cached action permit.

M4m eight-minute feedback evidence (October 31): a standalone, read-only `--vm-qualify-feedback <lab> <source-pin>` CLI now measures a fixed **480,000 ms** source-pinned window with 500 ms target polls and requires every accepted-advance gap strictly **below 4,500 ms**, including its terminal tail. Bounded temporary not-fresh observations are allowed only with validated source/game/player/epoch provenance and producer timing; the full binding and transport/producer watermarks are checked even during unavailable frames. Invalid timing, malformed/changed sources, rollback, cancellation and incomplete windows cannot pass. Offline 480s clock simulation and parser-backed adversarial cases pass (**112/112 Windows/VM specs**, Core **149/149**, clean Release build/smoke), but both actual historical reader pins still exit 2 as `reader_not_running`. No live 480s run, controller contact, VM change or game input occurred. This is an evidence tool for the RR2 owner's fresh pin renewal, **not** movement permission or gameplay-feedback qualification.

M4n WPF qualification (October 31): the fixed eight-minute, single-lab telemetry gate is now available in the Windows app with redacted authoritative aggregate progress, Start/Stop, captured source selection and strict window-local exclusion among watch/preflight/pulse/qualification. Cancellation holds the operation until the file-reader settles, and pulse GET/POST/follow-up delays now accept the shared cancellation token. Deterministic WPF smoke plus Windows/VM 115/115, Core 149/149 and a zero-warning Release build passed; three independent source reviews accepted. This does not activate a reader, establish a live eight-minute pass or authorize movement. **Next:** owner-reviewed fresh source pins and a real uninterrupted window for each lab, followed by a separately supervised action check. Meanwhile improve actual Windows usability/package deliverables rather than expanding unverified combat/navigation internals.

M4o single-executable Windows package (October 31): a fresh, 0.5.6 `win-x64`, self-contained .NET 10 WPF single-file publish produces only one 139,968,677-byte EXE (SHA-256 `79cb7839013c8d397ec5dab05be94c1307560658be79b5068794739df06fa57a`). The actual published EXE passed direct help, all five nonshown UI smoke groups and a saved-fixture inspection from another cwd; invalid and malformed action syntax were rejected. [Release receipt](windows-release-m4o-2026-10-31.md) records the path, reproducible command, validation and limitations. This is an unsigned local deliverable with historical default RR2 path strings, not public/clean-machine installation proof. **Next offline app feature:** a user-triggered, redacted, saveable 480-second evidence report, while waiting for RR2-owner-reviewed live pins.

G1 geometry feasibility (October 31): the bounded local repo audit found only historical X/Z proximity graphs and unqualified old-build native collision casts—not a current scene mesh. Primary RIFT asset documentation and TelaraFly's world/placement loader reveal a concrete **offline NIF/CDR extraction** route, but the viewer skips collision-only NIF entries and derives Unity colliders from rendered meshes. The [G1 evidence and bounded G2 experiment](automation-platform-geometry-feasibility-2026-10-31.md) make one current-build terrain+collision fixture, hashes and independent coordinate calibration the next prerequisite before selecting DotRecast or claiming obstacle avoidance. M9 remains blocked; M4 reader/source-pin renewal and single-client feedback remain the immediate live-work gate.

## Start-to-finish milestones

| ID | Dependencies | Deliverable | Exit gate / failure branch |
| --- | --- | --- | --- |
| M0 Evidence baseline | None | Preserve collector/protocols; inventory relevant repos; exact build and source-evidence ledger | No stale offset or historical test promoted to current proof; current/installed/active identities distinct |
| M1 Architecture and contracts | M0 | Lean ADRs, ownership/time/capability contracts, risk register, support assumptions and full roadmap | Six specialist reviews reconciled; no runtime changes; current docs precedence unambiguous |
| M2 Offline observer foundation | M1 | Minimal Core/Windows/Desktop projects as needed, synthetic observation/replay pipeline, bounded diagnostics, tests and CLI/help entry if useful | Builds on declared .NET 10; deterministic stale/restart/identity tests; no gameplay executor; UI labels synthetic/replay data; current-source receipt and independent review |
| M3 Observation feasibility | M1; use M2 harness | Timeboxed native-field and Lua-transport experiments; choose one useful qualified provider, not both as mandatory | Current-build identity, semantics, changed-state/restart proof, source-age/error/CPU measurements; if neither passes remain replay-only and record blocker |
| M4 One-session live observer | M2 + one M3 provider | Explicit session selection, provider to typed state to diagnostic UI; optional source comparison | Approved observation-only live test; restart/source-loss/contradiction gates; no research-repo runtime dependency; not an automation release |
| G Geometry feasibility (parallel) | M1 | Provenance/extraction and coordinate calibration; layered geometry/query fixtures | Run early beside M2-M4; timebox before choosing mesh stack; absent geometry parks M9, not the observer |
| M5 Action safety and confirmation | M4 | Single global lease/owned-key ledger; arm/disarm, manual takeover, deadlines, ambiguous outcomes | Fake executor adversarial tests first; then separately approved one narrow real action and observed feedback; focus race limits explicit |
| M6 Target lifecycle and casting | M5 + qualified required fields | Eligibility-before-scoring; acquire/retain/switch/release; cast attempts and deterministic class priorities | Target death/loss, ID reuse, no-plan, moving cast, event gaps, instant spells, late feedback and bounded retries; actual selected target/cast evidence |
| M7 Measured movement | M5 + qualified pose/control mapping | Short open-terrain control, turn/follow/range behavior and bounded stuck recovery | Calibrated keys/yaw or explicitly limited pulse mode; actual displacement and arrival; stale feedback stops control; no 3D route claims |
| M8 Integrated single-client profiles | M6 + M7 | Combat/movement resource coordination, role/profile changes and explainable decisions | Recorded representative encounters and combined-fault replays; no approach/cast/reacquire loops; supported contexts documented |
| M9 Geometry-backed navigation | G passed + M7 | Mesh/corridor planner, independent LOS/clearance, layered routes, dynamic obstacles/replanning | Bridge/underpass/stairs/cliffs/partial route and stale revision tests plus approved held-out live routes; if G fails retain explicitly limited steering/routes |
| M10 Three-client operation | M4 + accepted single-client action features | Isolated state/profiles/telemetry and globally serialized input | Cross-session/PID/window/cancellation fault tests, measured scheduling fairness/age; no simultaneous foreground-input guarantee |
| M11 Native expansion / Lua reduction | M3-M4; per-field dependencies | Current-build native XYZ/orientation/camera/ability/event coverage; parity matrix | Current-build semantic, missingness, timing-tail, restart and native-only capability tests per field; Lua retirement optional, never calendar-driven |
| M12 Product hardening and release | Accepted feature subset of M8-M11 | Packaging, migration/rollback, diagnostics, user guide, reproducible build; optional radar/overlay | Declared capability matrix, clean-checkout build/run, regression/perf/recovery, source/package/installed/observed-active receipt and no research runtime files |

Testing, performance observation, documentation, recovery and packaging checks accompany every milestone. M12 is their final release gate, not their first appearance. Radar may start as a simple 2D diagnostic consumer; a 3D overlay waits for qualified camera/projection data.

## First implementation slice

Implement M2, not fake M3:
1. An immutable session identity and typed observation of a small useful value, with explicit synthetic origin and monotonic acquisition time.
2. A deterministic state reducer/view projection handling session reset, unsupported build, invalid/stale data, equal fresh values and late prior-session messages.
3. A bounded scenario/replay reader and headless acceptance harness. Source and receive age remain distinct.
4. A minimal WPF observer showing mode, session/build, field value/status/source/age and reason a capability is unavailable.
5. A Windows observation boundary with no native offsets or gameplay input. Live process discovery, if implemented, is opt-in and does not mean native field support.
6. Exact build/test commands and independent review. Keep raw captures/generated outputs ignored.

No target/combat/navigation stubs pretending to be working features. Implement only seams needed by this slice. No real input implementation or import in the first runnable host. A successful replay is evidence of the core, not of RIFT compatibility.

## Feasibility work packages

**Native field:** select one current-build field; verify exact binary and typed locator, changed-state checks against an independent reference, region/partial/torn reads and restart/reused PID. Do not reuse the old proven RVA on the updated build. A scan-based discovery result is not the hot-path sampler.

**Lua transport:** keep the manual collector unchanged; compare minimal optical/native-readback candidates on payload/fidelity/latency/CPU and reset/occlusion behavior. Validate heartbeat and stream sequence, not just CRC. Prove bounded event capacity or mark gaps. No background addon installation without its own gate.

**Geometry:** first provenance and actual available triangles/collision semantics, then units/axes/scene calibration and layered test area. Evaluate DotRecast only against that verified input. Choose a finite experiment budget before live work; report a useful go/no-go result rather than indefinite research.

## Per-milestone acceptance record

Each completed milestone must record:
- exact source commit and file hashes where appropriate;
- observed toolchain/client/build and whether data are synthetic, historical or current live;
- commands, job IDs, actual terminal state/exit codes and test counts;
- relevant success/failure/cancellation/overflow/restart cases;
- independent review disposition and unresolved limitations;
- performance sample size/environment where material;
- deploy/active identity and rollback only if separately performed;
- next bounded scope and approval needed.

A submitted job is not a passed test. A clean build is not a live validation. A local Git commit is not an off-machine backup.

## Ownership and parallel work

Use the maximum useful reasoning concurrency actually exposed by the session; prefer six specialists plus lead when available. M2 exposed four total slots, so the lead used three worker lanes. Do not confuse command-job capacity with agents. Keep exclusive source ownership and stable contracts before parallel implementation. Suggested lanes: Core/state contracts; Windows providers; Lua/transport; desktop/replay tooling; tests/performance; independent reviewer. The lead owns integration, docs/status/roadmap and commits. Reassign idle slots to independent review of real changes; do not create duplicate or speculative work to fill a quota.

Research repos and installed addons are read-only references. Never run cloud/guest Codex jobs. Model routing follows `AGENTS.md`; record requested and exposed actual settings honestly.

## Release completion versus scope reduction

The desired final application includes combat, targeting, movement, navigation and multiclient operation. It is only complete for the capability set actually demonstrated. If current-build observations, transport, geometry or input guarantees are unavailable, explicitly narrow or block the corresponding feature; do not rename a partial implementation as completed automation.
