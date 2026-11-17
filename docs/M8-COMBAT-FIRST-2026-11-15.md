# AutoFight combat-first M8 plan — 2026-11-15

Status: approved combat-first direction; offline preview foundations exist, but live combat is not yet accepted.

> **November 16, 2026 offline clock-hardening receipt:** `StationaryCombatPlanner` now maintains a per-session nondecreasing invocation-time watermark across `Evaluate`, `BeginPreviewAttack`, and `CompletePreviewAttack`. Negative or regressed clocks invalidate readiness/pending intent and latch reset-required; equal timestamps remain valid, and explicit `Reset` begins a new clock epoch. Additional deterministic tests cover expired-readiness revival, out-of-order acknowledgments, negative calls, foreign-session isolation and lower-clock Reset. **.NET 10 Release build: 0 warnings/errors; 182 Core + 232 Windows + 39 Guest = 453/453 specs; WPF smoke 7/7.** Independent reviewer `/root/combat_audit` reviewed exact source/test hashes and reported **PASS, no blocking findings**. Review limitation: `nowMs` must be the current invocation time, not a delayed event timestamp. No RIFT input, live telemetry or combat was performed. Do not infer combat capability from this fix.

> **November 16 offline target-continuity follow-up:** Previously tracked target loss, identity swap, or death now latches an explicit reset even when unrelated player/range/hostility fields are unavailable. Initial target acquisition, pending attack invalidation, STOP and recovery retain their precedence. New synthetic tests cover lost→same-ID, dead→same-ID and partial-field bypass attempts. Independent reviewer `/root/combat_audit` identified and then verified correction of the partial-field ordering defect, recording **final PASS** against exact code/test hashes and the same 453/453 spec suite. This does not independently prove actor generation/freshness or actual combat.

## Mission
AutoFight is a standalone RIFT combat bot. First acceptance is one stationary character selecting a hostile target, issuing a bounded basic attack through AutoFight-owned input, observing target/player state and returning to idle or recovery. Navigation is supporting work, not the product.

## Implementation sequence
1. Inventory existing AutoFight Core contracts and RR2/RRE reference implementations read-only. Determine which current-build target identity, hostility, range, HP and cast/ability signals are actually qualified. Unknown signals must remain unavailable, not inferred.
2. Implement a pure C#/.NET 10 stationary-combat FSM in Core: Idle, Acquire, Ready, AttackPending, ObserveResult, Recover, Stopped. Use immutable source-pinned observations, monotonic ages, explicit transitions, bounded attempts and latched STOP. No live input in this slice.
3. Build deterministic tests for stale/duplicate samples, wrong player/target, friendly/dead target, unavailable health, missing ability readiness, target swap, failed cast, timeouts, stop, cancellation and restart.
4. Add an AutoFight-owned adapter for a single bounded basic-attack command only after current-build target and player observations are qualified. RR2 movement/focus and RRE addon/API patterns are reference scaffolding, not runtime services.
5. Test one user-authorized, single-target stationary encounter on physical RIFT with before/after target and player telemetry. Preserve exact process/build/player/target/action receipts and independent visual or addon corroboration. Do not infer combat success from input insertion.
6. Add deterministic recovery and repeat encounters only after first-loop acceptance; then use RR2 movement and RiftWorld routes for approach and patrol.

## Review workflow
Every implementation milestone: light/instant implementation and tests where agent model selection is available; independent elevated-thinking architecture/code review; coordinator resolves findings; run focused and solution tests; record actual review evidence; scoped commit. Never describe Workbridge command slots as reasoning subagents. If elevated independent reviewer cannot be launched, hold review gate open and disclose it.

## Source boundaries
RR2: XYZ, focus/input, waypoint controller and STOP techniques. RiftWorld: geometry, collision and offline route planning later. RRE: current-build target, combat state, ability and casting research. All copied/adapted code must live in AutoFight; no runtime imports, services, or checkout paths.

## Immediate blocker
M7 has read-only physical XYZ but no qualified live target identity/hostility/HP/ability readiness or executed combat action. The first implementation must keep these as explicit unknowns and must not send live input.
