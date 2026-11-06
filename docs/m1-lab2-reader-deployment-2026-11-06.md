---
schema: autofight.m1.lab2.reader-deployment-handoff.v1
date_et: 2026-11-06
repo: C:\work\AutoFight
lab: lab2
source_branch: main
classification: evidence-ledger
addon_files: verified-6-of-6
guest_reader_presence_before_deployment: absent
private_config_prepared: verified
game_pin_prepared: verified-at-capture
stage: VERIFIED_STAGED_ONLY
install: VERIFIED_INSTALLED_NOT_RUNNING
independent_postinstall_audit: VERIFIED_PASS
guest_reader_running: NOT_RUNNING
in_game_addon_loaded: NOT_VERIFIED
owned_xyz: NOT_VERIFIED
---

# AutoFight M1 — Lab 2 reader-only deployment readiness

## Outcome at this checkpoint

**Lab 2's six existing AutoFight Lua addon files are byte-identical to this repository's authoritative addon sources. Do not overwrite or reinstall them.** The dedicated AutoFight GuestReader executable and configuration were found absent from the expected Lab 2 install location before deployment. A Lab2-only private configuration and a fresh process identity pin were prepared on the host, and a strictly separated stage/install workflow with tests was created. The coordinator verified the inactive reader-only stage and separate reader-only installation. The installer reported installed-not-running; it did not start GuestReader, reload RIFT or alter the existing addon. An independent live post-install guest audit has now passed: the installed reader directory contains exactly two host-hash-matching files, all six addons remain source-identical, and no Reader/RR2 reader process was observed. This still does not prove in-game addon load or current XYZ.

Repository ownership: C:\work\AutoFight, source directory addon\AutoFight. VMware VMX: C:\rift modding\VMs\RIFT-Lab-2\RIFT-Lab.vmx. Lab 2 guest addon directory: C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\AutoFight. Guest reader installation destination (installer reports installed-not-running): C:\Users\mrkoo\AppData\Local\AutoFight\GuestReader.

## Existing addon contents — VERIFIED, read-only

The guest folder returned exactly six names. Guest files were copied to an ignored temporary host folder and compared for both size and SHA-256 to repository source, with no guest addon writes.

| File | Size bytes | SHA-256 | Outcome |
| --- | ---: | --- | --- |
| RiftAddon.toc | 305 | 1d0d6323078894e3c7c50f7d6859bcf1011bc169a3330a7dd3d035acd8e982f3 | MATCH |
| wire.lua | 6405 | 45d10ab987e29e25c45c1260828c442c381afbea928ce8bc453c01efd163c9a0 | MATCH |
| producer.lua | 7992 | ba565468bda7fc054186d1bb1f15b504620565eeb448818451ffa346887dc779 | MATCH |
| live.lua | 2060 | 548653ba443d4a56174b06db4a74c8c03f12fd6ab32d39a1bc564706c5441891 | MATCH |
| guidance.lua | 8584 | 184a9e86d9f493a147b521ef5d04ac02aff8b8a2dbf948a91d11dd74132102ce | MATCH |
| main.lua | 8674 | 8315af2fdaaa5ba69ddcad39d420ebdd9693bf0471e20feaeb87c9a1cee20f4a | MATCH |

Source-of-truth report: docs/m1-lab2-addon-content-parity-2026-11-06.md. Guest inspection jobs (exit 0): af-lab2-addon-readonly-guest-session-20261106-01, af-lab2-addon-readonly-list-20261106-01, af-lab2-addon-byte-sha-parity-20261106-01.

The six OneDrive files carry Cloud Files reparse tag **0x9000601A**, not ordinary symlinks or junctions; this exact tag was verified during Lab 2 inspection. A reader-only installer must accept this explicitly scoped Cloud Files family while still rejecting other reparse types, unexpected files and drifting hashes. Evidence: af-lab2-addon-readonly-reparse-attrs-20261106-01 and af-lab2-cloud-tag-ps-parser-live-20261106-01 (both exit 0). Reading/hashing may hydrate a cloud placeholder; matching bytes do not prove the UI loaded an addon or that OneDrive will keep syncing.

## Reader absence and private binding prerequisites

- **Reader absent before deployment:** No installed C:\Users\mrkoo\AppData\Local\AutoFight\GuestReader package or running AutoFight-owned reader was established in the inspected Lab 2 state. Read-only evidence: af-lab2-guestreader-readonly-inventory-20261106-01 and af-lab2-guestreader-absence-classification-20261106-01 (exit 0).
- **Private host config prepared:** ignored C:\work\AutoFight\artifacts\local\lab2-guest-reader.local.json, generated with a 64-hex random delivery token. Endpoint restricted to http://192.168.56.1:48761/observation. The token must NEVER be recorded in docs, logs or Git. Creation job af-lab2-private-config-create-20261106-01; readback job af-lab2-private-config-readback-20261106-01 (exit 0).
- **Fresh game identity at capture:** ignored C:\work\AutoFight\artifacts\local\lab2-game-identity.local.json prepared under af-lab2-fresh-game-pin-20261106-01 (exit 0). Its PID/start/hash are time-bound, so refresh and re-pin if the RIFT process restarts or the data changes; historical IDs do not authorize commands.
- **Host firewall previously verified:** exact VMnet8 rule permits inbound TCP 48761 only from Lab1 .128 and Lab2 .129 to host 192.168.56.1. Passing a TCP probe does not itself prove the owned host receiver, guest reader or Lua addon is active.

## Created Lab2-only implementation and test boundaries

| File | Purpose |
| --- | --- |
| scripts/prepare_lab2_guest_config.py | Absent-only creation of ignored private config, no guest access or token disclosure |
| scripts/lab2_guest_reader_deploy.py | Identity-pinned cached-credential VMware preflight, exact addon parity gate, distinct stage/install modes, readback/hashes |
| scripts/install_lab2_guest_reader.ps1 | Guest-side reader-only two-file promotion; preserve existing addon; validate stage/game/reader and reparse tags |
| tests/scripts/test_prepare_lab2_guest_config.py | Private config creation and failure modes |
| tests/scripts/test_lab2_guest_reader_deploy.py | Guest deployment contract/fail-closed cases |
| tests/scripts/test_install_lab2_guest_reader.ps1 | Static PowerShell installer contract |

Reported independent checks: Lab2 PowerShell installer 26 passing; Lab2 Python deployment 22 passing; private config generator 9 passing. Integrated Python regression 93/93 passed under af-lab2-integrated-python-regression-20261106-01. These offline tests are separate from live acceptance. The independently hash-verified Lab2 reader-only stage and installer receipt both succeeded; the separate live post-install source hash audit passed under af-lab2-installed-reader-independent-parity-20261106-01. Lab 1's installer is intentionally hardcoded to Lab1 and should never be run unchanged against Lab2.

Important invariants in the new Lab2 workflow:

1. The six addon files are never promoted, modified, merged or reinstalled by reader-only deployment.
2. Stage uses a fresh unique, inactive guest AppData staging directory, verifies the two reader files by copy-back SHA-256, and stores an ignored host receipt.
3. Install is an explicit separate operation, guarded by exact stage ID, manifest hash, private config, fresh RIFT PID/start/exe/hash and no competing reader process.
4. The installer uses non-overwriting CreateNew writes to a pending directory, verifies final hashes and preserves a result receipt; if outcome is uncertain, stop and inspect rather than blindly retry.
5. Neither stage nor install starts a GuestReader, sends RIFT UI input, reloads the addon, restarts the game or changes VM lifecycle.

## Deployment state and next action

| Gate | Current evidence state |
| --- | --- |
| Existing Lab 2 addon 6/6 byte parity | **VERIFIED** |
| Cloud reparse classification | **VERIFIED** |
| Reader absence before deployment | **VERIFIED** |
| Private config and game pin preparation | **VERIFIED at capture** |
| Code and offline tests | **PASS (26/22/9; integrated Python 93/93)** |
| Actual Lab 2 staging | **VERIFIED STAGED ONLY — job af-lab2-reader-live-stage-20261106-01 exit 0; stage ID 3fe0dafb233f4763901c18bc951f2501** |
| Stage receipt | **Private ignored lab2-stage-3fe0dafb233f4763901c18bc951f2501.local.json; SHA-256 4b953982d49f6613d4533e8e51fea816dbe2904109710bea83ca63c9962a28f8** |
| Actual Lab 2 reader install | **VERIFIED INSTALLED-NOT-RUNNING — job af-lab2-reader-live-install-20261106-01 exit 0 at 2026-11-06 08:49:35Z; stage ID 3fe0dafb233f4763901c18bc951f2501** |
| Installer side effects | **addonModified=false; gameReloaded=false; guestReaderStarted=false** |
| Independent post-install source hash audit | **VERIFIED PASS — af-lab2-installed-reader-independent-parity-20261106-01, exit 0 at 2026-11-06 08:53:46Z; exactly two reader files and 6/6 addon hashes match** |
| Reader started/current nonce | **NOT DONE** |
| In-game Lua addon load and bind | **UNVERIFIED** |
| Status-only VNC review capture | **CAPTURED, NOT APPROVED** — job af-lab2-status-readonly-capture-20261106-01, capture ID bcaebc6605e64dcba529cbadcfb75095, screen SHA b352948b58336832aeec16d2eba9e19a62453373693e2826438b8ab048c65813; initial review.json has decision=denied and no focused/empty-chat assertions. No `/afguide status` was sent; review TTL 60 seconds, so a new capture is required for future execution. |
| Owned XYZ / 480-second qualification / movement | **NOT DONE** |

Coordinator: verified stage contained two reader files and left all six addon files untouched. The separate install job returned state=installed-not-running with exit 0 and no reader launch, game UI reload or addon modification; retain the exact receipt. The independent final binary/config source-hash audit now confirms exactly two installed files: Rift.Automation.GuestReader.exe (73,668,413 bytes, SHA-256 2b75222f09d0735e0f09cdf18e9f8e271f1a3e4ad7620c93bde950162d9f7369) and autofight-guest-reader.json (329 bytes, SHA-256 538212e37b45facd29dcfa3188e19e3445af25fd28c706980f943ea2f2c4e545), both matching host source. All six addon files remain exact source hash matches. No bearer token was printed; no AutoFight or RR2 reader process was detected. The independently observed RIFT PID 9752 is a historical pin that must be refreshed before live action. Keep private token/config contents ignored. Never equate successful installation with in-game addon load or advancing XYZ.

Read alongside docs/HANDOFF-AUTOFIGHT-VMWARE-LABS-2026-11-06.md, docs/m1-lab2-addon-content-parity-2026-11-06.md, SESSION_HANDOFF.md and AGENTS.md. This document now records verified stage and install outcomes; update it after later live activation checks, preserving the distinction between installed, running and in-game loaded.
