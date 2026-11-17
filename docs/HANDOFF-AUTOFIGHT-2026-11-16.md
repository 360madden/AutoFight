# AutoFight — Continuation Handoff (2026-11-16 04:33 EST)

## Newest offline checkpoint — 2026-11-16 22:07 EST

- **Baseline:** independently verified AMD-DESKTOP, `C:\work\AutoFight`, clean `main` at pushed HEAD `0f3567a`; earlier stale diagnostic fix `69a2929` and docs `0f3567a` both published.
- **Small test-only integration:** New deterministic end-to-end GuestReader AFT2 diagnostic fixture uses fresh `TargetVitalsV2CandidateAccumulator` per synthetic scan, split envelopes and both heap-copy orders, then `TargetVitalsCandidateReporting.Build` → real `LiveReaderSnapshot` `JsonFormat.Options` serialization → `PhysicalTelemetryIngest.Accept`.
- **Observed in synthetic fixtures:** target A HP40 seq2/clock12.5 → HP30 seq3/clock13.5; newer outer receipt with frozen producer remains unqualified; valid mixed A+B copies produce `conflict` and no HP; B-only seq4 produces typed diagnostic; completed empty scan produces `not-found`; B reappears unqualified; unstable identity produces `unavailable` and clears HP; another B-only scan recovers unqualified. No permanent live freshness inferred from either clock.
- **Validation:** 182 Core + 236 Windows + 56 Guest = **474/474** synthetic specs, 7/7 WPF nonshown smoke, .NET 10 Release 0 warnings/errors and clean `git diff --check`. Independent `/root/aft2_stream_audit` reviewed exact test SHA and reported **PASS no P0/P1/P2**; source not changed.
- **Unresolved gate:** Actual `Program` orchestration, true native scanning, network delivery, live AFT2 addon load/progress, source pin, HP/target identity and attack result remain unverified. `TargetVitalsV2Intake` and `TargetVitalsV2Continuity` exist but are only used in tests, not the live program; do **not** mistake their standalone regression coverage for an enforced live producer stream. No RIFT/VM/process-memory/input action during this checkpoint.
- **Next:** commit/push the reviewed synthetic test and handoff, then assess the narrowest production-owned producer progression diagnostic/qualification seam or request a separately scoped read-only physical RIFT validation when offline evidence can no longer advance.

## Prior offline checkpoint — 2026-11-16 22:00 EST

- **Machine / Git baseline:** independently verified AMD-DESKTOP `C:\work\AutoFight`, `main` at clean pushed `03906e0`; no other project or runtime touched.
- **Proven defect reproduced red:** `PhysicalTelemetryIngest.Latest` and `ReadFresh` could expose an earlier unqualified `TargetVitals` candidate after a newer authenticated, exact-pinned, malformed target-health diagnostic was rejected. One new seeded-candidate regression failed on original implementation; subsequent modified code passes.
- **Scoped correction:** after authenticating peer/token and exact reader/process/start/path/hash/nonce plus running status, malformed target-vitals reports with strictly newer outer sequence clear only the last receipt's `TargetVitals`, `TargetVitalsStatus`, and `TargetVitalsSequence`. Prior receipt's accepted sequence/status/UTC remain unchanged. Invalid outer sequence is consumed as replay watermark only when a previous accepted receipt exists, so delayed intermediate valid data cannot resurrect health. Recovery requires a higher valid outer sequence. Invalid auth, wrong bound game, malformed outer envelope, old/replayed sequence, unavailable reader, or null target do not cause rejection-side revocation.
- **Offline evidence:** Windows specs were **234 PASS / 1 expected regression FAIL** before fix, then **236/236 PASS** after two focused tests (malformed tuple/HP/clock/hash/count/status/type, duplicate property, wrong pin/auth, null target, older replay, valid recovery and concurrent order). Core **182/182**, Guest **55/55**, total **473/473**, WPF **7/7**, .NET 10 Release **0 warnings/errors**, `git diff --check` exit 0. Independent elevated `/root/cache_policy_audit` advised scope/fencing, `/root/cache_revoke_review` reviewed exact source+test SHA256 and result jobs: **PASS, no P0/P1/P2**.
- **Limitations:** fully unparseable JSON cannot prove the pinned source or ordering and leaves previous last-valid diagnostics until their prior age expiry; immutable receipt references handed to callers before revocation cannot be changed afterward. Thread tests sample observed schedules; reviewer inspected the lock ordering for both possibilities. This is diagnostic correctness only, not source freshness, live HP, input or combat. No game/VM/process memory action occurred.
- **Integration:** reviewed stale-diagnostic source commit `69a2929` and documentation commit `0f3567a` were pushed and upstream-clean before the subsequent 22:07 test-only checkpoint.

## Prior offline checkpoint — 2026-11-16 21:47 EST

- **Baseline:** AMD-DESKTOP `C:\work\AutoFight` `main`; preceding selector/fast-region code `b2201bc` and docs `1b4dbfc` committed/pushed to GitHub; clean worktree before this follow-up.
- **Scoped-read deadline correction:** `ScanCandidateRegions` previously could report `complete` even if the final native read / candidate collection elapsed beyond its budget. It now builds both candidate lists, checks cancellation, and calls a pure `FinalizeScopedCandidateScan` with a post-materialization monotonic clock reading. Expired results are incomplete `time-budget-exceeded`, keep counters but expose neither RR2A1 nor AFT2 candidates. Exactly equal deadline is allowed.
- **Tests:** 182 Core + 234 Windows + 55 Guest = **471/471** offline specs; **7/7** WPF nonshown smoke; .NET 10 Release build **0 warnings/errors**; `git diff --check` exit 0. An initial concurrently invoked build had a CS2012 assembly-write collision; serial retry passed without source change.
- **Independent gate:** `/root/scoped_deadline_review` independently verified machine, two exact source/test file SHA256 values, actual source call-site and settled test job outputs: **PASS, no P0/P1/P2**. Regression directly exercises the pure finalizer, not a simulated native syscall; guard cannot preempt a blocked synchronous Win32 read.
- **Boundary:** No live RIFT/process scan, VM input/focus, addon reload, qualified HP or combat. Continue offline only; separate live qualification remains a future user-controlled gate.

## Prior offline checkpoint — 2026-11-16 21:40 EST

- **Machine:** verified AMD-DESKTOP, `C:\work\AutoFight` on `main`; prior reviewed provenance commit `bf79992` pushed and synchronized before this new change.
- **AFT2 retained-frame ambiguity:** `TargetVitalsV2CandidateSelector` now rejects a completed candidate set with different producer epochs, player IDs, target IDs, sequence-inverted producer clocks, contradictory copies or more than 256 candidates. Identical duplicates and valid unordered copies still work. This remains candidate-only evidence, never producer freshness or authorization.
- **Fast-scan diagnostic loss repaired offline:** `WindowsMemoryScanner.ScanAttempt` previously remembered only legacy RR2A1-containing regions, so a separate AFT2-only region could disappear during scoped fast scans. Extracted bounded `FeedAndTrackCandidateRegions` feeds both existing accumulators and retains the current+contiguous-tail regions when either finds a valid candidate. No new scan, controller or runtime service; fast mode still requires RR2A1 anchor and obeys existing budgets.
- **Validation:** 182 Core + 234 Windows + 54 Guest = **470/470** deterministic offline specs, **7/7** WPF nonshown smoke, .NET 10 Release **0 warnings/errors**. Selector and scanner independent elevated code reviews both **PASS, zero P0/P1**. One Workbridge output poll timed out, but existing job ID reconciliation verified exit 0 without rerunning it.
- **Comments:** corrected stale Lua/accumulator comments that mistakenly described the AFT2 producer as not loaded/scanned; source code behavior unchanged by those comments.
- **Unresolved gates:** no current live target-health source, verified addon producer progression, action feedback or combat. Mixed historical memory copies can intentionally suppress diagnostics. Scoped scanner's pre-existing final post-decode deadline check remains an offline reliability follow-up. All live RIFT/process/VM input/scan interactions remain untouched in this checkpoint.
- **Next:** independent review is complete; perform scoped Git commit and verify remote before optional push. Then address the bounded scoped-scan deadline condition offline, or schedule a separately identified read-only live qualification when Joey chooses.

## Newest offline checkpoint — 2026-11-16 21:30 EST

- **Machine:** independently verified Workbridge `AMD-DESKTOP` v0.16.5-dev, `C:\work\AutoFight`, clean `main...origin/main` before the new changes; previous pushed HEAD `724ffd1`.
- **AFT2 now survives end-to-end diagnostics:** `TargetVitalsCandidateReporting` retains AFT2 PlayerId, TargetId, HP/max, Epoch, Sequence, raw producer clock and envelope/frame hashes; `PhysicalTelemetryIngest` parses a bounded typed candidate only from a pinned, authenticated running reader/game receipt. Full Guest JSON serialization to actual physical host is covered by an offline test. `PhysicalTargetVitalsCandidate.SourceQualified` is always **false** and no candidate is wired into live combat or input.
- **Clock safety:** `TargetVitalsV2Intake` tracks a shared monotonic invocation-time watermark on accept + freshness checks. Regressed/negative timestamps latch a fault and clear cached health until explicit Reset. Independent `/root/vitals_clock_review` found no blocker; after review, three more isolated query-time tests were added.
- **Verification:** Release .NET 10 solution build 0 warnings/errors; 182 Core + 234 Windows + 46 Guest = **462/462 synthetic specs**, WPF smoke **7/7**, `git diff --check` exit 0. Independent elevated `/root/vitals_provenance_review` **PASS**, no blocking P0/P1; validated exact five source/test hashes and the new Guest serialization → host test. Caveats: older partial-provenance Guest candidate payloads fail closed (only status/sequence-only legacy diagnostics remain compatible); malformed authenticated follow-ups can leave a prior *unqualified* last-valid receipt until its ordinary age expiry. No producer freshness established.
- **Not proved:** live/current addon load and reader bind, passive producer progress/target changes, physical target-health observations, observed attack damage or autonomous combat. All game input, reloads, screen focus and VM actions remained untouched.
- **Next:** close independent review and correct any findings; commit scoped code/docs and inspect remote before optional push. Later, qualify current-build source/producer freshness with a separately approved or Joey-operated read-only experiment. Do not infer health freshness from advancing outer transport sequence or create `CombatObservation` with unknown hostility/range/readiness values.

## Newer offline checkpoint — 2026-11-16 20:50 EST

- **Machine independently verified:** Workbridge `computerName=AMD-DESKTOP`, root `C:\work`, connector v0.16.5-dev. Authoritative repository remains `C:\work\AutoFight`, no sister-repo changes.
- **Local Git:** `main` at `77d10e7` after reviewed commits `9eca9fb` (Storage Guardian hardening), `ac869d7` (integration docs), `649e01e` (monotonic combat preview clock), and `77d10e7` (target continuity). Clean at this checkpoint; remote state/push must be verified separately.
- **Storage Guardian:** independently reviewed and integrated into local main; nested Git worktrees/submodules and UTF-8 tracked names protected, **54/54** PowerShell 5.1 fixture tests, real read-only audit **930.33 MiB Normal**, **526.24 MiB** old generated-output Preview candidates. No real cleanup, automatic scheduling or generated-output deletion. See `docs/STORAGE-GUARDIAN.md`.
- **Combat offline correctness:** planner time-rollback and target-loss/death/swap discontinuity corrections accepted by independent reviewer `/root/combat_audit`. Missing unrelated fields cannot mask the target identity latches. Latest .NET 10 Release build **0 warnings/errors**; **182 Core + 232 Windows + 39 Guest = 453/453 tests**, WPF synthetic smoke **7/7**. Both changes remain **preview-only** (`CanExecute=false`), with no active combat controller or qualified live target data.
- **Live evidence still absent:** no 30-second manual physical A→B trace/M6 replay, qualified current-build live target health/identity/ability readings, live attack/damage attribution, real movement or automated combat. No RIFT/VM input, focus, reload, process memory scan or lifecycle changes occurred during this offline checkpoint.
- **Next meaningful offline work:** audit the already-present AutoFight-owned target-vitals v2 addon/GuestReader/host provenance and find the smallest concrete missing adapter into typed combat observations; choose a testable integration, not another speculative FSM abstraction. Record exact source freshness/identity gates. Live validation requires a separately identified Joey-operated or user-authorized session. Continue small tests, independent review, scoped commits.
- **Scope:** no runtime RR2/RRE/RiftWorld/Workbridge/AI dependencies; those repositories remain references only. Preserve private capture data and Git history.

## Repository and verification
- Target: AMD-DESKTOP Workbridge, `C:\work\AutoFight`. Verify machine identity independently before executing future changes; connector label alone is not hardware proof.
- Git: `main`, clean worktree at this read, `ahead 21` relative to `origin/main`. Exact HEAD hash was not retrieved; obtain it before changes. No push performed in this handoff.
- Source of truth: `AGENTS.md`, `SESSION_HANDOFF.md`, `docs/current-status.md`, `docs/M7-PHYSICAL-XYZ-CLI-CAPTURE-2026-11-15.md`. Older VMware sections are historical, not current runtime proof.
- Do not edit `C:\work\Rift API`, `C:\work\riftreader2`, RRE or RiftWorld incidentally.

## Most recent substantiated capability
- M7: AutoFight-owned read-only native physical RIFT XYZ CLI capture, with exact PID/start binding, source/build/player/age checks, ~1Hz bounded 10–120s capture, cancellation/deadlines, redacted no-overwrite JSON export, and M5 A/B assessment.
- M6: strict canonical saved-JSON import and offline `--physical-xyz-trace-assess` replay.
- M5: deterministic A/B assessment requires three matching A samples, a change, and three matching distinct B samples, same source identity, bounded timing, no invalid history.
- M4: bounded 600-event WPF trace recorder and explicit Save action.
- Last documented acceptance (Nov 15): Release solution build 0 errors/warnings; 149 Core + 215 Windows + 15 Guest = 379/379 tests; 7/7 nonshown WPF smoke; independent review PASS. These are historical recorded results, not tests rerun for this handoff.
- No continuous live user-driven A-to-B capture has been accepted, no proof of producer freshness, no AutoFight-initiated gameplay movement or combat.

## Next practical acceptance
1. Reverify actual AMD-DESKTOP identity, current Git HEAD/worktree, executable build, physical RIFT PID/start/build/player; never reuse historical PID.
2. Review M7 CLI syntax and run `--help`. Use a fresh unique JSON destination. Capture 30 seconds while Joey manually holds the character at A for at least 3 samples, moves briefly, then holds at B for at least 3 samples.
3. Run M6 `--physical-xyz-trace-assess <saved-json-file>` and compare summary to M7/WPF; record source identity, outcome, timing, failure reasons, and artifact location privately. This is observational proof only; it is not proof AutoFight sent movement input.
4. If A/B fails, diagnose native sampling continuity/identity first. Do not infer gameplay success from synthetic tests or unrelated historical readings.
5. Then build toward a minimal closed-loop stationary single-enemy combat assistant: target observation -> one bounded basic attack -> independently observed result -> recovery, with explicit source and input authority. Combat-first directive in AGENTS.md takes precedence over broad navigation. Keep runtime standalone; RR2/RRE/RiftWorld are references, not required services.
6. Each substantive change: focused implementation, deterministic regression tests, independent elevated code/architecture review, documented evidence, scoped commit. Do not push without reviewing remote state and user direction.

## Operational constraints
- No VM start/stop, guest changes, game input, UI focus, /reloadui, or RR2 service reliance as incidental actions.
- Never treat a successful controller command, historical XYZ, or synthetic test as observed game behavior.
- Preserve secrets, private source pins and raw captures outside Git.
- Coordinate useful parallel work with exclusive file ownership; Workbridge command slots are not reasoning agents.
- Keep timestamped SITREPs and state what is verified versus pending.

## Build commands (PowerShell 7 in C:\work\AutoFight)
```powershell
git status --short --branch
git rev-parse HEAD
dotnet build .\Rift.Automation.slnx -c Release
dotnet run --project .\tests\Rift.Automation.Specs\Rift.Automation.Specs.csproj -c Release -- selftest
dotnet run --project .\tests\Rift.Automation.Windows.Specs\Rift.Automation.Windows.Specs.csproj -c Release
dotnet run --project .\tests\Rift.Automation.Guest.Specs\Rift.Automation.Guest.Specs.csproj -c Release
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --smoke-test
```

## Handoff creation provenance
Created from live Workbridge read-only Git status, directory listing, and current AGENTS/SESSION_HANDOFF/current-status contents on 2026-11-16. No code changes, game input, new tests, commits, or GitHub push were performed as part of the evidence collection.
