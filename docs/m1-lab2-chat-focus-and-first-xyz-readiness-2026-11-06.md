---
schema: autofight.m1.lab2.chat-focus-and-first-xyz-readiness.v1
date_et: 2026-11-06
repository: C:\work\AutoFight
scope: lab2-only
runtimes: AutoFight-owned
classification: source-reviewed-plus-live-readonly-evidence
lua_editor_focus: VERIFIED_SOURCE_PATTERN
rift_native_chat_focus: UNVERIFIED
lab2_guest_reader_files: VERIFIED_INSTALLED
lab2_reader_started: VERIFIED_AT_2026_11_06_0934_UTC
lab2_host_receiver_transport: VERIFIED_ADVANCING_UNAVAILABLE
lab2_addon_bind: NOT_VERIFIED
lab2_private_bind_helper: IMPLEMENTED_OFFLINE_REVIEWED_NOT_EXECUTED
lab2_owned_vnc_toolchain: LIVE_SCREEN_CAPTURE_VERIFIED
lab2_luapad_files: PREVIEW20_ON_DISK_NOT_LIVE_PROVEN
lab2_owned_xyz: NOT_VERIFIED
lab2_game_input_performed: false
---

# Lab 2 first XYZ readiness — LuaPad focus reference and reader workflow

> **Latest November 6 checkpoint:** AutoFight's independent Windows VNC Python 3.14.6 environment is installed and verified by an actual read-only Lab 2 screenshot (`artifacts/local/venv-vnc`; pinned dependencies in `scripts/requirements-vnc.txt`). A separate `scripts/lab2_chat_private_bind.py` now provides a reviewed, private, one-shot `/afguide bind lab2` path, validated by 14 focused tests and 157 combined Python tests (one expected skip), independently reviewed. It never stores the nonce in arguments, stdout, plan/review/execution or an after-command screenshot. **No native RIFT input or bind attempt occurred.** Lab 2's LuaPadModern addon is confirmed Preview 20 on disk, while physical RIFT's Preview 21 editor-focus result is separate. In-game loading and LuaPad Run in Lab 2 remain unverified. Existing historical checkpoint text below is preserved as evidence of what was known then.

## Decision

LuaPadModern has a useful, proven pattern **only for an addon-owned text input**. Its `RiftTextfield:GetKeyFocus()` reports its own editor state, and `RiftTextfield:SetKeyFocus(true)` requests editor focus before rechecking. Native `inputField.Event:KeyDown(button)` processes Enter and Tab; generic `Event.UI.Input.Key.Down` previously failed for these controls. A click handler requests focus, and focus gain/loss events update the editor's visible state. Startup does not steal focus.

Source reviewed read-only: `C:\work\LuaPadModern\main.lua`, SHA-256 `3a94c7909e91913c79a1c89d41a5d6ec3098275a9f845d3afb9fde0323fb3e97`; focus functions around lines 206–241, native key handler around 293–324, click/focus events around 325–341. Prior Preview17 in-game Enter/Tab evidence: `DEPLOYMENT-LUAPAD-NATIVE-KEYS-PREVIEW17-2026-11-04.md`.

**Do not infer native RIFT chat focus from LuaPad's editor focus.** The Lua addon API does not demonstrate reading or setting the game's built-in chat input caret. Joey's development interface choice is: retain RIFT native chat for simple scaffolding/slash commands; use LuaPadModern as the preferred future interactive Lua code scratchpad once its Run/execution testing is accepted; add only a small AutoFight-owned diagnostic status button if it removes a real first-XYZ blocker. Do not build a competing full text editor, attempt to rewrite native chat, or add a LuaPad runtime dependency. Any future AutoFight addon UI source change requires staged hash-parity refresh, controlled deployment and live verification. LuaPadModern and RiftReader2 remain read-only development references.

## Lab 2 live state at this checkpoint

- Six AutoFight addon files in Lab 2 were independently verified byte-identical to `C:\work\AutoFight\addon\AutoFight` (6/6). OneDrive Cloud Files reparse tag `0x9000601A` is expected; hash parity is not in-game load proof.
- AutoFight-owned GuestReader was separately staged/installed with two exact verified hashes and then **successfully started once** under `af-lab2-reader-single-start-20261106-01` (exit 0; 09:34 UTC): Windows guest Reader PID **6252**, start UTC ticks `639295544587249998`, distinct instance `4fe553ed-fe5f-4411-984f-7b9c8a47cdab`, state `reader-launched-not-bound`. The launch sent no RIFT keyboard input, did not alter the six addon files and performed no reload or VM lifecycle change. The Reader is not assumed evergreen; revalidate current PID/start before reuse. Do not blindly launch another Reader.
- Initial and fresh bounded read-only VNC screenshots showed RIFT visible with the right-side bag/inventory panel open and no native chat caret. The proposed chat status review remains DENIED; neither Enter-only chat opener nor `/afguide status` was executed. Fresh capture `af-lab2-native-chat-correct-env-capture-20261106-01`, capture ID `356e12763dd842ae81b86d738eaf3f10`. Two earlier captures failed because global `C:\Python314\python.exe` lacks `vncdotool`; that historical blocker is now resolved with the independently verified AutoFight-owned `artifacts/local/venv-vnc` (`vncdotool` 1.4.2, Pillow 12.3.0). No RiftReader2 environment is needed for subsequent Lab 2 captures.
- GuestOps PowerShell ran in **Windows Session 0** while RIFT ran in **Session 1**. A bounded `-interactive` probe failed, so `GetForegroundWindow` from standard GuestOps does **not** provide RIFT foreground proof. Screenshot visibility alone also does not prove keyboard focus.
- Reader snapshot verification PASSED under `af-lab2-reader-first-snapshot-fetch-20261106-01` (sequence 9) and again under `af-lab2-reader-second-snapshot-fetch-20261106-01` (sequence 37), exact Reader instance and Lab2 game PID 9752. The first host configuration attempt correctly failed `snapshot-stale-or-clock-skew`; a fresh same-reader snapshot immediately followed by `af-lab2-bootstrap-host-telemetry-20261106-02` PASSED and created ignored `artifacts/local/host-lab2.local.json` without overwriting existing files.
- Actual AutoFight-owned .NET 10 host watch `af-lab2-owned-host-telemetry-first-20261106-01` ran 20 seconds: **40 polls, 40 unavailable, zero fresh XYZ**, advancing delivery outer sequence through 61, reason `source_not_fresh`. It terminated exit 2 (expected negative acceptance); no movement authority granted. A redacted Reader snapshot readout reported `readerStatus=running`, `targetFound=true`, full 3,404,537,856-byte process scan complete, zero matching candidates, selection `no-matching-candidate`, reason **`matching-challenge-not-found`**. `addon/AutoFight/live.lua` lines 59–64 intentionally does not publish the challenge-wrapped frame until the in-game addon is bound to that Reader's 32-hex nonce. This does not, by itself, establish whether the addon was loaded in-game.
- No RIFT keyboard input, `/reloadui`, game exit, guest restart, or VM lifecycle changes occurred. Native chat focus, addon load, addon binding and live XYZ remain **NOT VERIFIED**.

Read-only evidence: `af-lab2-status-readonly-capture-20261106-01` (screenshot capture ID `bcaebc6605e64dcba529cbadcfb75095`), `af-lab2-guestops-session-probe-20261106-01` (pass), `af-lab2-guestops-interactive-session-20261106-01` (failed); earlier Reader install `af-lab2-reader-live-install-20261106-01` and independent parity audit `af-lab2-installed-reader-independent-parity-20261106-01` both passed.

## Implemented independent Lab 2 tools (Reader and receiver now exercised live)

| Script | Exact role | Safety gate |
| --- | --- | --- |
| `scripts/lab2_chat_status.py` | Fresh VNC screenshot plus separately reviewed one-Enter chat opener or exact `/afguide status` | Exact lab/game pin, 60s review, one-shot, screenshot check afterward; no blind input |
| `scripts/lab2_chat_private_bind.py` | One exact `/afguide bind lab2` command using privately fetched fresh Reader nonce | Denied-by-default screenshot review, empty/focused chat, exact Reader/game/VM/stage pin, hash-only plan, durable one-shot receipt, no post-bind screenshot; **not yet executed** |
| `scripts/lab2_guest_reader_start.py` + `scripts/start_lab2_guest_reader.ps1` | Start the already-installed AutoFight GuestReader exactly once with `--live` | Explicit `--start --confirm START-LAB2`, stage ID, fresh game pin, exact installed hashes, no competing Reader |
| `scripts/fetch_lab2_guest_reader_snapshot.py` | Fetch one fresh existing `latest.json` from that Reader | Reader PID/start and binary SHA, game pin, stage receipt, 30s freshness, identity recheck, private absent-only output |
| `scripts/bootstrap_lab2_host_telemetry.py` | Optionally generate one private host receiver config | Exact Lab2 game and reader identity, nonce/token match, endpoint 192.168.56.1:48761, no-overwrite, reparse-safe private path |

These tools operate independently of any running RiftReader2 service. They do not implement automatic movement, spellcasting, game reload or guest VM restart.

## Tests and reviews

- Root combined Python regression: `af-lab2-root-all-scripts-regression-20261106-02`, **142 tests, 0 failures, 1 expected Windows symlink-privilege skip**.
- Lab2 Reader launch PowerShell static checks: `af-lab2-root-start-ps-static-20261106-01`, **32/32 PASS**.
- Existing Lab2 install PowerShell regression: `af-lab2-root-install-ps-static-20261106-02`, **26/26 PASS**.
- Independent review of final Lab2 start source/test hashes: PASS; host-only preflight **PASSED** again under `af-lab2-reader-preflight-20261106-02` (2026-11-06 09:32 UTC). Live launch is a separately recorded operation, not an offline test.
- Independent review of final Lab2 snapshot-fetch source/test hashes: PASS, focused 16/16, Lab1 comparator 8/8; two live verified snapshots subsequently fetched.
- Independent review of hardened Lab2 host bootstrap: PASS, focused 18 pass / 1 skip; actual private directory chain read-only probe PASS; one live host configuration subsequently created from a fresh snapshot.
- **Latest private-bind acceptance:** Focused 14/14 and independent combined Python 157 tests (one expected skip) PASSED; reviewed helper/test SHA-256 and source match commit `57159a6`. No Lab 2 game input. AutoFight-owned VNC Python environment successfully captured Lab 2 read-only under `af-local-vnc-venv-capture-smoke-20261106-01`; `pip check` passed. Exact versions/setup documented in [local VNC toolchain](local-vnc-toolchain.md). The existence of a reviewed private-bind helper is not proof of addon load or XYZ.
- **Privacy correction after the first live bootstrap:** the original CLI inadvertently included the 32-hex Reader binding nonce in its retained Workbridge stdout (not the separate delivery token). Source now reports `readerNonce: "[redacted]"` while retaining both values only inside ignored private host configuration. Added validate-only and `--write` stdout-redaction tests; independent check `af-lab2-bootstrap-privacy-independent-integrated-20261106-01` PASSED, 143 Python tests, 1 expected symlink-privilege skip. The earlier retained job output is historical and was not automatically erased or rotated; do not paste it in docs/chat. No reader/game restart is needed for this stdout-only fix.

## Next controlled milestone sequence

1. **Do not restart the Reader:** it was started once and delivered to the host. Revalidate Lab2 VM/RIFT/Reader process IDs, start times and executable hashes before any future operation; old pins are not evergreen.
2. Close the Lab2 inventory panel and make RIFT visibly interactive; capture a fresh Lab2 VNC screenshot with the tested development interpreter. Keep the review DENIED unless the screen establishes safe conditions. If approved, execute only one Enter-to-open-chat and verify an empty focused native caret in a separate after-capture.
3. Use a separately reviewed native chat command `/afguide status` to prove AutoFight addon load. For initial binding, use only the new `scripts/lab2_chat_private_bind.py` capture/review/execute workflow after an independently fresh and explicitly approved empty focused-chat screenshot; it reads the current Reader nonce privately at execution and sends exactly `/afguide bind lab2` plus the nonce once. Recheck current process/Reader identity immediately before submission. **Do not capture, paste or publish an after-bind chat screenshot**, as chat history could expose the ephemeral nonce; verify the bind separately through fresh redacted Reader telemetry. A command-sent receipt is not XYZ acceptance.
4. Re-run a bounded AutoFight-owned host telemetry watch using already-created ignored `artifacts/local/host-lab2.local.json`. Require advancing valid XYZ rather than merely advancing outer delivery sequences. Do not label `source_not_fresh` as a network failure: the host already received advancing Reader deliveries.
5. After two valid advancing XYZ observations, perform the standalone 480-second strict qualification; movement is separately gated. If native chat focus remains blocked, consider a tiny AutoFight-owned Status/bind UI only as a new, deliberately staged addon change—never silently change the current six hash-pinned files.

If native chat focus remains hard to prove, evaluate a tiny always-available AutoFight **Status** button calling the existing `guidance:Text(...)`/`displayGuidance()` functions; do not duplicate LuaPad's editor. Current guidance panel is lazily visible only after nonce binding. Never edit the six staged addon source files while a Lab2 reader start/fetch attempt is in progress: the immutable stage receipt pins all six SHA-256 hashes and source drift must block. LuaPadModern Preview21's editor focus was live-accepted in physical RIFT (`docs/PREVIEW21-EDITOR-KEYBOARD-FOCUS-2026-11-06.md` in LuaPadModern), but that does not by itself prove Lua execution/Run or native chat caret control. LuaPad source declares GPLv3-or-later; adapt concepts, do not silently copy verbatim source.
