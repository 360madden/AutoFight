# AutoFight — Continuation Handoff (2026-11-16 04:33 EST)

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
