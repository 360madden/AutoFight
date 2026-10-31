# G1 — RIFT world geometry feasibility and next bounded experiment

Date: 2026-10-31 (America/New_York)
Disposition: **conditional go for a read-only, offline asset-extraction proof; no go yet for a game-qualified 3D collision/navmesh provider**.

## Scope and evidence classes

This is a bounded review of existing local research and primary upstream format/source documents. No RIFT executable, asset archive, addon, VM, controller or input was touched; no viewer was run, external code imported, package installed or app runtime dependency added. Historical source pointers are research references, not AutoFight runtime dependencies.

| Lead | Actual evidence | Remaining qualification |
| --- | --- | --- |
| Local old RiftReader waypoint graph | `C:\rift modding\RiftReader\scripts\record-navmesh.py` lines 56-82 rounds X/Z to grid cells, stores Y as a value, and connects all nodes within three cells; `find_path` is BFS. `build-navmesh.py` also snaps X/Z and contains input-driving code that was not executed. | Cannot represent walls, proven traversability, stairs or stacked floors. It is **not** a recovered mesh or safe navigation provider. |
| Historical RRE native casts | `C:\work\rre\docs\agents\collision.md` records old-build Havok ray, linear and phantom-cast code/caller evidence. | No player-owned callable ABI, collision filters, units, hit semantics or current-build validity. Do not call or hard-code old RVAs. |
| Changed client build | `C:\work\rre\docs\REA-RIFT-BUILD-UPDATE-2026-10-27.md` records a host executable change from SHA `7c356c9dde9e12fcbff3c08d953b0630b48b1510a0ffb36e4ec164606e7b17bd` to `1935a05c9db6d468e34e0055014a4a99c1b5afceb1695884e24600f5c20a4be8`, plus changed asset archives/manifests. | Exact currently running VM game and current asset sample identities need separate proof. |
| RIFT asset formats | The Ghar Station documents terrain NIF chunks named like `world_terrain_256_0_split.nif`, associated world CDRs, and CDR object references to NIF/HKX with position, rotation and scale. | No current-build source-hashed region NIF/CDR/HKX fixture or parser acceptance exists locally. |
| TelaraFly upstream | RIFT-specific offline Unity viewer using a local manifest/assets; GitHub `imathrowback/telarafly`, examined historical release tag `2025`, pinned source commit `41aacc0485ed2c90ceb1df7cdb7653853cc52d18`. | Current RIFT asset compatibility and original collision decoding unverified; repository license metadata/root license not established. Inspect as reference; do not copy implementation until terms are resolved. |

**Critical renderer/collision distinction:** in pinned TelaraFly `Assets/WorldStuff/CDRParse.cs`, terrain NIF and world-object transforms are read, but lines 410-414 skip entries marked collision-only NIF. `Assets/NIF/NIFLoader.cs` lines 347-351 constructs a Unity `MeshCollider` from its *visual* mesh. A rendered world with colliders is therefore **not evidence of the authoritative RIFT collision surfaces**; invisible blockers and walkability semantics can be missing. Static meshes also cannot locate server-driven NPCs, doors or other dynamic interactions.

**Bounded local inventory:** the inspected top-level `C:\work` contains no `RiftAssetDumper` or `RiftScan` checkout; `C:\rift modding` has no `Riftscan` folder. Targeted NIF/CDR/HKX/parser searches in RR2 source, RRE tools and old RiftReader scripts/reader found no usable local asset decoder. These are scoped negative observations, not proof that no other drives, archives or historical GitHub repositories contain tools.

## Candidate architecture (conditional)

1. A read-only **Asset/Scene Provider** decodes versioned terrain triangles and placed structures, retaining source archive/build hashes, scene ID, transforms and missing collision references. A parse success cannot self-certify semantics.
2. A distinct **Collision/Support Provider** resolves actual solid surfaces, agent-height/radius, clearance, ramps/steps, drops, and 3D line of sight. Return `unknown` for unmapped/missing collision layers.
3. Once a calibrated geometry fixture passes, benchmark **DotRecast Recast + Detour** for *ground-surface* multilayer routing, with finite query budgets and complete/partial/unreachable/unknown results. A Detour walkability ray is **not** true 3D spell LOS or capsule clearance; keep those services separate.
4. **Movement Feedback** retains the existing current-build freshness/identity gate and compares desired corridor with actual observed poses. It never treats an async path as valid after scene, target, geometry revision, session or cancellation changes.
5. A parallel **native client click-to-move behavior** lane can potentially reuse game movement semantics, but existing RRE NAV-002 has no qualified current gesture/point delivery/stop proof. It depends on the separately owned fresh RR2 feedback and supervised trials; it is not a substitute for geometry proof.

## G2 first experiment: one region, not an entire world

Input acquisition and all following steps remain **future work**; none are authorized merely by this document.

1. Identify a specific current RIFT *local* asset-manifest/archive pair, version and read-only access path. Obtain one matching `*_terrain_*_split.nif` chunk, its CDR tile/world placement record and one static-object NIF/HKX or collision-only reference; preferably one adjacent tile for a seam. Do not bulk-dump a game or run an unreviewed legacy viewer by default.
2. Record file sizes, SHA-256, manifest/content IDs, extraction method/parser revision and compression/format assumptions. Cap input bytes, decompression expansion, object recursion and triangle counts; reject nonfinite values, out-of-bounds indices, unresolved references and implausible transforms.
3. Prove real finite vertices/indices, tile and object transforms, LOD selection, chunk seams and what *collision-only* data is available. Keep visual triangles distinct from verified solid/walkable collision geometry; report missing HKX/collision paths explicitly.
4. Independently calibrate handedness, X/Y/Z mapping, units, origin/offset and height against at least **three noncollinear** known RIFT landmarks plus a separately observed vertical/elevation difference and a held-out noncoplanar check when available. Fix handedness/up-axis explicitly rather than inferring them from a planar fit. Include a bridge/underpass or stairs case with same X/Z but distinct elevation/layers.
5. Only after data and calibration pass, evaluate a tiny deterministic layered bake/path/clearance fixture with wall, narrow opening, cliff, ramp and disconnected goals. Separate actual 3D LOS/swept-body checks; never equate Detour planar ray with spell LOS. Include late results from a previous scene/session/geometry revision or cancelled request, and missing/stale player feedback: these must never become a clear route, arrival or physical-obstacle diagnosis. Record quality, performance and false-positive/unknown cases.
6. Close G2 as `qualified-for-specified-region`, `partially-supported`, `failed` or `blocked` with exact source identities and limits. After two materially different nondiscriminating probes, park the lead until a new source sample or behavior changes the decision.

**Exit gate:** one provenance-bound, current-build, calibrated geometry **and collision** fixture with reproducible queries (or a precise blocker). Without it, M9 remains parked; observation and carefully limited future open-terrain movement can progress independently after their own telemetry/action gates. No full-3D obstacle avoidance, reliable line of sight or global game pathfinding is claimed.

## Sources and bounded review

- Upstream terrain, world objects and asset layout: https://thegharstation.com/gharwiki/index.php/World_Data_Files and https://thegharstation.com/gharwiki/index.php/Assets_File_Format
- TelaraFly overview: https://www.thegharstation.com/telara-fly/ ; pinned source: https://github.com/imathrowback/telarafly/tree/41aacc0485ed2c90ceb1df7cdb7653853cc52d18
- Collision omission: https://github.com/imathrowback/telarafly/blob/41aacc0485ed2c90ceb1df7cdb7653853cc52d18/Assets/WorldStuff/CDRParse.cs ; generated Unity collider: https://github.com/imathrowback/telarafly/blob/41aacc0485ed2c90ceb1df7cdb7653853cc52d18/Assets/NIF/NIFLoader.cs
- Recast and DotRecast: https://recastnav.com/classdtNavMeshQuery.html and https://github.com/ikpil/DotRecast
- Developer's historical Reddit asset-extraction lead: https://www.reddit.com/r/Rift/comments/4gzw4g/extracting_game_model_files_and_textures/
- Local source hashes from bounded read-only checks: `record-navmesh.py` `aca6c67b1d1b5fcd651d487f489159adb4a8a5476540880a66805cb982f4f371`; `build-navmesh.py` `896b0896dfaa4f048285b92b3a490fe84675968c24bbd9d533ed186ed14bca32`; `rre/docs/agents/collision.md` `4fc0bed390d8e42b563359a796e26de17b5bd1795957dafae16a9374b7be4d18`.

Three independent read-only reasoning lanes inspected local asset inventory, old navigation/movement code and current upstream geometry algorithms/source. Their model/effort settings were requested per `AGENTS.md`; actual runtime metadata was not exposed. No build/test command was run for this documentation-only feasibility study and no live measurements were made.
