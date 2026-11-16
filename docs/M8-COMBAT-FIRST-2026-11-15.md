# AutoFight combat-first M8 plan — 2026-11-15

Status: approved direction; implementation and live combat not yet accepted.

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
