# Synthetic replay contract v1

Date: 2026-10-30. Scope: M2 offline observer, not a live telemetry protocol.
Schema: `rift-replay/1`; origin must be `synthetic`.

The parser must validate the whole bounded document before exposing a scenario. Replays are data, never executable scripts. They cannot enable a game provider or input executor. The production Lua/native observation and event transport schemas remain separate future work.

## Bounds and syntax

- Strict UTF-8 JSON; no comments/trailing commas, duplicate keys or unknown properties.
- At most 65,536 input bytes, 256 steps and JSON depth 8.
- Required strings are nonempty, bounded and contain no control characters.
- Times are nonnegative integer milliseconds in one explicitly synthetic monotonic clock.
- Array order is the deterministic tie-breaker for equal step times.
- Coordinates must be finite doubles; legitimate zero and negative values are retained.
- The complete scenario begins with a reset. Reset epochs strictly increase.
- No partial scenario is accepted when a later step is malformed.
- Fixed diagnostic error codes should not echo untrusted file content.

See the executable tests for exact string/numeric/type bounds and failure codes; do not claim that file validity establishes game-data truth.

## Root and steps

Root fields: `schema`, `origin`, `name`, `steps`.

| Kind | Fields |
| --- | --- |
| reset | kind, atMs, sessionId, buildId, epoch |
| sample | kind, atMs, sessionId, buildId, epoch, sequence, status, value, acquiredStartMs, acquiredEndMs, receivedMs, sourceId; optional reason |
| gap | kind, atMs, reason |
| tick | kind, atMs |

A present sample uses a value object with exactly x/y/z. Missing, unsupported, invalid and error samples use null. Sample acquisition must satisfy start <= end <= received <= step time. This clock is host-side synthetic acquisition, not a claimed Lua producer clock or a physical process start time.

Sample session/epoch or sequence may deliberately be old to exercise reducer rejection. That is a semantically rejected observation within a structurally valid replay. Invalid JSON/types/time ordering are intake failures.

The engine accepts only its configured synthetic build ID. Build strings in a replay cannot register a real build or make native compatibility true. The opaque synthetic session identity is not live PID-reuse validation.

## State behavior

- Newer, equal-valued samples can be fresh.
- A newly received sample with old acquisition time remains stale. Acquisition age uses the start of the read interval conservatively, not just its newest end.
- Old session/epoch and duplicate/out-of-order sequence samples cannot replace current state.
- A malformed newer sample for the active identity invalidates current eligibility; last-known data are diagnostic only.
- Accepted missing/error/invalid states clear current eligibility.
- Gaps invalidate current observation until resynchronized. New valid capture must begin at or after the latest reset/gap; a delayed earlier frame cannot restore current validity.
- Reset clears current and last-known state. Late prior-epoch data cannot resurrect either.
- The engine admits a monotonic clock watermark on projection and rejected samples too. Time reversal throws; projection otherwise leaves observation/revision unchanged. Historical/backdated projections require a separate replay instance.
- Replay reset constructs a fresh engine and returns the virtual clock/index to zero.
- Acquisition/receipt age thresholds of 1,000 ms are synthetic test settings, not live-game acceptance budgets.
- Automation is unavailable in every state.

The [example fixture](../tests/fixtures/replay/synthetic-observer.json) covers fresh/equal/stale/error/gap/reset/late-old/recovery transitions. A test runner replay trace and the desktop both use the same Core engine/player.

## UI and test boundary

The WPF default is a built-in synthetic demonstration. Next/reset/load/open controls manipulate only the replay. The displayed clock advances with steps, not wall time. There is no hidden background observer.

A headless WPF smoke mode may construct and exercise actual XAML/presenter controls without showing a window. That checks initialization/control paths, not pixel-level appearance or real interaction. A normal visual check is a distinct acceptance step.
