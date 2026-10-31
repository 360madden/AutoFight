# AutoFight current status

Updated October 31, 2026. **Current source of truth: C:\work\AutoFight, main branch.** App development version: 0.5.7 from src/Directory.Build.props.

## What works now

- A self-contained C#/.NET 10 Windows WPF codebase with offline replay, manual unit TSV import and host RIFT process listing.
- Independent Windows assembly for VMware Lab 1/Lab 2 controller diagnostics, pinned RR2 telemetry read/watch, bound read-only preflight and strictly supervised one-pulse path.
- Fixed 480-second/500-ms source-pinned telemetry qualification with strict advancing-feedback gaps below 4,500 ms. The UI displays state and supports redacted JSON export of a completed/failed/cancelled report when the runner produces one.
- The exact-source collector TSV parser is copied locally into the Windows assembly. Builds no longer compile from C:\work\Rift API.
- Five required application/test projects; no installed addon or external research checkout is needed to compile or open the offline demo.

## Acceptance in the new repository

| Check | Actual result |
| --- | --- |
| dotnet Release solution build | Exit 0; 0 errors/warnings |
| Core synthetic regression suite | 149 passed, 0 failed |
| Windows/VM synthetic and fixture suite, including evidence export | 119 passed, 0 failed |
| Nonshown WPF smoke | All five categories pass |

The AutoFight-built 0.5.7 self-contained x64 executable passed direct help and all five nonshown WPF smoke categories. Its path, SHA-256, size and job IDs are in release-autofight-0.5.7-2026-10-31.md. This is not visible UI or clean-machine acceptance.

Exact Workbridge job IDs and provenance are recorded in MIGRATION-2026-10-31.md. Earlier commits and tests in automation-platform-status.md were actually performed from C:\work\Rift API, not AutoFight.

## Unverified or blocked

- Both historical RR2 source pins previously reported stopped readers. They must be refreshed by the RiftReader2 owner and rechecked; no new live eight-minute pass is claimed here.
- No actual RIFT movement/action caused and verified by AutoFight has been demonstrated. The controller adapter is an implementation, not proof of in-game behavior.
- Native game memory acquisition, target/combat rotation, spellcasting, continuous measured movement, collision/LOS pathfinding and three-client combat remain future work.
- Visible desktop click-through and clean-machine installation need separate acceptance.
- A successful telemetry window never grants input authority and a controller HTTP acknowledgment alone never confirms game movement.

## Practical next milestone

1. Obtain owner-reviewed fresh telemetry source pins for Lab 1 and Lab 2 (without changing other repos as a side effect).
2. Run a full independent eight-minute telemetry qualification per lab using this AutoFight build.
3. Once identities and permissions are current, conduct one separately supervised bounded movement pulse in a chosen lab; compare independent before/after game position.
4. Only after that closed-loop control proof, implement live target selection, casting and measured movement in small functional slices.

The original Rift API research repo retains the older manual collector and five unchanged uncommitted v0.5.7 draft files. Its AGENTS.md/README.md now redirect AutoFight development here (source redirect commit 752e133); the historical app files were not deleted. Future AutoFight commits must be made only in the new repository.
