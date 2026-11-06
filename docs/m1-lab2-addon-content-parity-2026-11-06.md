# AutoFight M1 — Lab 2 addon content parity (2026-11-06)

## Outcome

**VERIFIED: Lab 2 currently has exactly the six expected AutoFight addon filenames, and every copied guest file matches the authoritative AutoFight source file byte-for-byte (size + SHA-256).** No Lab 2 addon repair or reinstall was necessary or performed. This is on-disk content parity, NOT proof that the addon has loaded into the running RIFT UI.

- Repository: C:\work\AutoFight, branch main, source addon folder: addon\AutoFight.
- VMware guest: RIFT-LAB-2, VMX: C:\rift modding\VMs\RIFT-Lab-2\RIFT-Lab.vmx, console loopback port 5962.
- Guest addon directory: C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\AutoFight.
- Evidence: Workbridge command jobs af-lab2-addon-readonly-guest-session-20261106-01 (exit 0), af-lab2-addon-readonly-list-20261106-01 (exit 0), and af-lab2-addon-byte-sha-parity-20261106-01 (exit 0).
- Historical VM process PID observed/pinned during the checks: 17848. Treat it as stale at the next session.
- Reference package independently checked against artifacts\local\autofight-guest-live-20261105\addon\AutoFight; all six reference/staged hashes match.

## File-by-file guest evidence

| File | Guest bytes | Lab 2 SHA-256 | Result against source |
| --- | ---: | --- | --- |
| RiftAddon.toc | 305 | 1d0d6323078894e3c7c50f7d6859bcf1011bc169a3330a7dd3d035acd8e982f3 | MATCH |
| wire.lua | 6405 | 45d10ab987e29e25c45c1260828c442c381afbea928ce8bc453c01efd163c9a0 | MATCH |
| producer.lua | 7992 | ba565468bda7fc054186d1bb1f15b504620565eeb448818451ffa346887dc779 | MATCH |
| live.lua | 2060 | 548653ba443d4a56174b06db4a74c8c03f12fd6ab32d39a1bc564706c5441891 | MATCH |
| guidance.lua | 8584 | 184a9e86d9f493a147b521ef5d04ac02aff8b8a2dbf948a91d11dd74132102ce | MATCH |
| main.lua | 8674 | 8315af2fdaaa5ba69ddcad39d420ebdd9693bf0471e20feaeb87c9a1cee20f4a | MATCH |

Guest directory listing output declared six entries, exactly these names, with no unexpected entries. Lab 2's manifest is AutoFight v0.1.0 and RunOnStartup order is wire.lua, producer.lua, live.lua, guidance.lua, main.lua, based on the byte-identical source TOC.

## Verification method and effects

1. Used the existing AutoFight-owned VMware GuestOps/Credential Manager helper as a reference, parameterized **in process** for the exact Lab 2 VMX and console port. Cached credentials were not printed or written.
2. Revalidated exact live VMware process/log/listener identity before and after guest calls; authenticated GuestOps session opened and read-only process list returned.
3. Called vmrun listDirectoryInGuest for only the exact Lab 2 addon directory. It reported six names.
4. Copied each of those six files **from Lab 2 to an automatically removed temporary host directory** under ignored artifacts/local. Compared SHA-256 and size against fresh source files. Source file hashes were unchanged across the check.
5. No guest files, VM permissions, Codex settings, Windows firewall, RIFT UI, game process or addon configuration were altered by this audit. No /reloadui, reader launch, gameplay input, VM/game restart or reboot.

**Caveat:** Reading OneDrive-backed files may hydrate cloud placeholders. The successful copies establish accessible real bytes at test time, but do not prove ongoing OneDrive synchronization or that RIFT loaded the addon.

## Remaining gates

- Lab 2 AutoFight GuestReader executable/config installation, running process and current reader nonce: **not independently verified by this file-only audit**.
- Lab 2 addon load inside the currently running RIFT Lua environment: **not verified**. Installed matching files do not imply in-game activation; an appropriate reviewed UI reload/addon startup check may be needed.
- Lab 2 owned host receiver/bind and live XYZ: **not done**; 480-second qualification and measured movement are **not proved**.
- Compare current game/VM identity anew before any live action. A historical PID or file hash is not an input authorization.

**Practical next step:** Do not redeploy these identical Lua files. Continue with Lab 1 owned live XYZ first (per current handoff), while separately inspecting Lab 2 GuestReader presence/readiness and planning a specific, reviewed addon-load proof. Refer to HANDOFF-AUTOFIGHT-VMWARE-LABS-2026-11-06.md for overall milestone sequence.
