# AutoFight YEA — master engineering directive v3

Date: October 31, 2026
Platform: Windows 11 x64; C#/.NET 10 WPF
**Target and sole application repository: C:\work\AutoFight**

This directive supersedes v2's wrong C:\work\Rift API path. Never use the API-discovery research repo as AutoFight's development destination. Rift API, RiftReader2, RRE/REA, and other C:\work projects are references, not AutoFight's source folders or build/runtime dependencies. Do not overwrite their files while working on AutoFight.

## Mission

Develop a practical standalone RIFT combat and movement automation application, with independently tested game-state acquisition, target selection, cast-aware feedback, real measured movement and eventually obstacle-aware navigation and multiclient operation. C# owns UI, memory acquisition, decisions, targeting, movement and input. Lua is temporary observation scaffolding if useful, potentially retired field by field after native parity. Architecture should remain modular but not process-heavy.

## Current state and starting point

Read README.md, AGENTS.md, docs/current-status.md and existing source/Git status. The imported 0.5.7 WIP telemetry JSON export is completed and tested in AutoFight; preserve the original research repo. Standalone Windows builds, offline replay, controller bridge, pinned VM telemetry watches and the eight-minute qualification UI exist. No current live feedback window, real input-caused displacement, combat or navigation has been accepted. Treat prior source-root commits as historical provenance only.

## Work priorities

1. Keep the new AutoFight repository cleanly separated, tested and locally committed in useful milestones.
2. Coordinate with the RiftReader2 lab owner for current RR2 receiver/source-pin renewal in both existing VMware guests. Do not manage a VM, change another repository or transmit gameplay input incidentally.
3. Establish fresh telemetry identity and sustained source advancement in AutoFight, one lab at a time.
4. Prove the bidirectional loop: select the correct VMware RIFT client, send one explicitly supervised bounded control, observe a separately attributed response, and stop if uncertain.
5. Build useful game-state coverage: player/target poses, identity, health, ability/cooldown/cast state. Research and validate native memory readings against independent current-build observations.
6. Introduce deterministic targeting, casting, facing/range and movement decisions, with bounded recovery. Add geometry and navigation only after current-build surfaces/collision, coordinate calibration and independent LOS checks.
7. Expand carefully to multiple clients and profiles after one client is objectively functional.

## Implementation discipline

- Preserve history and unrelated work, review diffs, run deterministic tests, document measured limitations, and commit each accepted functional milestone.
- Use maximum genuinely useful ChatGPT specialist agents with exclusive file ownership and an independent reviewer when available; coordinator owns integration and Git. Do not mistake Workbridge's job slots for reasoning agents or invoke a prohibited cloud/guest Codex quota.
- Prefer existing interfaces and direct, bounded code over speculative frameworks. Run builds/tests from C:\work\AutoFight using Workbridge and the pinned .NET SDK.
- Distinguish source observation from stale/historical data, command delivery from guest action, and guest action from verified RIFT response. Unknown never silently becomes success.
- Never copy raw credentials, private controller files, user captures or generated artifacts into Git.
- Maintain optional interoperability with RiftApiCollector's frozen manual TSV protocol using owned parser source, but do not embed its research repository.
- Follow current user authorization for live experiments; no automatic input, installed-addon or VM lifecycle changes occur during ordinary build, test or packaging tasks.

This document records planned capabilities, not an assertion that combat automation is already operational.
