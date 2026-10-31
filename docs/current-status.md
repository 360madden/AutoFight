# AutoFight current status

Updated October 31, 2026. **Current source of truth: C:\work\AutoFight, main branch.** App development version: 0.5.7 from src/Directory.Build.props.

## What works now

- A self-contained C#/.NET 10 Windows WPF codebase with offline replay, manual unit TSV import and host RIFT process listing.
- Independent Windows assembly for VMware Lab 1/Lab 2 controller diagnostics, pinned legacy RR2 telemetry read/watch, bound read-only preflight and strictly supervised legacy one-pulse path. Desktop Refresh/Telemetry now share VM operation cancellation/exclusion.
- Fixed 480-second/500-ms source-pinned telemetry qualification with strict advancing-feedback gaps below 4,500 ms. The UI displays state and supports redacted JSON export of a completed/failed/cancelled report when the runner produces one.
- The exact-source collector TSV parser is copied locally into the Windows assembly. Builds no longer compile from C:\work\Rift API.
- Seven required application/test projects, including an AutoFight-owned guest reader executable and 13 deterministic guest wire/scanner/host-contract specs. The six-file AutoFight Lua addon source is bundled in this repo. The solution has no RR2 ProjectReferences and compiles without another checkout; runtime lab deployment has not been performed.
- New in-process authenticated telemetry ingest/HTTP listener and bounded synthetic keyboard executor. They compile and pass regression tests but are not yet connected to a live in-process receiver session or native guest controller.

## Acceptance in the new repository

| Check | Actual result |
| --- | --- |
| dotnet Release solution build | Exit 0; 0 errors/warnings |
| Core synthetic regression suite | 149 passed, 0 failed |
| Windows/VM synthetic, source identity, receiver socket and fixture suite | 141 passed, 0 failed |
| Guest RR2A1/RR2L1 decoder, candidate, config and host JSON roundtrip | 13 passed, 0 failed |
| Nonshown WPF smoke | All five categories pass |

The AutoFight-built 0.5.7 self-contained x64 executable passed direct help and all five nonshown WPF smoke categories. Its path, SHA-256, size and job IDs are in release-autofight-0.5.7-2026-10-31.md. This is not visible UI or clean-machine acceptance.

Current milestone evidence and port provenance are recorded in M1-standalone-guest-port-2026-10-31.md. Migration-era evidence is recorded in MIGRATION-2026-10-31.md. Earlier commits and tests in automation-platform-status.md were actually performed from C:\work\Rift API, not AutoFight.

## Unverified or blocked

- Both historical RR2 source pins were rechecked read-only on October 31 and returned reader_not_running. No fresh live eight-minute pass is claimed. The new guest reader has not been installed or started in either VM.
- No actual RIFT movement/action caused and verified by AutoFight has been demonstrated. The controller adapter is an implementation, not proof of in-game behavior.
- The owned guest native memory scanner now compiles but has not been live-validated. Automatic target/combat rotation, spellcasting, continuous measured movement, collision/LOS pathfinding and three-client combat remain future work.
- Visible desktop click-through and clean-machine installation need separate acceptance.
- A successful telemetry window never grants input authority and a controller HTTP acknowledgment alone never confirms game movement.

## Practical next milestone

1. Compose the AutoFight host receiver/ingest into an explicitly configured, source-pinned one-lab session and display read-only XYZ in AutoFight.
2. With separate lab authorization, deploy the AutoFight-owned guest reader and addon, bind /afbind and prove live XYZ without a running RR2 service; retain source/hash evidence.
3. Run the full eight-minute telemetry qualification and diagnose any freshness gaps. Port the proven RR2 guest controller and host protocol into AutoFight while preserving capture, arm, input-release and identity gates.
4. With separate supervised input approval, issue one bounded movement pulse and independently verify before/after position change. Only then build targeting, casting and navigation.

The original Rift API research repo retains the older manual collector and five unchanged uncommitted v0.5.7 draft files. Its AGENTS.md/README.md now redirect AutoFight development here (source redirect commit 752e133); the historical app files were not deleted. Future AutoFight commits must be made only in the new repository.
