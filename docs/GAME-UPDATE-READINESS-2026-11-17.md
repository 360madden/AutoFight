# AutoFight — RIFT Game Update Readiness (2026-11-17)

**Scope:** AMD-DESKTOP, `C:\work\AutoFight`, independent `main`. Preparation for a user-initiated RIFT client update. This work does **not** launch an updater, restart or move RIFT, scan process memory, install/reload addons or send game input.

## Verified before-update file baseline — November 17, 2026, about 06:40 EST

- Observed process metadata only: physical `rift_x64.exe` PID **58932** (transient; not a reusable process pin).
- Installed executable: `C:\Program Files (x86)\Glyph\Games\RIFT\Live\rift_x64.exe`.
- File length: **60,493,640 bytes**.
- File last-write UTC: **2026-10-27T10:56:49.6396025Z**.
- Independently hashed on disk before update, SHA-256: **`1935A05C9DB6D468E34E0055014A4A99C1B5AFCEB1695884E24600F5C20A4BE8`**.
- It **matched** `PhysicalRiftPositionReader.GameSha256` in source and the new preflight CLI's `MatchesPinnedBuild` response.
- Exact native XYZ RVA recipe for that **one** pinned executable only: `ManagerRva=0x33624A8`, `PlayerSystemRva=0x33623E0`, `PositionIndexRva=0x2F588A0`, `PositionComponentVptrRva=0x26B4FB0`.
- This is a **file identity** baseline, not proof of active RIFT offsets, passive producer freshness or combat control. The physical XYZ native reader has pre-existing hash-mismatch and on-disk mid-read-change rejection.
- No copy of proprietary game binaries or private gameplay telemetry was made.

## New disk-only compatibility command

From **PowerShell 7 on AMD-DESKTOP**, in `C:\work\AutoFight`:

```powershell
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --physical-build-check "C:\Program Files (x86)\Glyph\Games\RIFT\Live\rift_x64.exe"
```

The `PhysicalRiftBuildPreflight` helper reads only this explicitly named, fully-qualified `rift_x64.exe` disk file. It does not enumerate or open a process or contact a controller. It requires nonempty, non-reparse, at-most-1-GiB file, checks length/creation/last-write metadata around SHA-256 hashing, and reports immutable, redacted JSON. The CLI has a 30-second cooperative timeout. It does not emit paths or arbitrary error content.

| Result | Exit | Interpretation |
| --- | ---: | --- |
| `MatchesPinnedBuild` | 0 | On-disk bytes match the existing verified fingerprint **only**; no new authority |
| `RequiresRequalification` | 2 | Different executable; **do not reuse old native offsets** |
| `Unavailable`, `InvalidPath`, `ChangedDuringRead` | 2 | No usable compatibility conclusion; retry only when disk is stable |

Regardless of status, output includes `nativeOffsetsRequalified=false` and `canAutomate=false`. A file might change after a successful check; the actual native reader rechecks the exact file/build when invoked. Cooperative cancellation cannot interrupt an OS filesystem call already blocked inside the kernel.

## After Joey lets RIFT update — requalification checklist

1. **Stop stale observations.** Treat all prior PID/start, process leases, AFT2 target-health candidates, frame/producer clocks and historical XYZ traces as **old-build evidence**, not current input or combat qualification.
2. **Run disk-only preflight again.** Record new executable SHA/size/UTC. If it differs, retain `RequiresRequalification`; do not edit the pinned SHA alone.
3. **Offline binary analysis.** Independently reidentify the player system, actor/manager, position index and component vtable in the new EXE (Ghidra/REA or equivalent). Verify all four RVAs and structure invariants, not only their numerical proximity to the previous build.
4. **Addon and wire compatibility.** Recheck the AutoFight Lua producer and GuestReader AFT2 framing, version/schema and host binding offline. Confirm expected addon files after update. File timestamps alone do not prove the game loaded an addon; in-game addon status is a separate later gate.
5. **Run offline acceptance.** `dotnet build Rift.Automation.slnx -c Release`; Core, Windows, Guest specs; WPF `--smoke-test`; compare newly saved traces against strict historical import rules without promoting old samples.
6. **Separate read-only current-build qualification.** Only after the new exact-build native recipe is independently reviewed may a separately planned on-demand physical RIFT read-only check occur. Validate exact process PID/start/path/hash and player identity; requalify producer continuity and sustained freshness.
7. **Combat remains gated.** AFT2 `SourceQualified` remains hardcoded `false`; M8 stationary combat is synthetic preview-only (`CanExecute=false`). Do not infer attack success or command attribution from offline tests or the new file fingerprint.

**What was deliberately not changed:** the existing `PhysicalRiftPositionReader.GameSha256`, native offsets, live RIFT process, game client files, running addon, telemetry authority flags or combat execution path.

## Test and review receipt

- Offline preflight regressions exercise synthetic exact match, changed build, missing/empty file, wrong/relative/malformed path (including embedded NUL), early cancellation and invalid expected hash.
- New CLI was exercised against the real **on-disk** current EXE with `MatchesPinnedBuild`, `nativeOffsetsRequalified=false`, `canAutomate=false` and exit 0.
- Independent architecture/readiness audit: `/root/update_readiness_audit` read-only PASS.
- Independent source review: `/root/update_preflight_code_review` read-only PASS after P2 malformed-path fix and timeout correction, no remaining P0/P1/P2. Review limitations: no live update/post-update test and OS I/O cannot always be preempted by a cancellation token.
- **Final offline acceptance:** .NET 10 Release solution build **0 warnings/errors**; **191 Core + 242 Windows + 57 Guest = 490/490** deterministic specs; WPF smoke **7/7**; Git diff check exit 0 (Git only reported LF/CRLF normalization warnings). Final exact CLI exercised against installed RIFT file: exit 0 and `MatchesPinnedBuild`. Invalid-path CLI: intentional exit 2 and typed `InvalidPath`; no crash. Jobs: `af-update-final-build-20261117-01`, `af-update-final-{core,windows,guest,smoke,preflight_current,preflight_invalid}-20261117-02`. Git commit/push recorded in repository history.
