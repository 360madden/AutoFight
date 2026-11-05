# AutoFight current status

Updated November 5, 2026. **Current source of truth: C:\work\AutoFight, main branch.** App development version: 0.5.7 from src/Directory.Build.props.

## What works now

- A self-contained C#/.NET 10 Windows WPF codebase with offline replay, manual unit TSV import and host RIFT process listing.
- Independent Windows assembly for VMware Lab 1/Lab 2 controller diagnostics, pinned legacy RR2 telemetry read/watch, bound read-only preflight and strictly supervised legacy one-pulse path. Desktop Refresh/Telemetry now share VM operation cancellation/exclusion.
- Fixed 480-second/500-ms source-pinned telemetry qualification with strict advancing-feedback gaps below 4,500 ms. The UI displays state and supports redacted JSON export of a completed/failed/cancelled report when the runner produces one.
- The exact-source collector TSV parser is copied locally into the Windows assembly. Builds no longer compile from C:\work\Rift API.
- Seven required application/test projects, including an AutoFight-owned guest reader executable and 15 deterministic guest wire/scanner/host-contract specs. The six-file AutoFight Lua addon source is bundled in this repo. The solution has no RR2 ProjectReferences and compiles without another checkout; Lab 1 guest installation is verified; live reader operation remains unverified.
- AutoFight-owned in-process authenticated telemetry ingest/HTTP listener and a disposable, exact-identity host session; `--owned-telemetry <private-config.local.json> <seconds>` now runs a read-only, 1–120-second CLI XYZ watch independent of RR2 services. An isolated WPF panel supports explicitly starting/stopping the same AutoFight-owned listener and displays valid XYZ, age, sequence, and source state; it clears stale XYZ. The bounded synthetic keyboard executor is not a deployed guest controller.
- November 5 Lab 1 installation: independent AutoFight-owned cached-credential VMware transport, read-only VM/game identity preflight, 8/8 staged file hashes (stage ID `417a1d6f024143e3a7ad1bb02d715979`), and verified absent-only promotion (job `af-lab1-owned-verified-install-20261105-c3`, exit 0). Independent postinstall checks confirmed exactly two reader files and six addon assets, all hashes matching, with no reader launch, UI reload, or game input. The new GuestReader EXE SHA-256 is `2b75222f09d0735e0f09cdf18e9f8e271f1a3e4ad7620c93bde950162d9f7369`.

## Acceptance in the new repository

| Check | Actual result |
| --- | --- |
| dotnet Release solution build | Exit 0; 0 errors/warnings |
| Core synthetic regression suite | 149 passed, 0 failed |
| Windows/VM synthetic, source identity, receiver socket and private config suite | 150 passed, 0 failed (November 5) |
| Guest RR2A1/RR2L1 decoder, candidate, config and host JSON roundtrip | 15 passed, 0 failed |
| Nonshown WPF smoke | All six categories pass, including standalone receiver panel |
| AutoFight-owned guest staging and installation | 15 Python specs; 8/8 final SHA-256 file matches in Lab 1 (November 5) |

The AutoFight-built 0.5.7 self-contained x64 executable passed direct help and all five nonshown WPF smoke categories. Its path, SHA-256, size and job IDs are in release-autofight-0.5.7-2026-10-31.md. This is not visible UI or clean-machine acceptance.

A November 5, 2026 synthetic integration test now sends HTTP observations through two independent AutoFight-owned in-process socket listeners, verifies exact XYZ, wrong-token and wrong-game rejection, unavailable/stale demotion and cancellation (commit 09ad023). This is not live RIFT evidence. Current port provenance is recorded in M1-standalone-guest-port-2026-10-31.md. Migration-era evidence is recorded in MIGRATION-2026-10-31.md. Earlier commits and tests in automation-platform-status.md were actually performed from C:\work\Rift API, not AutoFight.

## Unverified or blocked

- Authenticated November 5 checks pinned both VMware guests' RIFT process IDs, process start ticks, executable paths and SHA-256. Lab 1's AutoFight-owned GuestReader and six-file Lua addon were installed from an eight-file hash-verified package, with independent final-directory hashes. GuestReader has **not** been started, and the game has **not** loaded or bound the new addon; Lab 2 remains without AutoFight. Historical RR2 source pins returned reader_not_running on October 31. No fresh AutoFight XYZ or eight-minute live pass is claimed.
- No actual RIFT movement/action caused and verified by AutoFight has been demonstrated. The controller adapter is an implementation, not proof of in-game behavior.
- The owned guest native memory scanner now compiles but has not been live-validated. Automatic target/combat rotation, spellcasting, continuous measured movement, collision/LOS pathfinding and three-client combat remain future work.
- Visible desktop click-through and clean-machine installation need separate acceptance.
- A successful telemetry window never grants input authority and a controller HTTP acknowledgment alone never confirms game movement.

## Practical next milestone

1. The standalone host receiver is wired into CLI and WPF diagnostics. Lab 1 exact game identity, private guest config, VMnet8 addresses, receiver port, and both installed guest components are ready. With separate authorization, reload Lab 1 RIFT's UI to load the new addon; then start GuestReader and capture its new reader instance/nonce.
2. With separately approved Lab 1 in-game binding input, enter /afbind with the current reader nonce and prove live XYZ without a running RR2 service; retain source/hash evidence.
3. Run the full eight-minute telemetry qualification and diagnose any freshness gaps. Port the proven RR2 guest controller and host protocol into AutoFight while preserving capture, arm, input-release and identity gates.
4. With separate supervised input approval, issue one bounded movement pulse and independently verify before/after position change. Only then build targeting, casting and navigation.

The original Rift API research repo retains the older manual collector and five unchanged uncommitted v0.5.7 draft files. Its AGENTS.md/README.md now redirect AutoFight development here (source redirect commit 752e133); the historical app files were not deleted. Future AutoFight commits must be made only in the new repository.
