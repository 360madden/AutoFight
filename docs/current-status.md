# AutoFight current status

> **November 6, 2026 update:** [Detailed Lab1/Lab2 handoff](HANDOFF-AUTOFIGHT-VMWARE-LABS-2026-11-06.md) supersedes older dated network, permissions and UI-readiness notes below. The exact scoped host firewall rule now permits verified TCP 48761 from both labs; both Codex guest user defaults are saved as full access/no approval. Lab1 installed addon/reader bytes do not yet prove a loaded in-game addon or live XYZ; Lab2 addon contents remain unverified. Fresh Lab1 screenshot review denied the obscured toolbar reload, and no movement has been measured. The remaining November 5 status is historical.

Updated November 5, 2026. **Current source of truth: C:\work\AutoFight, main branch.** App development version: 0.5.7 from src/Directory.Build.props.

## What works now

- A self-contained C#/.NET 10 Windows WPF codebase with offline replay, manual unit TSV import and host RIFT process listing.
- Independent Windows assembly for VMware Lab 1/Lab 2 controller diagnostics, pinned legacy RR2 telemetry read/watch, bound read-only preflight and strictly supervised legacy one-pulse path. Desktop Refresh/Telemetry now share VM operation cancellation/exclusion.
- Fixed 480-second/500-ms source-pinned telemetry qualification with strict advancing-feedback gaps below 4,500 ms. The UI displays state and supports redacted JSON export of a completed/failed/cancelled report when the runner produces one.
- The exact-source collector TSV parser is copied locally into the Windows assembly. Builds no longer compile from C:\work\Rift API.
- Seven required application/test projects, including an AutoFight-owned guest reader executable and 15 deterministic guest wire/scanner/host-contract specs. The six-file AutoFight Lua addon source is bundled in this repo. The solution has no RR2 ProjectReferences and compiles without another checkout; Lab 1 guest installation is verified; live reader operation remains unverified.
- AutoFight-owned in-process authenticated telemetry ingest/HTTP listener and a disposable, exact-identity host session; `--owned-telemetry <private-config.local.json> <seconds>` now runs a read-only, 1–120-second CLI XYZ watch independent of RR2 services. An isolated WPF panel supports explicitly starting/stopping the same AutoFight-owned listener and displays valid XYZ, age, sequence, and source state; it clears stale XYZ. The bounded synthetic keyboard executor is not a deployed guest controller.
- November 5 Lab 1 installation: independent AutoFight-owned cached-credential VMware transport, read-only VM/game identity preflight, 8/8 staged file hashes (stage ID `417a1d6f024143e3a7ad1bb02d715979`), and verified absent-only promotion (job `af-lab1-owned-verified-install-20261105-c3`, exit 0). Independent postinstall checks confirmed exactly two reader files and six addon assets, all hashes matching, with no reader launch or UI reload. The new GuestReader EXE SHA-256 is `2b75222f09d0735e0f09cdf18e9f8e271f1a3e4ad7620c93bde950162d9f7369`.
- AutoFight-owned bounded Lab 1 console helper (`scripts/lab1_chat_console.py`), pinned guest-reader launcher, fresh guest-snapshot fetcher and private host-config bootstrap are implemented and tested. The reviewed console supports only exact Lab 1 window focus (120,8), one `Enter` to open chat, `/reloadui`, and `/afbind <32hex>`; each operation requires its own captured-screen/manual review and identity preflight. The new `--owned-qualify-telemetry` CLI independently enforces a 30-second warmup and 480-second strict observed-feedback window without RR2 runtime services or gameplay input.

## Acceptance in the new repository

| Check | Actual result |
| --- | --- |
| dotnet Release solution build | Exit 0; 0 errors/warnings |
| Core synthetic regression suite | 149 passed, 0 failed |
| Windows/VM synthetic, source identity, receiver socket, private config and standalone 480s qualification | 157 passed, 0 failed (November 5) |
| Guest RR2A1/RR2L1 decoder, candidate, config and host JSON roundtrip | 15 passed, 0 failed |
| Nonshown WPF smoke | All six categories pass, including standalone receiver panel |
| AutoFight-owned guest staging, console, reader launch, snapshot fetch and host-config scripts | 58 Python tests passed; 8/8 installed file SHA-256 matches in Lab 1 (November 5) |

The AutoFight-built 0.5.7 self-contained x64 executable passed direct help and all five nonshown WPF smoke categories. Its path, SHA-256, size and job IDs are in release-autofight-0.5.7-2026-10-31.md. This is not visible UI or clean-machine acceptance.

A November 5, 2026 synthetic integration test now sends HTTP observations through two independent AutoFight-owned in-process socket listeners, verifies exact XYZ, wrong-token and wrong-game rejection, unavailable/stale demotion and cancellation (commit 09ad023). This is not live RIFT evidence. Current port provenance is recorded in M1-standalone-guest-port-2026-10-31.md. Migration-era evidence is recorded in MIGRATION-2026-10-31.md. Earlier commits and tests in automation-platform-status.md were actually performed from C:\work\Rift API, not AutoFight.

## Unverified or blocked

- Authenticated November 5 checks pinned both VMware guests' RIFT process IDs, process start ticks, executable paths and SHA-256. Lab 1's AutoFight-owned GuestReader and six-file Lua addon were installed from an eight-file hash-verified package, with independent final-directory hashes. GuestReader has **not** been started, and the game has **not** loaded or bound the new addon; Lab 2 remains without AutoFight. Historical RR2 source pins returned reader_not_running on October 31. No fresh AutoFight XYZ or eight-minute live pass is claimed.
- No actual RIFT movement/action caused and verified by AutoFight has been demonstrated. The controller adapter is an implementation, not proof of in-game behavior.
- The owned guest native memory scanner now compiles but has not been live-validated. Automatic target/combat rotation, spellcasting, continuous measured movement, collision/LOS pathfinding and three-client combat remain future work.
- Live Lab 1 VNC screenshots were reviewed and exact VM/game identity remained pinned. Two initial reviewed `Enter` chat-open attempts and one final reviewed attempt, plus one bounded RIFT guest-window titlebar focus click `(120,8)`, completed with key/button release, but no screenshot proved an open focused empty chat entry. **No `/reloadui` or `/afbind` slash command was sent; GuestReader remains not started; no current live XYZ.** The guest input route must be re-established with visible chat verification before resuming. Physical RIFT and Lab 2 were untouched.
- A bounded guest-to-host TCP diagnostic temporarily listened on `192.168.56.1:48761` and received no Lab 1 peer. The listener closed; guest PowerShell control succeeded. Windows Firewall default inbound block with no visible AutoFight allow rule is the leading explanation, not packet-level proof. Narrowly scoped firewall change awaits separate user approval and verification of VMnet8's effective network profile; no firewall change was made.
- Visible desktop click-through and clean-machine installation need separate acceptance.
- A successful telemetry window never grants input authority and a controller HTTP acknowledgment alone never confirms game movement.

## Practical next milestone

1. The standalone host receiver is wired into CLI/WPF diagnostics and a new 480-second owned qualifier. Lab 1 game identity, private guest config, VMnet8 addresses, receiver port and installed components are ready. First restore/verify a *visibly focused, empty RIFT chat entry* in Lab 1 (or ask Joey to enter `/reloadui` manually); one approved UI reload is pending, not yet performed. Do not send the slash command into an unverified UI state. Test guest-to-host TCP after explicit authorization for any needed narrowly scoped firewall rule, then start GuestReader and capture the new reader instance/nonce.
2. With separately approved Lab 1 in-game binding input, enter /afbind with the current reader nonce and prove live XYZ without a running RR2 service; retain source/hash evidence.
3. Run the full eight-minute telemetry qualification and diagnose any freshness gaps. Port the proven RR2 guest controller and host protocol into AutoFight while preserving capture, arm, input-release and identity gates.
4. With separate supervised input approval, issue one bounded movement pulse and independently verify before/after position change. Only then build targeting, casting and navigation.

The original Rift API research repo retains the older manual collector and five unchanged uncommitted v0.5.7 draft files. Its AGENTS.md/README.md now redirect AutoFight development here (source redirect commit 752e133); the historical app files were not deleted. Future AutoFight commits must be made only in the new repository.
