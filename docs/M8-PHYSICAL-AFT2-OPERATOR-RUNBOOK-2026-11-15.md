# M8 Physical RIFT AFT2 binding runbook (2026-11-15)

## Scope and non-negotiable identity checks
- Run only on **AMD-DESKTOP**, from `C:\work\AutoFight`.
- Discover the current physical RIFT windows and identify the intended **PID and HWND** afresh. Historical PID `37928` is not an enduring identity; never blindly reuse it.
- Do not use LEGION, VMware, RiftReader2 services, or a different RIFT client.
- Do not infer successful addon loading from file existence or timestamps. `/reloadui` must be confirmed in the intended game client.
- No movement, casting, or combat input in this telemetry-only milestone.

## Prerequisites
1. Confirm the canonical AutoFight addon directory in the active RIFT Documents location contains `RiftAddon.toc`, `main.lua`, and `target_vitals_v2.lua`.
2. In the **correct RIFT client**, perform `/reloadui` and confirm the addon is enabled. A console preflight warning is advisory: file timestamps alone cannot establish a reload.
3. Ensure no previous AutoFight GuestReader/coordinator is running and the local receiver port is available.
4. Inspect the local, ignored `artifacts\local\guest-reader-physical-m8-20261116\autofight-guest-reader.json` without printing its delivery token. Configure:
   - `targetProcessId` to the **freshly verified** game PID
   - `initialScanDelaySeconds` to 30
   - `maxScanSeconds` to 15
   - `durationSeconds` to 75 or greater, within validated bounds
   - `maxScanBytes` sufficient for the observed process map (historically ~4.25 GiB)
   - loopback delivery `http://127.0.0.1:49198/observation`, with a locally stored secret token
5. Publish the Release GuestReader executable to the configured local folder before launching the coordinator. Preserve the ignored configuration file.

## One explicit reader session
In **PowerShell 7**, working directory `C:\work\AutoFight`:

```powershell
$reader = (Resolve-Path '.\artifacts\local\guest-reader-physical-m8-20261116\Rift.Automation.GuestReader.exe').Path
$pidForRift = <freshly-verified-rift-PID>
dotnet run --project .\src\Rift.Automation.PhysicalHost\Rift.Automation.PhysicalHost.csproj -c Release -- --coordinate $reader --pid $pidForRift
```

- The coordinator must print the same PID and a new `/afbind <32-lowercase-hex-nonce>` instruction.
- During the initial scan delay, enter that **exact fresh command** in the verified game client. Do not use a nonce from a prior run or this document.
- Confirm in-game feedback: `AutoFight reader bound; waiting for advancing observations.`
- Keep the client running while the reader performs its full scan and subsequent observations. No combat input is needed to establish the transport, although target vitals require a valid target.
- Watch the coordinator's timestamped `scan=`, `selection=`, `freshness=`, and `delivery=` lines. Inspect `%LOCALAPPDATA%\AutoFight\GuestReader\latest.json` for the detailed diagnostic. `delivered-http-200` proves transport acceptance, **not** valid target health.
- If a nonce rotates, the ordinary `/afbind` command cannot replace an existing binding. Reload the game UI and bind the current nonce, or follow the separately validated explicit guidance rebind flow.

## Evidence gates
1. **Deployment:** correct files on disk and confirmed reload in correct client.
2. **Binding:** current nonce accepted by addon, not merely displayed by coordinator.
3. **Scan:** `scan.status=complete` for a stable, pinned game identity.
4. **Telemetry:** current AFT2 envelope decodes with matching nonce and valid target identity/health.
5. **Freshness:** source sequence advances, bounded age, no identity conflict.
6. **Delivery:** authenticated local receiver accepts the same session and fresh observation.

Do not claim M8 AFT2 success unless all six gates have evidence. If the scan completes with `matching-challenge-not-found`, first recheck addon load and binding rather than increasing the memory budget. The previously measured 256 KiB chunk scan completed ~4.24 GiB in 8.88 seconds; this is historical, not a performance guarantee.

## Stop and recovery
- Allow the bounded coordinator to exit, or cancel it; verify GuestReader is no longer running.
- Do not kill either RIFT client to clean up the reader.
- If a process or game identity changes, discard the nonce, re-verify PID/HWND and restart the workflow.
- Keep delivery tokens out of console logs, commits, and handoff documents.
