# AutoFight M1: VMware Lab1/Lab2 network and privilege readiness (2026-11-06)

## Scope and verified outcome

This record is a **time-stamped live test**, not a claim that GuestReader, the RIFT addon, or the full 480-second XYZ qualification has passed. No RR2 runtime service is required by the AutoFight-owned network receiver.

| Gate | Lab1 | Lab2 | Evidence |
|---|---|---|---|
| Guest IP pinned | 192.168.56.128 | 192.168.56.129 | Cached VMware guest identity checks |
| Guest PowerShell effective administrator token | PASS | PASS | Workbridge job `af-both-guest-privilege-corrected-20261106-b1` |
| Guest to host TCP 192.168.56.1:48761 | PASS | PASS | Workbridge job `af-both-labs-tcp-48761-20261106-a1` |
| AutoFight GuestReader started | NOT DONE | NOT DONE | Intentionally outside firewall test |
| AutoFight addon loaded in active RIFT | UNVERIFIED | UNVERIFIED | Lab1 reload pending; Lab2 OneDrive addon directory exists but contents/load not verified |
| Fresh sustained AutoFight-owned XYZ | NOT YET | NOT YET | Pending guest reader/addon handshake |

Host TCP listener was temporary, bound only to `192.168.56.1:48761`, accepted one identity-labelled guest connection from each source IP, and closed after the diagnostic. Exit code 0. No game input, guest file changes or restarts occurred in the connectivity test.

## Host Windows Firewall

- Rule name: `AutoFight-VMware-Labs-Telemetry-48761-VMnet8`.
- Display name: `AutoFight VMware Labs Telemetry TCP 48761`.
- Inbound TCP port: `48761`.
- Local IP: `192.168.56.1`; interface: `VMware Network Adapter VMnet8`.
- **Only** remote IPs `192.168.56.128` (Lab1) and `192.168.56.129` (Lab2).
- Rule enabled; action Allow; Profile Any (VMnet8 has no observed NLA profile); EdgeTraversalPolicy Block.
- Script: `scripts/enable_vmware_labs_telemetry_firewall.ps1`.
- Static checks: 26/26 passed, `tests/scripts/test_enable_vmware_labs_telemetry_firewall.ps1`.
- Elevated enable verification: UAC process exit 0; own transcript `artifacts/local/firewall-both-labs-uac-20261106-03.log` recorded `IS_ADMIN=True` and `state=enabled`; read-only Windows rule/filter inspection matched every pinned field.
- Initial two UAC launch attempts returned before their elevated child completed and did not install a rule. Subsequent `Start-Process -Verb RunAs -Wait` with durable transcript completed successfully. The precise lifecycle cause of the first attempts was not independently proved.

Rollback from **already elevated** Windows PowerShell, if requested:

```powershell
& 'C:\work\AutoFight\scripts\enable_vmware_labs_telemetry_firewall.ps1' -Undo
```

Undo validates the exact owned rule and refuses drift or unknown rules. Do not run rollback while a live AutoFight host receiver needs this port.

## Administrator privilege nuance

- Host Workbridge process token remains **not elevated**. The `Workbridge MCP` Windows scheduled task is `Interactive`, `RunLevel Limited`, and is currently Running under administrator-group member `mrkoo`. The task setup script `C:\work\workbridge-mcp\scripts\Install-Autostart.ps1` intentionally specifies `-RunLevel Limited`.
- The firewall was installed via one standard, user-authorized UAC-elevated PowerShell operation. Neither the firewall host listener nor ordinary AutoFight host telemetry needs a permanently elevated Workbridge process.
- Guest PowerShell processes launched through the already cached VMware guest sessions on **both labs** have an **effective full Administrator token**. No guest privilege or credential change was required.
- The user authorized elevation and/or permanent Workbridge admin configuration. The required host firewall operation was elevated successfully; a permanent `Highest` Workbridge startup task has **not** been configured because it is unnecessary for either guest's tested connectivity and activating it would require a controlled restart/reconnect that could disrupt other Workbridge sessions. This is an explicitly recorded remaining choice, not an elevation failure; if pursued, update the task and its source installation script together after XML backup/review.

## Lab1 RIFT UI reload gate

A fresh Lab1 VNC capture at `2026-11-06T07:13:24Z` was **DENIED**: File Explorer and a Windows Security permissions dialog covered the guest desktop. The RRECollector `Reload UI` toolbar/button was not visible; the previously reviewed fixed click must **not** be executed. The 60-second packet expired, its review remained denied, and no game input/reload was sent. Lab1 RIFT process remains running. Window ownership/permission dialog origin is unproven.

Next: diagnose guest visible dialog without blind input; restore visible RIFT by separately verified UI steps; create a **new** screenshot review before the one specifically authorized Lab1 reload. Then validate AutoFight addon and fresh reader nonce/binding, run owned telemetry watch, and eventually the 480-second proof. Lab2 RIFT remains untouched.

## Code checkpoint

Commit `1caa354b33a8c2c924b2677a50e165609a05a7ab` contains both-lab firewall helper, its static test, and Lab1 one-shot RRECollector toolbar click gate/tests. Guarded console test `test_lab1_chat_console.py` passed 21/21. No host Workbridge service or VMware guest was restarted.

## Disposable VMware Codex permission defaults (2026-11-06)

The user clarified that both VMware guests are disposable development environments and should default to broad Codex filesystem access with minimal **Codex** approvals. This is independent of Windows UAC, desktop per-chat permissions, Workbridge MCP approvals, and RIFT's in-game Lua security.

- **Lab1:** Added exactly two top-level TOML keys `sandbox_mode = "danger-full-access"` and `approval_policy = "never"` to guest `C:\Users\mrkoo\.codex\config.toml`. Original 3,203-byte SHA256 `134261decfa6b6106cbd35d42984346a8fbc41206ef52de2a5dc40f32cb9042f`; updated 3,265-byte SHA256 `edc12146d2ce067a3baef01030fbf0f61378ca38cc37b27f530f753314a703ae`. Exact byte comparison showed the two new lines prefixed and all original bytes unchanged; parsed TOML valid. Private original backup in guest: `C:\Users\mrkoo\.codex\config.toml.autofight-rollback.2cdea72fe0c64e0b9e5b352196cb00e7.bak`, original hash readback verified. Successful job `af-lab1-codex-safe-filecopy-edit-20261106-a1`.
- **Lab2:** `codex-cli 0.165.0` and guest `C:\Users\mrkoo\.codex\config.toml` already had top-level `sandbox_mode = "danger-full-access"` and `approval_policy = "never"`, with `[windows] sandbox = "elevated"`. Verified in read-only job `af-lab2-codex-config-version-readonly-20261106-b2`; no Lab2 config edit or backup required.
- **Group:** Lab2 `CodexSandboxUsers` is a local group containing `CodexSandboxOffline` and `CodexSandboxOnline`. It was NOT added to Administrators and no NTFS ACLs were modified. The Codex CLI full-access setting is separate from the Windows sandbox accounts.
- **Activation/limitations:** Saved user defaults were verified, but **effective settings of already-open Codex desktop chats were not inspected**; per-chat permissions, project flags, managed requirements and existing process state can override defaults. Verify in a fresh Codex session using `/status` and `/debug-config` (as supported). No Codex app, VM, Windows, or RIFT process was restarted. Windows UAC and Workbridge confirmation prompts are separate and not disabled by these two settings.
- **Rollback:** Restore Lab1's exact backup over `config.toml` using an authorized guest admin session, verify original hash, then reopen Codex to load the restored defaults. Do not delete the backup until accepted.

Current live AutoFight XYZ/addon-load/movement acceptance remains pending; this config change proves only saved Codex defaults.

