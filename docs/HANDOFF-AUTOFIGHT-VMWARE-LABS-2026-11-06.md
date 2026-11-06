---
document_type: AutoFight_detailed_agent_handoff
schema_version: 1
captured_date_et: 2026-11-06
repository: "C:\work\AutoFight"
branch: main
head_before_handoff: f09843f74f33870a27d684b32d0821287ece762d
app_version: "0.5.7"
priority: "Own live XYZ in Lab1, then prove movement; no RR2 runtime dependency"
evidence_policy: "VERIFIED/HISTORICAL/UNVERIFIED/BLOCKED; never promote historical pins"
---

# AutoFight — Detailed Lab1/Lab2 Handoff (November 6, 2026)

> **Follow-up at 08:49 UTC Nov 6:** Lab 2's existing six Lua addon files are independently byte-verified (6/6), and its previously absent dedicated AutoFight GuestReader was now staged in an inactive hash-verified folder and installed separately. Jobs `af-lab2-reader-live-stage-20261106-01` and `af-lab2-reader-live-install-20261106-01` both passed; install state `installed-not-running`. No Lua addon modification, RIFT reload, game input, or Reader launch. [Current Lab 2 deployment evidence](m1-lab2-reader-deployment-2026-11-06.md). Independent post-install audit PASSED (`af-lab2-installed-reader-independent-parity-20261106-01`): installed Reader EXE (73,668,413 bytes) and private config (329 bytes) both match source SHA-256, all six addon files still match, and Reader process remains absent. Addon actually loaded in RIFT, owned XYZ and movement remain UNVERIFIED. This supersedes older Lab 2 Reader-absent/content-unknown rows below.

> **Follow-up verified at 08:14 UTC Nov 6:** Lab 2's exact six addon TOC/Lua files were listed in its guest directory and separately copied to a temporary host folder; every SHA-256 and byte count matched the authoritative AutoFight addon source. No extras. Independent review PASS. **This supersedes the Lab 2 on-disk content UNKNOWN cells and the old Lab 2 content warning below**; in-game load and Lab 2 GuestReader presence remain UNVERIFIED. Full evidence: [Lab 2 addon parity](m1-lab2-addon-content-parity-2026-11-06.md). No guest deployment or game input occurred.

Read this after AGENTS.md and SESSION_HANDOFF.md. It supersedes their earlier dated claims about live firewall, two-lab permissions and current RIFT UI status. Check Git HEAD, VM/game identities and live process state again at each future session. This is an evidence ledger, not a claim that a PID or tool session will remain current.

## 1. Mission and ownership

- Joey's objective is a standalone C#/.NET 10 Windows WPF AutoFight program: interact with RIFT inside VMware, read current player XYZ, send one controlled movement, independently measure displacement, then build target/casting/navigation.
- The one write-owned application repository is C:\work\AutoFight, branch main. Its version is 0.5.7 from src/Directory.Build.props. At capture its clean HEAD is f09843f74f33870a27d684b32d0821287ece762d; this handoff's commit advances it.
- RiftReader2 is a read-only implementation reference. Copy/adapt proven patterns as needed, but the finished AutoFight runtime cannot require RR2 service processes, another repo's files, Workbridge, AI/Codex or development Python.
- Coordinator owns final integration/commits; use as many useful reasoning agents as appropriate with distinct tasks and independent review. Avoid conflicts over desktop focus/guest input.
- Project references: README.md; docs/current-status.md; docs/M1-standalone-guest-port-2026-10-31.md; docs/standalone-autonomous-directive-2026-10-31.md; docs/automation-platform-architecture.md; docs/automation-platform-roadmap.md.

## 2. Snapshot — verified against not yet done

| Item | Lab1 | Lab2 |
| --- | --- | --- |
| VMware guest IPv4 | 192.168.56.128 | 192.168.56.129 |
| RIFT historical PID | 7164 | 9752 |
| RIFT historical start ticks | 639294897866244204 | 639294897826627677 |
| Game executable SHA-256, historical | 1935a05c9db6d468e34e0055014a4a99c1b5afceb1695884e24600f5c20a4be8 | same image hash reported |
| Guest cached credentials and high-integrity administrator PowerShell | VERIFIED | VERIFIED |
| Guest -> host VMnet8 TCP48761 | VERIFIED PASS | VERIFIED PASS |
| Saved Codex full-access / no-approval user defaults | VERIFIED, changed Nov6 | VERIFIED, already configured |
| AutoFight GuestReader plus six addon bytes installed | VERIFIED in Lab1 | UNVERIFIED |
| OneDrive AddOns\AutoFight directory observed | Lab1 addon installed and hashed | Directory exists; content/load UNVERIFIED |
| AutoFight addon loaded into currently running RIFT UI | UNVERIFIED | UNVERIFIED |
| AutoFight GuestReader currently streaming | NOT PROVED | NOT PROVED |
| Current AutoFight-owned XYZ | NOT YET | NOT YET |
| 480-second strict current telemetry qualification | NOT YET | NOT YET |
| AutoFight-caused and independently measured movement | NOT YET | NOT YET |

The two historical game PIDs and ticks must be repinned before any future live action. Installed files do not imply in-game addon load. A host listener test does not imply telemetry. No physical RIFT action occurred in this AutoFight phase.

## 3. Both labs: firewall/network DONE

- On the Windows host, VMware VMnet8 was up at 192.168.56.1/24 and had no NLA profile. Host firewall was enabled with inbound default Block.
- Installed exact Windows Firewall rule AutoFight-VMware-Labs-Telemetry-48761-VMnet8, inbound TCP 48761, local 192.168.56.1, remote ONLY 192.168.56.128 and 192.168.56.129, interface VMware Network Adapter VMnet8, Action Allow, Profile Any constrained by exact interface and IPs, edge traversal Block. No wildcard/subnet/router rule.
- Source and undo helper: scripts/enable_vmware_labs_telemetry_firewall.ps1. It supports -DryRun, -Enable and exact-match -Undo. Enable/Undo require an already elevated PowerShell token. The helper was independently reviewed and 26/26 static checks passed in tests/scripts/test_enable_vmware_labs_telemetry_firewall.ps1.
- Successful one-time UAC operation was Start-Process -Verb RunAs -Wait with bounded transcript, job af-both-labs-uac-wait-and-log-20261106-03 exit0. The log in untracked artifacts/local records IS_ADMIN=True and state=enabled, followed by exact field inspection. The first two launchers returned too soon and did not install a rule; exact cause is unproved. No UAC bypass.
- Live job af-both-labs-tcp-48761-20261106-a1 exit0: short-lived host listener at 192.168.56.1:48761 accepted one source-labelled connection from Lab1 .128 and one from Lab2 .129. The temporary listener closed. No addon, GuestReader, RIFT input, game restart or VM reboot occurred during this probe.
- Do not expect port48761 to be listening at idle: the diagnostic listener was intentionally temporary. When ready, start AutoFight's own host receiver using validated private configuration. Do not remove firewall rule while it is needed.
- Rollback only if requested: from an ALREADY elevated Windows PowerShell, run scripts/enable_vmware_labs_telemetry_firewall.ps1 -Undo. It refuses drift/unknown rules.

## 4. Host Workbridge versus guest admin

- Host Workbridge 0.16.4-dev, root C:\work, is configured as a sign-in scheduled task named Workbridge MCP, under mrkoo, with RunLevel Limited. The running host token is UAC-filtered, not elevated, even though mrkoo belongs to local Administrators.
- Permanent host Workbridge elevation was discussed and user permitted it as an option, but NOT applied. The one-time firewall UAC elevation solved the network problem. Changing registered task to Highest would not elevate current process until next launch and may disconnect running MCP sessions; source installer C:\work\workbridge-mcp\scripts\Install-Autostart.ps1 also hardcodes Limited and would need coordinated change.
- Both VMware guest PowerShell sessions invoked through cached VMware credentials were VERIFIED high-integrity Administrators in job af-both-guest-privilege-corrected-20261106-b1. No guest credential change or Windows group elevation was needed.
- Lab2 local CodexSandboxUsers group contains CodexSandboxOffline and CodexSandboxOnline. Those sandbox users were NOT added to Administrators. Do not equate sandbox user membership with the already elevated mrkoo guest process.

## 5. Disposable VM Codex access — saved defaults DONE

Joey clarified that BOTH VM guests are disposable development machines intended for broad file access and few approval prompts. The correct native Codex user config defaults are top-level sandbox_mode = "danger-full-access" and approval_policy = "never". They are not Windows UAC or Workbridge MCP settings.

- Lab1 path C:\Users\mrkoo\.codex\config.toml: added exactly those two top-level keys, preserving every original byte after the prefix and all unrelated values. Original 3203 bytes, SHA256 134261decfa6b6106cbd35d42984346a8fbc41206ef52de2a5dc40f32cb9042f. Updated 3265 bytes, SHA256 edc12146d2ce067a3baef01030fbf0f61378ca38cc37b27f530f753314a703ae. Valid TOML and full readback. Successful job af-lab1-codex-safe-filecopy-edit-20261106-a1.
- Original Lab1 guest rollback backup remains at C:\Users\mrkoo\.codex\config.toml.autofight-rollback.2cdea72fe0c64e0b9e5b352196cb00e7.bak; original SHA-256 independently checked. A prior long guest runProgramInGuest command failed before execution; readback confirmed original unchanged before recoverable VMware file-copy fallback.
- Lab2 path C:\Users\mrkoo\.codex\config.toml ALREADY had these exact defaults plus [windows] sandbox = "elevated"; no Lab2 config edit. Codex CLI version 0.165.0 at C:\Users\mrkoo\AppData\Local\Programs\OpenAI\Codex\bin\codex.exe, verified job af-lab2-codex-config-version-readonly-20261106-b2.
- Existing Codex desktop chats and active sessions may have per-chat Ask for approval selected, CLI flags, project overrides or managed requirements. Those effective runtime settings were NOT inspected. For new sessions, use /status and /debug-config, or choose Full access in the app's per-chat permission picker. No Codex, RIFT, Windows or VM process was restarted to apply defaults.
- Windows UAC, ChatGPT/Workbridge connector Allow prompts, OneDrive ACLs and RIFT restricted Lua UI each have separate controls; Codex full access cannot suppress all of them. If prompts recur, capture the exact prompt and identify its origin before changing system-wide security settings.
- Lab2 C:\work exists but C:\work\AutoFight was ABSENT during the inspection; OneDrive RIFT\Interface\AddOns\AutoFight directory exists but its files/load are unverified. OneDrive folder is a cloud reparse path with inherited allow and delete-child-deny ACLs; no global ACL rewrite was performed.

## 6. Lab1 AutoFight files installed, but game UI reload BLOCKED

- Lab1 installed package: one AutoFight GuestReader executable and six addon Lua assets (RiftAddon.toc, wire.lua, producer.lua, live.lua, guidance.lua, main.lua), 8/8 hashes verified after staged promotion, job af-lab1-owned-verified-install-20261105-c3, stage 417a1d6f024143e3a7ad1bb02d715979. Reader EXE SHA256 2b75222f09d0735e0f09cdf18e9f8e271f1a3e4ad7620c93bde950162d9f7369.
- GuestReader has not been started for a live bound session. No fresh reader instance/nonce or in-game /afbind was captured. Installation occurred after the pinned RIFT game process started, so addon-on-disk is not proof of addon-loaded-in-game.
- scripts/lab1_chat_console.py is a reviewed Lab1-only bounded VNC input helper. It supports a fixed focus click, open chat Enter, /reloadui, /afbind with exact 32-hex nonce, and a reviewed fixed RRECollector toolbar Reload UI click. 60-second screenshot/review TTL, exact 1211x757 geometry for toolbar, same game/VM pins, input mutex, release and no automatic retry. Current console tests 21/21 and reported full script regression 62/62 PASS.
- Three prior individually reviewed Enter attempts and a titlebar focus click produced input-sent receipts but NO visible focused blank chat caret. No slash command was sent.
- Older Lab1 RRECollector v0.4.10 visible toolbar had a Reload UI restricted macro, with fixed reviewed click point (1125,553). It is valid ONLY when a fresh exact current screenshot shows that enabled button on the correct RIFT window/version.
- Newer packet at C:\work\AutoFight\artifacts\local\lab1-rre-ui-reload-packet-20261106-01, capture 2026-11-06 07:13:24Z, screenshot SHA d9feb23062685ffba679f767d4677ccb5751c146b5fef3ecbb4047e8e8586d01, showed File Explorer and a Windows Security permissions dialog obscuring RIFT. Review DENIED, expired after 60 seconds; NO toolbar click, reload or key input. Do not reuse this stale packet.
- Guest process MainWindowHandle=0 from VMware GuestOps did not independently prove foreground. RIFT PID was alive at the time. The permission dialog cause is unproved. Fresh visual inspection is required before any UI recovery.
- Do not affect physical host RIFT or Lab2 game via this helper. User has approved practical live VM testing but expects exact reviewed actions and no silent broad restarts.

## 7. Build/test evidence; avoid inflating claims

| Check | Recorded result |
| --- | --- |
| .NET 10 Release solution | 0 warnings, 0 errors, November 5 |
| Core selftest | 149/149 |
| Windows/VM specs | 157/157 |
| Guest contracts | 15/15 |
| WPF nonshown smoke | six categories |
| Python helper suite | 58/58 previous; expanded console suite 21/21 and 62/62 broader report |
| Firewall static PowerShell contract | 26/26 Nov6 |
| Lab1+Lab2 actual TCP host-connect | 2/2 Nov6 |
| Fresh AutoFight XYZ, 480s and movement | NOT PROVED |

Source Git commits:

- 9aea36a — original Oct31 repository separation (historical).
- 8fea7dd — code checkpoint before November 6 firewall/UI work.
- 1caa354 — two-lab firewall helper/tests and guarded toolbar reload capability.
- 681442c — network and privilege readiness handoff.
- f09843f — both VM Codex saved defaults and Lab1 rollback noted.
- Handoff commit itself will be a new hash; read git log -1 rather than trusting a predicted value.

No GitHub push or off-machine backup was made as part of these local commits.

## 8. Priority work queue — functional milestones

1. P0: Read current Git HEAD and Workbridge health; refresh VMX/game PID, start ticks, executable path/hash and guest IP for BOTH labs. Treat all old pins as historical.
2. P0: Fresh Lab1 VNC capture; identify the covering File Explorer/Windows Security dialog. Do not click until the exact dialog/window/action is reviewed; preserve capture hash and a denial option.
3. P0: Restore RIFT visibly to foreground through a verified safe UI operation, then capture NEW review for one authorized /reloadui or exact toolbar Reload UI button; do not replay expired review.
4. P0: Prove AutoFight addon actually loaded into RIFT (disk presence is not proof). Record startup or live binding evidence.
5. P0: Start precisely one AutoFight-owned Lab1 GuestReader with reviewed private config, process identity, new reader instance and nonce; no RR2 runtime service.
6. P0: Start AutoFight-owned host receiver, bound to 192.168.56.1:48761 with exact peer, game pin, token and nonce from current reader; do not guess sensitive data.
7. P0: With new verified focused chat, bind /afbind to the current 32-hex reader nonce; confirm in-game acceptance. No blind input or automatic retry.
8. P0: Run the short read-only owned XYZ telemetry watch (1-120 seconds); require at least two advancing, identity-bound observations, no stale epoch or reordered sequence.
9. P1: Run 30-second warmup plus strict 480-second qualification, accepted source-progress gaps below 4500ms, produce bounded redacted proof/failure receipt.
10. P1: Lab2 read-only addon/reader parity inspection. Its OneDrive addon directory existence does not prove valid six-file package or load. Stage/install/activate only under lab-specific checks.
11. P1: Complete AutoFight-owned guest controller and host transport independent of RR2; test lease, token, release, cancellation, stale target, uncertain outcomes and no auto-retry.
12. P1: One explicit bounded movement pulse in an accepted lab, independently attribute before/after current XYZ and prove displacement. Only then expand target selection, casting, navigation, obstacle handling and multiclient combat.

Agents may parallelize read-only Lab2 parity, receiver-config audit, test-oracle review and GUI troubleshooting, but serialize live guest input to a single owner. Prioritize real telemetry over new speculative frameworks.

## 9. Resumption checklist and commands

- Read AGENTS.md, SESSION_HANDOFF.md, this handoff, README.md, docs/current-status.md, docs/M1-standalone-guest-port-2026-10-31.md; inspect git status and current head before editing.
- Current source version from src/Directory.Build.props, not migration labels. Source changes require rebuild and focused tests before scoped commit.
- Windows PowerShell at C:\work\AutoFight:
  - dotnet build .\Rift.Automation.slnx -c Release
  - dotnet run --project .\tests\Rift.Automation.Specs\Rift.Automation.Specs.csproj -c Release -- selftest
  - dotnet run --project .\tests\Rift.Automation.Windows.Specs\Rift.Automation.Windows.Specs.csproj -c Release
  - dotnet run --project .\tests\Rift.Automation.Guest.Specs\Rift.Automation.Guest.Specs.csproj -c Release
  - dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --smoke-test
  - powershell -NoProfile -File .\tests\scripts\test_enable_vmware_labs_telemetry_firewall.ps1
  - C:\Python314\python.exe -m unittest discover -s tests\scripts -p test_lab1_chat_console.py -v
- After verifying exact fresh guest binding and private ignored host config, a *read-only* owned telemetry watch:
  - dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --owned-telemetry .\artifacts\local\host-lab1.local.json 15
- The private host config must carry real bindIpv4, port, deliveryToken and binding fields (lab, guestIp, reader instance, new nonce, game PID/start/path/hash). These values cannot be guessed or copied to docs/Git.
- The host listener is intentionally normally absent after the TCP probe. An idle closed port does not mean the verified Windows firewall rule disappeared.
- No arbitrary keypress, app focus, game UI reload, VM restart or Windows reboot is implicit in a build or read-only check. User requests approval before disruptive restarts; current screenshot approval must be specific and fresh.

## 10. Open issues, provenance, and explicit limits

- The current Lab1 Windows Security dialog may be unrelated to Codex. Do not call it a proven sandbox error. Need current screen/window diagnosis.
- AutoFight standalone guest Reader and host receiver source/test paths exist; legacy WPF controller/telemetry integration still has optional RR2 bridge dependencies. Keep those boundaries distinct.
- Current-status.md dated November 5 incorrectly claims firewall not enabled and no Lab2 addon present. This handoff supersedes that dated text; a front-of-file pointer is being added.
- On Nov6 host RIFT physical game was not manipulated. Lab2 RIFT was also untouched except read-only guest commands, group/ACL inventory and TCP test. No VM/Windows/RIFT restart occurred.
- Keep secrets, private local configs, guest passwords and captured raw gameplay out of Git and handoff. Preserve exact Lab1 rollback backup until accepted. Do not use cloud/guest Codex CLI as a workaround for usage quotas.

**Bottom line:** Both disposable VMware labs now have working guest-to-host firewall connectivity and saved Codex Full access/no-approval defaults. Lab1 has installed AutoFight reader/addon bytes but still needs a verified in-game addon load and a fresh reader/host binding. Lab2's installed addon/readiness is not independently confirmed. No AutoFight live XYZ or measured game movement has passed yet.
