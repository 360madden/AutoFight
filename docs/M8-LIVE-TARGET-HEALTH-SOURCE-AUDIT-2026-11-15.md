# M8 live combat telemetry source audit — 2026-11-15

Scope: read-only review on verified AMD-DESKTOP. No RIFT input, repo dependency, or live target-health acquisition performed.

## Confirmed source capabilities

- RRECollector source `rre/addons/RRECollector/context.lua` lines 6-10 explicitly allows `health`, `healthMax`, `combat`, unit ID and resource fields in a copied unit projection.
- Lines 39-57 call `Inspect.Unit.Detail("player")`, `Inspect.Unit.Lookup("player.target")`, and `Inspect.Unit.Detail(targetId)`. Target identity and detail are optional and capability-gated; this is source behavior, **not** verified fresh telemetry on the current physical RIFT client.
- `riftreader2/src/RiftReader2.Live/CurrentBuildPairedPositionCandidateReader.cs` lines 21-31 explicitly describes paired native target context as candidate-only, sequential, with unverified atomicity and passive freshness. Do not promote its target-health candidates to qualified combat facts without independent live evidence.
- AutoFight currently has an offline `RacTsvParser` and a target fixture at `tests/fixtures/rac-tsv/target_available.tsv`; fixture records a manual `Inspect.Unit.Detail` target without health fields, and `clockKind=none` / sampledAt unavailable. This cannot drive live combat decisions.
- AutoFight M8 `CombatEffectAssessor` and `CombatOutcomeAssessor` are pure offline comparators; they do not own a live target-health reader or prove damage attribution.

## Smallest viable next integration

1. Implement an AutoFight-owned, bounded target-health observation adapter using the RRECollector Lua projection pattern as **reference only**. It must not depend on RRECollector, RR2, or any separate runtime service. Determine whether a current-build native read or a genuinely fresh addon bridge is the least costly option.
2. Capture exact physical game PID/start/build/player/target IDs, source epoch/sequence, acquisition timestamp, health/current maximum and typed unavailable statuses. Reject missing clock, stale sequence, target swap, invalid HP and unqualified provenance.
3. Prove live passive freshness and changed target HP against an independent UI/addon reference on AMD-DESKTOP before enabling any attack dispatch.
4. Wire validated before/after observations into M8 outcome assessment. Keep command acknowledgment separate from observed health change; do not attribute third-party damage to AutoFight.
5. Only after independent elevated-thinking architecture/code review and end-to-end tests, implement a bounded single-target input action with an explicit STOP and game-window identity gate.

## Review blockers

The mandatory independent elevated-thinking reviewer has **not** run in this session: connected Workbridge agent-assignment functions are metadata/ownership tools, not model-backed reasoning agents, and there is no configurable subagent launcher exposed here. Code changes must remain uncommitted and unaccepted until this review is actually performed.

## Practical decision

Do not add more synthetic combat-state abstractions before qualifying a real target-health provider. The immediate bottleneck is live source evidence, not another offline FSM branch.
