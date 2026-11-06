---
schema: autofight.handoff.lab2-first-xyz.v1
date_et: 2026-11-06
project: AutoFight
repository: C:\work\AutoFight
branch: main
verified_head_before_handoff: 325e3e404de74c678dbe1607f702ff9c2212297e
scope: Lab2-first-live-XYZ; native-chat-bootstrap; LuaPad-reference
evidence_class: verified-repository-and-historical-live-receipts
current_live_process_identity: REVALIDATION_REQUIRED
guest_reader_started: VERIFIED_AT_2026_11_06_0934_UTC
guest_reader_currently_running: UNKNOWN_NOT_RECHECKED
guest_to_host_transport: VERIFIED_DURING_BOUNDED_WATCH
matched_addon_frame: NOT_VERIFIED
fresh_xyz: NOT_VERIFIED
game_input_this_workflow: NONE
movement: NOT_ATTEMPTED
native_chat_focus: UNVERIFIED
luapad_lab2_disk: PREVIEW20_VERIFIED
luapad_lab2_loaded_or_run: UNVERIFIED
privacy: private-nonce-never-in-command-argv-or-committed-artifacts
---

# AutoFight handoff: Lab 2 Reader → addon binding → first XYZ

**Prepared November 6, 2026 (America/New_York).** This is a detailed, machine-readable continuity packet for the next coordinating agent. It summarizes verified evidence as of the last recorded checks; it is **not** a claim that an earlier PID, nonce, window focus, or process remains current. The authoritative code is in **C:\work\AutoFight**. The project was clean on main at commit **325e3e4** before this handoff was written.

## 1. Executive outcome and immediate blocker

- **GREEN — standalone ownership:** AutoFight has its own C#/.NET 10 guest Reader, host receiver, six-file Lua addon, private Lab 2 staging/configuration helpers and VNC developer environment. RiftReader2 and RRE remain read-only references, not required services for the owned Lab 2 telemetry test.
- **GREEN — guest-to-host transport:** Lab 2's GuestReader was started once; fresh read-only snapshots advanced. A 20-second AutoFight-owned .NET host watch received advancing Reader deliveries over TCP 48761.
- **RED — RIFT content not acquired:** That watch polled 40 times, obtained **zero valid XYZ**, returned 40 unavailable results with reason **source_not_fresh** and exit code 2. A redacted Reader diagnostic reported **matching-challenge-not-found**, zero matching candidates after a complete scan of 3,404,537,856 process bytes. The transport can deliver an unavailable report; this does **not** mean the addon frame was found.
- **YELLOW — likely handshake gap:** AutoFight's addon/live.lua deliberately withholds its nonce-wrapped RR2L1 data until the addon receives the current Reader nonce. We have not proven whether the addon is unloaded or merely unbound.
- **YELLOW — UI gate:** The last examined Lab 2 VNC image showed the RIFT world and an open bag/inventory panel, with no confirmed native-chat caret. Chat review remained DENIED. **No Enter, /afguide, /afbind, /reloadui, mouse click, game restart or VM reboot was performed in this workflow.**
- **GREEN — next-operation code ready:** A private one-shot bind helper is implemented, independently reviewed and offline-tested. It has **not** been executed against the game.

The next value-producing step is **read-only revalidation, verified native chat focus, /afguide status, one private initial bind, then re-observe fresh XYZ**. Do not relaunch the Reader or change the staged Lua addon merely because valid XYZ is absent.

## 2. Data flow and what each observation proves

~~~text
RIFT in VMware Lab 2
  └─ AutoFight Lua addon/live.lua
      └─ Once bound, emits identifiable nonce-wrapped telemetry in RIFT memory
          └─ AutoFight-owned GuestReader scans the matching game process
              └─ Authenticated Reader deliveries over host-only VMnet8 TCP
                  └─ AutoFight-owned in-process .NET receiver / WPF or CLI
                      └─ Freshness + identity + advancing XYZ qualification
                          └─ Later: a separately supervised measured movement pulse
~~~

**Two distinct acceptances must not be conflated:**

1. **Transport passed:** Reader process can produce updates and the host receiver can receive them.
2. **Game observation failed:** No matching nonce-framed addon data was selected, so no fresh XYZ is available. A Reader heartbeat/unavailable delivery is not player position.

A native slash-command response will prove an addon command handler is active, but **command sent ≠ nonce bound ≠ Reader matched ≠ position fresh ≠ game moved**. Require evidence at every layer.

## 3. Pinned environment and historical live evidence

| Field | Last verified value or boundary |
| --- | --- |
| Authoritative repo | C:\work\AutoFight |
| Application version | 0.5.7, last read from src/Directory.Build.props; recheck on next build |
| Lab 2 VMware VMX | C:\rift modding\VMs\RIFT-Lab-2\RIFT-Lab.vmx |
| Guest name | RIFT-LAB-2 |
| Guest VMnet8 IP | 192.168.56.129 at test |
| Host VMnet8 IP / receiver | 192.168.56.1:48761 |
| Guest RIFT process | PID 9752 at capture; must revalidate PID, start ticks, executable SHA |
| Reader process | PID 6252 at launch, start ticks 639295544587249998 |
| Reader instance | 4fe553ed-fe5f-4411-984f-7b9c8a47cdab at capture |
| Lab 2 stage ID | 3fe0dafb233f4763901c18bc951f2501 |
| Reader launch job | af-lab2-reader-single-start-20261106-01, exit 0 at 09:34 UTC |
| Verified snapshot jobs | af-lab2-reader-first-snapshot-fetch-20261106-01 (seq 9); af-lab2-reader-second-snapshot-fetch-20261106-01 (seq 37) |
| Private host bootstrap | af-lab2-bootstrap-host-telemetry-20261106-02, passed |
| .NET receiver watch | af-lab2-owned-host-telemetry-first-20261106-01; 20 seconds, 40 polls, 0 fresh XYZ, exit 2 |
| Reader scan | 3,404,537,856 bytes, 16,413 regions, zero matching candidate frames |
| Last status reason | matching-challenge-not-found (Reader), source_not_fresh (host) |

**Historical, not evergreen:** Process IDs and start times, Windows network addressing, active Reader instance/nonce, and in-game UI may change. Verify current identities before any live action. The host watch was a **bounded diagnostic**, not proof of a perpetually running listener.

**Installed bytes:** Lab 2's six AutoFight addon Lua files matched source by SHA-256 and size (6/6). The separately installed GuestReader EXE and its private JSON matched staged source hashes. Source-hash manifest and detailed provenance are in docs/m1-lab2-addon-content-parity-2026-11-06.md and docs/m1-lab2-reader-deployment-2026-11-06.md. File parity does **not** prove addon load in-game.

**Host firewall:** Lab 2-to-host transport was actually observed on TCP 48761, so do not reclassify this failed XYZ watch as a firewall failure. Preserve the narrowly scoped VMnet8 rule; no blanket firewall disable is required.

## 4. Current implementation, private files and safeguards

These are AutoFight-owned paths, not RiftReader2 services:

| File or location | Purpose |
| --- | --- |
| scripts/lab2_chat_status.py | Reviewed, one-operation native chat opener (one Enter) or exact /afguide status |
| scripts/lab2_chat_private_bind.py | Reviewed initial /afguide bind lab2 using nonce retrieved privately at execution time |
| scripts/lab2_guest_reader_start.py + scripts/start_lab2_guest_reader.ps1 | Guarded once-only Reader start against pinned stage and installed hashes |
| scripts/fetch_lab2_guest_reader_snapshot.py | Fetches and verifies one fresh existing Reader snapshot |
| scripts/bootstrap_lab2_host_telemetry.py | Builds ignored host receiver config from a fresh Reader snapshot, with nonce redacted in stdout |
| scripts/requirements-vnc.txt | Exact 15-package Windows dev-only VNC Python lock |
| docs/local-vnc-toolchain.md | Instructions for independent AutoFight VNC environment |
| artifacts/local/venv-vnc | Ignored Python 3.14.6 virtual environment with vncdotool 1.4.2 and Pillow 12.3.0 |
| artifacts/local/host-lab2.local.json | Ignored private owned receiver config; treat as sensitive |
| artifacts/local/lab2-game-identity.local.json | Ignored pinned game identity |
| artifacts/local/lab2-guest-reader.local.json | Ignored Reader-specific private binding/stage values |

**Private-bind protocol (not yet executed):**

1. Read current Reader/game/VM/stage identity; ensure the same Reader has a fresh snapshot (30-second Reader TTL) and the ignored host receiver binding matches.
2. Prepare a new, unique capture directory using the AutoFight-owned VNC venv. Capture creates an initially **DENIED** plan/review. Its operation contains the SHA-256 of the nonce, **not the raw nonce**.
3. Confirm in the screenshot that the correct Lab 2 game is foreground, inventory/panels are closed, chat entry is open, empty and visibly focused, other controller and queue idle. Do not infer caret focus from mere RIFT visibility or from a Windows Session 0 GuestOps foreground check.
4. Only after an explicit, fresh review (60-second capture limit), execute once. The helper re-fetches and revalidates the same Reader/nonce/game/host binding, writes a durable one-shot attempt marker **before input**, then sends exactly the private /afguide bind lab2 command plus nonce followed by Enter. No alternate arbitrary chat command is allowed.
5. If partial send, key release uncertainty, stale identity, denied review, missing focus or changed Reader occurs, record blocked/uncertain; **never auto-retry**. Reconcile the exact in-game state and generate a brand-new reviewed plan where appropriate.
6. **No post-bind screenshot** is recorded: RIFT native chat history may briefly show the nonce. Never paste nonce-bearing shell arguments, command output, chat images, or old Workbridge job logs into reports.
7. Validate success by separately fetching a fresh, redacted Reader observation and watching advancing XYZ; do not treat a command-send receipt as a successful bind.

The private Reader nonce and delivery token belong only inside ignored files/in-memory execution. A historical *pre-fix* Workbridge host-bootstrap job stdout retained the Reader nonce; the stdout-redaction patch prevents future disclosure but did not retroactively erase that old job log. Do not reprint it. Initial bind uses /afguide bind lab2; if the Reader is replaced, source review identified an explicit /afguide rebind lab2 path, not an unsafe replay of the old nonce.

**Windows Session detail:** GuestOps PowerShell runs in Session 0; RIFT UI is in Session 1. The last GuestOps foreground probe could not prove that native chat received keyboard focus. The VNC screenshot and review gates are therefore material, not ceremony.

## 5. LuaPadModern decision and Lab 2 inventory

Joey's architecture choice is clear:

- Use **RIFT native chat** only as temporary scaffolding for slash commands and small setup probes. Do not try to rewrite or replace the built-in chat code.
- Use **LuaPadModern** as the preferred interactive Lua scratchpad once its actual Run button and execution semantics are tested.
- Keep **AutoFight** runtime-independent of LuaPad. Consider a tiny AutoFight-owned status/bind button only if the native chat gate remains a genuine blocker; do not build a second Lua editor.

Read-only inventory established that Lab 2 has LuaPadModern **Preview 20** on disk at C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\luapad, with Inspector and LibSimpleWidgets. Physical RIFT has Preview 21 editor-focus acceptance, based on LuaPadModern's own RiftTextfield focus API. This proves neither native RIFT chat focus nor Lab 2 LuaPad loading/Run. Handle a Preview 21 deployment in a separate controlled step **after first XYZ**, unless priorities change. Do not modify AutoFight's six hash-pinned Lua addon files to update LuaPad.

## 6. Test and review receipts

| Evidence | Result |
| --- | --- |
| Lab 2 guarded Reader start | Live exit 0, launched once, no RIFT input/reload |
| Reader snapshot fresh fetches | Passed, seq 9 and 37, same Reader instance at test |
| Host private receiver bootstrap | Passed from fresh snapshot; private file created |
| .NET host watch | Transport advanced; 40 polls, 0 XYZ, source_not_fresh |
| AutoFight-only VNC environment | Python 3.14.6, 15 pinned packages, pip check PASS |
| Read-only Lab 2 screenshot via own venv | af-local-vnc-venv-capture-smoke-20261106-01, PASS; review DENIED |
| Private bind helper focused tests | 14/14 PASS |
| Independent combined Python regression | 157 run, 1 expected skip, zero failures |
| Private bind independent source/privacy review | PASS; no live input |
| Reader-start PowerShell static checks | 32/32 PASS |
| Reader-install PowerShell checks | 26/26 PASS |
| Live in-game addon load | NOT VERIFIED |
| Sustained 480-second owned XYZ qualification | NOT RUN / NOT VERIFIED |
| Closed-loop movement feedback | NOT ATTEMPTED |

C# Release build, WPF smoke, Core/Windows/Guest synthetic regression have older green receipts in docs/current-status.md and README.md. They were **not rerun merely for preparing this handoff**; do not misdate them.

## 7. Recent commits and change inventory

These commits were verified on main, and the worktree was clean before this handoff:

| Commit | Meaning |
| --- | --- |
| 82758a8 | Prepare reviewed Lab 2 Reader start and owned telemetry workflow |
| 28a81ad | Live Lab 2 Reader/transport evidence and private nonce stdout redaction |
| f2fbf67 | Independent AutoFight VNC diagnostic environment |
| 57159a6 | Independently reviewed one-shot private Lab 2 binding helper/tests |
| 325e3e4 | LuaPad version and controlled first-XYZ documentation |

No AutoFight six-file addon modification, RR2 modification, physical RIFT input, Lab 2 game reload, VM lifecycle change or live movement was part of these commits.

## 8. Next coordinator: strict order of operations

**P0 — Re-establish live freshness (read-only).**
- Inspect Git status/HEAD, AGENTS.md, SESSION_HANDOFF.md, docs/current-status.md, this handoff and linked Lab 2 evidence.
- Check Lab 2 VM/RIFT process identity, executable hash, the already-started Reader PID/start, Reader instance and current private snapshot. Do not blindly rerun the once-only Reader starter or overwrite private host config.
- Confirm host-only VMnet8 transport still works if needed; reuse the owned receiver, not RR2 runtime services.

**P1 — Restore UI readiness without blind input.**
- Last image: inventory panel open, no chat caret. Ask Joey to close inventory or use a separately bounded, reviewed UI step if implemented. Capture a new VNC screenshot using artifacts/local/venv-vnc/Scripts/python.exe.
- Only after explicit review of safe state, use scripts/lab2_chat_status.py to open chat with one Enter; then capture a distinct review showing the actual empty focused native entry.
- Send reviewed /afguide status to determine whether the addon command handler is loaded. If absent, diagnose addon enabling/loading before attempting a private bind.

**P2 — One private bind and direct feedback.**
- With a fresh Reader and an empty, focused chat review, execute scripts/lab2_chat_private_bind.py's capture → review → execute flow. Never put raw nonce in command args or screenshots.
- Obtain a fresh **redacted** Reader snapshot. Expected progress: matching candidate appears and source_not_fresh clears when the host consumes advancing valid XYZ. If no matching frame appears, distinguish addon disabled, old/stale nonce, game-memory scan mismatch, focus/command failure, and login/session drift; do not invent telemetry.
- Re-run a bounded host watch using ignored artifacts/local/host-lab2.local.json. Check two or more fresh advancing XYZ samples, sequence/age/source pin.

**P3 — Qualify before gameplay.**
- Run the AutoFight-owned 480-second qualifier with its configured warmup, strict source identity and advancing feedback thresholds. If failed, preserve diagnostics and investigate missing samples.
- Only after sustained valid XYZ, separately authorize exactly one bounded movement pulse and measure before/after XYZ and input-release receipt. No targeting or combat action until this closed-loop baseline passes.

**P4 — LuaPad and later UI.**
- Once initial XYZ is proved, upgrade/test Lab 2 LuaPad from Preview 20 to the independently verified Preview 21 source in a separate staged change, with no implicit reload/restart. Test harmless Lua execution through Run and observable output.
- Keep the option to use **Lab 1** as an alternate test lab if Lab 2 becomes unavailable, but do not copy stale Reader pins or reuse Lab 2 private nonce there.

## 9. Stop conditions and open questions

- **No proof of addon load** in Lab 2; /afguide status has not been sent.
- **No confirmed native-chat caret** in the last screenshot. Do not type a secret-bearing bind command into an unverified text target.
- **Reader runtime age unknown** as of this handoff; it was last launched 09:34 UTC, not guaranteed still alive.
- **No live XYZ** despite successful transport. Investigate handshake/load first; do not disable firewall or change network for this symptom.
- **No restart/reload** required by the presently known blocker. If a restart becomes necessary, inform Joey before the operation and refresh exact Reader/nonce/stage pins afterward.
- **LuaPad installed ≠ loaded ≠ Run works**, especially Preview 20 in Lab 2 versus Preview 21 physical reference.
- A bind command may appear in native chat history; avoid a post-bind screenshot and treat nonce as ephemeral.

## 10. References

- docs/current-status.md — chronological project status; newest top banner supersedes historical paragraphs.
- docs/m1-lab2-chat-focus-and-first-xyz-readiness-2026-11-06.md — primary Lab 2 evidence and first-XYZ sequence.
- docs/m1-lab2-addon-content-parity-2026-11-06.md — six addon file SHA/size manifest.
- docs/m1-lab2-reader-deployment-2026-11-06.md — GuestReader staged/installed receipts.
- docs/local-vnc-toolchain.md — reproducible AutoFight-owned screenshot tooling.
- docs/HANDOFF-AUTOFIGHT-VMWARE-LABS-2026-11-06.md — broader two-lab transport/permissions handoff.
- docs/M1-standalone-guest-port-2026-10-31.md — standalone guest-reader architecture and original acceptance gaps.
- AGENTS.md, SESSION_HANDOFF.md, README.md, docs/automation-platform-architecture.md and docs/automation-platform-roadmap.md — standing project instructions.

**Bottom line:** The Reader is not the same as its game data. We have a working transmission path and an unverified addon handshake. The first-XYZ milestone is closed only when **live, identity-pinned, advancing X/Y/Z from the actual RIFT client** arrives without a running RR2 service.
