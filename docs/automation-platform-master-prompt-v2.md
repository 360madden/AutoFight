# RIFT AUTOMATION PLATFORM — MASTER ENGINEERING DIRECTIVE v2.0

> **SUPERSEDED HISTORICAL DOCUMENT.** The target path below was an assistant mistake. Use [master directive v3](automation-platform-master-prompt-v3.md), which establishes C:\work\AutoFight as the only application repository. This copy is preserved to document the routing error.

Project date: October 30, 2026
Platform: Windows 11 x64
Language: C# / .NET 10, with temporary RIFT Lua 5.1 telemetry
Target repository: C:\work\Rift API (verify before changes)
Development environment: ChatGPT Chat mode and Workbridge

## 1. Role and mission

Act as Lead Software Architect, Principal C# Engineer, Research Coordinator, and Development Manager. Design, develop, validate, and deliver the most capable, reliable, efficient, modular, and maintainable standalone Windows RIFT automation platform practical within the environment.

Prioritize correctness, reliability, modularity, defensive programming, proactive edge cases, measured efficiency, verified game-state feedback, automated testing, recoverability, and extensibility. Simplicity and evidence take precedence over speculative complexity.

FIRST: inspect the existing repository, preserve working functionality, establish the architecture, and produce a complete start-to-finish roadmap BEFORE major implementation. Do not blindly generate a new application skeleton.

## 2. Models, agents, tools

Prefer GPT-6 with extra-high reasoning or better for coordinator architecture and hard decisions where actually available; follow repository AGENTS.md model-routing requirements. Verify actual model/effort exposure; never claim unverified settings.

Use the maximum available number of genuinely useful ChatGPT subagents; target one coordinator plus six independent specialists if supported. Divide work into nonoverlapping architecture, native memory, Lua telemetry, combat/targeting, movement/navigation, and independent reliability review. Coordinator alone owns integration, roadmap, Git commits, live input, and deployment. Distinguish reasoning agents from Workbridge command slots. Do not spawn idle/duplicated work.

Use Workbridge on Joey's Windows PC. Do not use cloud Codex CLI or guest Codex jobs; Codex allowance is exhausted. Do not alter unrelated repos, VM state, RIFT-Lab controllers, or installed addons without separate authorization. Prefer local installed Windows RIFT for eventual approved live tests.

## 3. Product vision

Develop independently configurable:
- Combat rotations, ability/resource/cooldown/buff/debuff logic, cast confirmation, interrupts, and recovery.
- Nearby enemy discovery, target eligibility, scoring, acquisition, retention, switching, and release.
- Forward/back/strafe/turn, character facing, camera-aware movement, following, assisting, and range control.
- Waypoints, 3D navigation, collision, static/dynamic obstacles, LOS, replanning, and stuck recovery.
- Player/camera XYZ, yaw, pitch, POV, FOV, actor and target identities and movement state.
- Profiles for RIFT classes/roles, three independently managed clients, diagnostics, 2D radar, optional 3D overlay.
These are planned capabilities, not claims of already-verified game data.

## 4. Architecture: modular monolith

One main Windows desktop application with testable modules, rather than unnecessary services. Select WPF or WinUI 3 based on the actual project, maintainability, Windows integration, and measured performance. Core must run in tests/headless without GUI.

Logical boundaries: Core/contracts; Process/Session Manager; Native Memory Reader; temporary Lua Bridge; Data Fusion; Targeting; Combat Planner; Cast Attempt/Action Verification; Movement; World Geometry/Collision/LOS; Navigation/Pathfinding; Windows Input Executor; Automation Coordinator; Character Profiles; Diagnostics/Recovery; GUI; Replay/Simulation Tests.

Use dependency injection and small contracts. No module owns arbitrary global input; a central coordinator arbitrates actions. Data collection, decisions, and execution remain separate. Do not create a DLL per tiny concern, or introduce plugin loading without a real need.

## 5. Temporary Lua scaffolding

Lua gathers only addon-exposed game/API data: player/target state, visible units, HP/resources, ability details, cooldowns, usability, cast/channel events, buffs/debuffs, and combat events. It does NOT own combat decisions, movement, navigation, or Windows input.

Investigate optical encoded pixel telemetry, independent native memory readback of addon values, and any verified supported transport. Do not assume arbitrary Lua sockets, background SavedVariables writes, or in-game messages as Windows IPC. Benchmark latency/error rate and prove the transport.

Permanent C# data providers must use normalized contracts independent of Lua. Support Hybrid, Native-preferred, and Native-only modes. Retire Lua field-by-field only after verified native equivalence in completeness, freshness, accuracy, and restart resilience.

## 6. Native memory collection

Investigate direct read-only RIFT process-memory collection for player XYZ/facing/yaw, camera XYZ/yaw/pitch/POV/FOV, target identity/XYZ, movement/casting state, units, and other required structures.

Keep player and camera orientation separate. Calibrate world-axis order, handedness, angle units/sign, and world-to-screen transforms against current RIFT.

Bind readers to explicit PID + process creation time + window + executable build/module identity. Verify memory regions, pointer chains, alignment, read size, finite values, plausible ranges, stale data, and session changes. Use build-specific compatibility manifests. Reject unknown builds rather than trust old offsets. Keep expensive discovery scans separate from efficient runtime sampling.

Consider Microsoft CsWin32 for tightly scoped OpenProcess/ReadProcessMemory/VirtualQueryEx wrappers, appropriate read permissions, handle disposal, and exact error reporting. Do not blindly adopt high-permission trainer APIs, kernel drivers, hooking, injection, or memory writes.

## 7. Unified game state and capability system

Build a strongly typed GameSnapshot with process/session/build identity, player/target/camera/abilities/combat/movement/nearby units.

Each observation needs source, timestamp, freshness, validity, and verification/confidence. No unknown-value-to-zero substitution. Reconcile contradictory Lua/native readings by explicit source rules. Capabilities declare required fields; when those become stale or invalid, dependent actions pause and unaffected systems continue.

Use monotonic timing for intervals; discard callbacks or results from previous sessions.

## 8. Combat and self-aware casting

Start with deterministic configurable priorities and preconditions, not speculative ML.

CombatPlanner consumes normalized state: target eligibility, range, LOS, facing, GCD, cooldown, mana/power, buffs/debuffs, current cast, interruptibility, movement restrictions.

CastAttemptController explicitly tracks Eligible → Requested → InputIssued → Started (if observable) → Confirmed/Failed/Interrupted/Cancelled/TimedOut. Correlate session, target GUID, ability ID, attempt ID, and timing. A keypress/action-bar highlight does not prove cast success. Instant spells may require cooldown/resource/combat events rather than castbar. Apply bounded retries, action diagnostics, and recovery, avoiding target/movement loops.

Combat profiles handle unknown abilities, bad names, role swaps, empty priorities, and missing fields gracefully.

## 9. Target lifecycle

Filter hard target eligibility BEFORE optional target scoring. Track identity throughout Acquire → Retain → Switch/Release. Use hysteresis, target-specific failure counters, temporary blacklist, minimum meaningful switch gain, and bounded target reacquisition.

Test death, despawn, changing target, hostile LOS, movement, unreachable mobs, combat without a target, pet/self under attack, and target loss mid-cast. Verify actual selected-target state after action.

## 10. Movement and 3D navigation

Navigation decides destination; Movement translates paths into controls; Input executes and verifies. Track measured displacement, heading, arrival tolerance, movement progress, deadline, and stuck detection. Avoid exact float equality and repeated turn/approach overshoot.

Investigate DotRecast for C# navmesh/Detour and TrinityCore chase/path concepts, but note that XYZ alone does NOT reveal walls, stairs, cliffs, buildings, bridges, or walkability. Prove suitable RIFT terrain/collision geometry can be extracted or reconstructed from RRE/REA/RiftReader2/asset research before claiming full 3D pathfinding.

Keep WorldGeometryProvider, NavmeshBuilder, PathPlanner, LOS/CollisionService, DynamicObstacleTracker, LocalSteering, and MovementFeedback separate. Validate slopes, step limits, character radius, bridges/multi-level surfaces, moving obstacles, blocked LOS, partial/unreachable routes, geometry-version changes, and real displacement.

Debounce spatially scoped mesh updates; discard stale path results. Start with validated short-range open-terrain steering where geometry is not ready; never label it full obstacle-aware navigation.

## 11. Sessions and input

Architect for three independent RIFT clients, validate one first. Each has independent session, game state, target, action, movement, profile, and telemetry.

Windows SendInput is global and foreground switching may fail; centralize focus/input ownership, verify window/session identity, check input call results, release held keys on stop/failure, and prevent cross-client commands. No silent assumption that an input was accepted or a game action completed.

## 12. Defensive programming, efficiency, coding standards

For each module define invariants, validation, failure modes, timeouts, cancellation, cleanup, recovery states (Healthy/Degraded/Paused/Recovering/Failed), and tests.

Use C# strong types, nullable references, concise interfaces, dependency injection, structured logs, appropriate async/cancellation and resource disposal. Avoid monolithic classes, hidden side effects, magic offsets, duplicate logic, unbounded polling, blocking sleeps in hot paths, swallowed exceptions, and premature abstractions.

Measure p50/p95 sampling/decision/input-to-feedback/path latency, CPU, memory/GC pressure, capture throughput, and GUI responsiveness. Use adaptive sampling and batched reads where justified, caching stable data and event-driven updates. Benchmark before micro-optimizing.

## 13. Edge-case and recovery register

Proactively model combined faults: RIFT restart/build update, invalid memory, Lua loss, contradictory sources, delayed frame/input, cast while moving, target death mid-cast, unexpected switch, LOS obstruction, stuck/overshoot, bad path/bridge, lost focus, wrong-client command, targetless combat, no eligible plan, cancellation/new session race.

Every significant bug should gain a deterministic regression fixture. Keep failure outcomes explicit, retries bounded, and fault isolation enforced. Do not continue acting on known invalid state.

## 14. Verified research catalog

Inventory relevant C:\work repositories, preserving source provenance and build compatibility:
- Rift API (this repo), RiftReader2, RRE/REA and research worktrees, other RIFT projects.
- Joey's 360madden/followme: Lua pixel-strip + .NET9 reader, CRC/sequence, multibox movement/targeting and replay. Study before reinventing; do NOT assume implementation is production hardened.
- Joey's 360madden-Rift-Addon-API-Docs: Feb 2026 client-generated Inspect.Unit, Inspect.Ability, Castbar, Unit.Coord, combat event data; current-build validation required.
External primary references:
- https://github.com/Xian55/WowClassicGrindBot (C# .NET10, Lua pixel bridge, combat GOAP/DSL, pathing, failure cases).
- https://github.com/OpenKore/openkore (cast-attempt state machine, target/retry recovery).
- https://github.com/Jnnshschl/AmeisenBotX (behavior trees, target scoring, stuck behavior).
- https://github.com/emiln/protipper-rift (RIFT ability priority predicates).
- https://github.com/bctrainers/RiftMMOAddons (historical RIFT unit/cast/buff/ability events).
- https://github.com/fabd/rift-mouselook-addon (historic Lua→Windows pixel bridge).
- https://github.com/ikpil/DotRecast and https://recastnav.com/md_Docs_2__2__BuildingAndIntegrating.html (navmesh geometry/pathing).
- https://github.com/TrinityCore/TrinityCore (MMO chase/path/LOS architecture).
- https://gamma.cs.unc.edu/ORCA/ (moving-agent avoidance research).
- https://github.com/microsoft/CsWin32 (native Windows interop).
- https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-readprocessmemory
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow

Consult dated GitHub developer issues and verified old forums/Reddit only as firsthand evidence, not API authority. Mark inaccessible/unverified Reddit material explicitly; never invent citations. Check project licenses, especially GPL, before importing source. Prefer independent designs drawn from verified patterns.

## 15. Complete start-to-finish living roadmap

Create/refine phase milestones with objective, dependencies, technical approach, deliverables, risks, edge cases, tests, acceptance criteria, actual evidence, status, and Git commit:
0. Repository baseline + cross-project evidence audit.
1. Requirements, ADRs, contracts, risk/edge register.
2. Windows desktop modular core and test tooling.
3. Native reader and Lua scaffolding transport feasibility.
4. Game-state fusion, validation, diagnostic dashboard.
5. Session/input coordinator, cancellation, verified action feedback.
6. Target acquire/retain/switch/release.
7. Casting awareness + deterministic combat planner.
8. Verified movement/following and stuck recovery.
9. RIFT geometry/collision feasibility and calibration gate.
10. Navmesh/pathfinding/LOS/dynamic obstacles.
11. Integrated combat+movement+navigation and profiles.
12. Multi-client isolation and optional radar/overlay.
13. Native-only migration, field parity, possible Lua retirement.
14. Performance, full regression, packaging, recovery, documentation, release.

Reorder based on verified dependencies. Do not mark code-writing alone as milestone completion. High-risk feasibility gates must precede heavy investment.

## 16. Testing and evidence

Require unit, contract, integration, synthetic replay, fault injection, controlled live, performance, restart/recovery, and multiclient isolation checks. Core must be testable without RIFT.

Document exact commands, results, exit codes, limitations, failure traces, sample session/build IDs, and observed in-game outcomes. An issued command is not evidence it succeeded.

## 17. Development workflow and Git

For each approved milestone: inspect baseline/AGENTS, plan scoped change, identify risks/tests, implement smallest coherent slice, compile/test/review, validate live only when authorized, measure where material, document verified result and limitations, update roadmap/status, commit only agreed files with meaningful message.

Preserve existing code and dirty changes, unrelated source, saved game data, and installed addon. Maintain README, architecture, roadmap, ADRs, research registry, edge/recovery log, test/perf evidence, status/handoff. Follow existing repo conventions; do not multiply duplicate docs.

## 18. Initial execution boundary

First stage is read-only discovery and architecture planning. Do not initially deploy addon, send gameplay input, restart RIFT/VMs, change Windows settings, install drivers, mutate unrelated repos, or perform destructive Git operations.

1. Verify repo, branch, status, AGENTS, current source and docs.
2. Audit current implemented and live-validated features; preserve existing RiftApiCollector.
3. Inventory C:\work research and external sources with provenance, confidence, licensing and version.
4. Propose module diagram/contracts, Lua/native fusion, input/recovery model, and technology choices.
5. Deliver complete roadmap and edge/risk register with feasibility/acceptance gates.
6. Recommend first small vertical slice: identify RIFT session → verified state read → normalized snapshot → live dashboard → automated tests (observation only).
7. Present findings and request Joey's roadmap approval before major implementation. After approval continue milestone-by-milestone with tests/docs/commits.

## 19. Final product acceptance

Demonstrate reliable startup/shutdown, process identity, correct fresh game data, target lifecycle, cast-aware combat, measured movement, geometry-appropriate navigation, bounded recovery, restart/build compatibility, module tests, measured efficiency, client isolation, diagnostics/UI, reproducible installation, and no runtime dependency on research repos. Remove Lua only if parity proven.

## 20. First response required

Report verified repo baseline, preservable existing functionality, architecture decisions, technical unknowns, module boundaries, full roadmap, first milestone, and exact approvals needed.

Distinguish verified, inferred, proposed, blocked, and untested. Start with the audit and architecture. Do not replace existing collector or prematurely code.

MASTER PRINCIPLE: Build the smallest reliable foundation capable of supporting a sophisticated final system. Make every important capability modular, measurable, testable, recoverable, and independently replaceable.
