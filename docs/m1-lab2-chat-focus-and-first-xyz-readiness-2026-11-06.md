---
schema: autofight.m1.lab2.chat-focus-and-first-xyz-readiness.v1
date_et: 2026-11-06
repository: C:\work\AutoFight
scope: lab2-only
runtimes: AutoFight-owned
classification: source-reviewed-plus-live-readonly-evidence
lua_editor_focus: VERIFIED_SOURCE_PATTERN
rift_native_chat_focus: UNVERIFIED
lab2_guest_reader_files: VERIFIED_INSTALLED_NOT_RUNNING
lab2_owned_xyz: NOT_VERIFIED
lab2_game_input_performed: false
---

# Lab 2 first XYZ readiness — LuaPad focus reference and reader workflow

## Decision

LuaPadModern has a useful, proven pattern **only for an addon-owned text input**. Its `RiftTextfield:GetKeyFocus()` reports its own editor state, and `RiftTextfield:SetKeyFocus(true)` requests editor focus before rechecking. Native `inputField.Event:KeyDown(button)` processes Enter and Tab; generic `Event.UI.Input.Key.Down` previously failed for these controls. A click handler requests focus, and focus gain/loss events update the editor's visible state. Startup does not steal focus.

Source reviewed read-only: `C:\work\LuaPadModern\main.lua`, SHA-256 `3a94c7909e91913c79a1c89d41a5d6ec3098275a9f845d3afb9fde0323fb3e97`; focus functions around lines 206–241, native key handler around 293–324, click/focus events around 325–341. Prior Preview17 in-game Enter/Tab evidence: `DEPLOYMENT-LUAPAD-NATIVE-KEYS-PREVIEW17-2026-11-04.md`.

**Do not infer native RIFT chat focus from LuaPad's editor focus.** The Lua addon API does not demonstrate reading or setting the game's built-in chat input caret. An AutoFight-owned RiftTextfield/button could be added later to provide its own status console, but that is a separate addon change and would require controlled deployment and live verification. LuaPadModern and RiftReader2 remain reference-only for AutoFight; there is no new runtime dependency.

## Lab 2 live state at this checkpoint

- Six AutoFight addon files in Lab 2 were independently verified byte-identical to `C:\work\AutoFight\addon\AutoFight` (6/6). OneDrive Cloud Files reparse tag `0x9000601A` is expected; hash parity is not in-game load proof.
- AutoFight-owned GuestReader was staged and installed separately with exact two-file hashes; independent guest postinstall verification passed. State is **installed-not-running**. No Reader or RR2 reader process was observed.
- A bounded read-only VNC screenshot showed the RIFT world with a right-side bag/inventory panel open and no visible native chat caret. The proposed status command review was DENIED and expired; neither chat opener nor `/afguide status` was executed.
- GuestOps PowerShell ran in **Windows Session 0** while RIFT ran in **Session 1**. A bounded `-interactive` probe failed, so `GetForegroundWindow` from standard GuestOps does **not** provide RIFT foreground proof. Screenshot visibility alone also does not prove keyboard focus.
- No RIFT input, `/reloadui`, Reader startup, game exit, guest restart, or VM lifecycle changes were performed as part of this readiness work. Native chat focus, addon load, addon binding and live XYZ are **NOT VERIFIED**.

Read-only evidence: `af-lab2-status-readonly-capture-20261106-01` (screenshot capture ID `bcaebc6605e64dcba529cbadcfb75095`), `af-lab2-guestops-session-probe-20261106-01` (pass), `af-lab2-guestops-interactive-session-20261106-01` (failed); earlier Reader install `af-lab2-reader-live-install-20261106-01` and independent parity audit `af-lab2-installed-reader-independent-parity-20261106-01` both passed.

## Implemented independent Lab 2 tools (not yet launched live)

| Script | Exact role | Safety gate |
| --- | --- | --- |
| `scripts/lab2_chat_status.py` | Fresh VNC screenshot plus separately reviewed one-Enter chat opener or exact `/afguide status` | Exact lab/game pin, 60s review, one-shot, screenshot check afterward; no blind input |
| `scripts/lab2_guest_reader_start.py` + `scripts/start_lab2_guest_reader.ps1` | Start the already-installed AutoFight GuestReader exactly once with `--live` | Explicit `--start --confirm START-LAB2`, stage ID, fresh game pin, exact installed hashes, no competing Reader |
| `scripts/fetch_lab2_guest_reader_snapshot.py` | Fetch one fresh existing `latest.json` from that Reader | Reader PID/start and binary SHA, game pin, stage receipt, 30s freshness, identity recheck, private absent-only output |
| `scripts/bootstrap_lab2_host_telemetry.py` | Optionally generate one private host receiver config | Exact Lab2 game and reader identity, nonce/token match, endpoint 192.168.56.1:48761, no-overwrite, reparse-safe private path |

These tools operate independently of any running RiftReader2 service. They do not implement automatic movement, spellcasting, game reload or guest VM restart.

## Tests and reviews

- Root combined Python regression: `af-lab2-root-all-scripts-regression-20261106-02`, **142 tests, 0 failures, 1 expected Windows symlink-privilege skip**.
- Lab2 Reader launch PowerShell static checks: `af-lab2-root-start-ps-static-20261106-01`, **32/32 PASS**.
- Existing Lab2 install PowerShell regression: `af-lab2-root-install-ps-static-20261106-02`, **26/26 PASS**.
- Independent review of final Lab2 start source/test hashes: PASS, host-only preflight PASS; **no launch**.
- Independent review of final Lab2 snapshot-fetch source/test hashes: PASS, focused 16/16, Lab1 comparator 8/8; **no live fetch**.
- Independent review of hardened Lab2 host bootstrap: PASS, focused 18 pass / 1 skip; actual private directory chain read-only probe PASS; **no live bootstrap**.

## Next controlled milestone sequence

1. Refresh **current** Lab2 VM and RIFT PID/start/executable SHA. Historical PID9752 is not an evergreen authorization.
2. With the inventory panel closed and RIFT visible, capture a fresh Lab2 VNC screenshot. Only if a new review establishes appropriate conditions, execute the **single Enter-only** chat opener. Review an after-screenshot for a visibly focused empty native chat entry. Stop if uncertain.
3. Capture a **separate** fresh screenshot and execute only `/afguide status` if native chat focus has just been established; check the in-game result for addon load.
4. Start the already-installed AutoFight Reader in one explicitly confirmed, identity-pinned action. Treat an uncertain launch as **do not retry** until inspected. Observe the separate Reader PID/start.
5. Fetch and validate its fresh `latest.json`, create the host private receiver configuration, start the AutoFight-owned .NET 10 listener, and perform a separately reviewed game-chat bind using the fresh Reader nonce. Keep secrets in ignored `artifacts\local` only.
6. First acceptance is fresh advancing XYZ from Lab2 **without any RR2 runtime service**; eight-minute qualification and movement come afterward. Do not call synthetic tests live-game proof.

If native chat focus remains hard to prove, evaluate a separate AutoFight-owned focus-aware status UI using the LuaPad pattern; preserve current addon sources until such a change is reviewed and deployed.
