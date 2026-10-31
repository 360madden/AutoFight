# Synthetic replay analysis v1

Date: 2026-10-30. Implemented M3 measurement preparation, not completed live-provider feasibility.

## Scope and command

`ReplayAnalysis.Analyze(ReplayScenario)` is a pure, bounded Core operation. It reuses the existing validated `ReplayPlayer`; it does not read clocks, processes, memory, network, or files. The CLI alone opens the explicitly selected replay file and uses the unchanged strict `rift-replay/1` reader before analysis.

From PowerShell in `C:\work\AutoFight`:

```powershell
dotnet run --project .\tests\Rift.Automation.Specs\Rift.Automation.Specs.csproj -c Release -- analyze .\tests\fixtures\replay\synthetic-observer.json
```

Success returns exit 0 and exactly one complete JSON report, without selftest output. Malformed input, missing/unreadable paths and invalid arguments return exit 2, diagnostics on stderr and no report on stdout. Full parsing and analysis precede serialization/output. The generic unavailable-file error is not a precise classification of every exception. Existing `selftest`, `replay <path>` and `--help` remain supported.

The report always declares:

- `Schema = rift-replay-analysis/1`
- `Origin = SyntheticReplay`
- `IsLiveQualification = false`
- `CanAutomate = false`
- `LatencyPopulation = all_structurally_valid_sample_attempts_including_rejected`
- `PercentileMethod = nearest_rank`

Names, build-like strings and source labels in the input cannot promote its evidence category. A healthy synthetic replay cannot register a supported live build or authorize automation.

## Exact coverage model

The horizon is **[first reset AtMs, final step AtMs)**. Pre-reset time is excluded even when the first reset occurs after zero. Each integer millisecond t owns the slot [t,t+1), with the state that the engine would project at t.

- Apply all same-time steps in source-array order before charging the following slots.
- The final step has no following duration. Its final status and sample counts still matter.
- Freshness is inclusive at the engine budget. With the current synthetic 1,000 ms limits, age 1,000 is fresh; age 1,001 is stale.
- Split an interval at automatic expiry even if no explicit tick records that boundary.
- Obtain both budgets from the replay player/engine rather than duplicating defaults.
- Compute from remaining allowance, not absolute timestamp + budget + 1; near-Int64.Max timestamps must not overflow.
- Last-known values never contribute usable slots. A newly accepted error/missing/invalid observation is not a successful present reading.
- Rejected old-session/epoch/sequence samples do not refresh or automatically invalidate the prior observation; use the actual reducer result and projection.
- Consecutive unavailable statuses form one outage, including across resets. A zero-duration intermediate present state does not break it.
- Usable duration plus unavailable duration, and the sum of all status durations, equal the exact horizon.
- A zero-length horizon has zero durations and a null usable percentage, not a fabricated 100% score.

These are **synthetic millisecond-slot measurements**, not continuous-time integration or measured wall-clock performance.

`TrailingUnavailableDurationMs` is the unavailable run still open at the terminal state. `TrailingUnavailableIsRightCensored` states that its later recovery is unobserved. If the final event recovers to Present, the tail closes at the endpoint and its open duration is zero. A final gap or first-stale endpoint can start a right-censored tail with zero measured slots. Never extrapolate beyond the final event.

## Counts and timings

Input-status counts, engine application results and projected-status durations are separate. An accepted Error counts as accepted application, not usable position. An old rejected Present counts as an input attempt, not accepted/fresh-present data. `FreshPresentOnArrivalCount` counts accepted Present samples whose immediate projected state is usable; a later same-time event can still invalidate their following coverage.

All structurally valid sample attempts, including semantic rejections and non-present statuses, populate these four distributions:

| Report field | Definition |
| --- | --- |
| AcquisitionSpan | AcquiredEndMs - AcquiredStartMs |
| ReceiptDelay | ReceivedMs - AcquiredEndMs |
| ApplicationQueueWait | SampleStep.AtMs - ReceivedMs |
| AcquisitionToApply | SampleStep.AtMs - AcquiredStartMs |

The first three sum to the fourth per sample. These are declared synthetic pipeline intervals, **not producer age or producer liveness**. Recapturing an old frame is not evidence that the producer generated a new sample.

Each summary contains Count, MinMs, P50Ms, P95Ms, P99Ms and MaxMs. Quantiles use sorted nearest rank: index ceil(percentile * count / 100) - 1. Empty populations have count 0 and null extrema/quantiles. A single sample repeats its value. No latency total is accumulated into an overflow-prone integer sum.

The input retains its existing 65,536-byte, 256-step and depth-8 bounds. Analysis uses bounded collections, ordinal result-code ordering and read-only output wrappers. It does not widen the replay schema, configure a live clock or alter observation semantics.

## Independent oracles and limits

For the built-in `DemoReplay` (not the different JSON example fixture):

| Item | Expected |
| --- | ---: |
| Horizon | [0,2221) |
| Steps / samples / accepted / rejected | 10 / 5 / 4 / 1 |
| Usable / unavailable slots | 2092 / 129 |
| Longest unavailable interval | 119 |
| Final state | Stale |
| Open tail | 0 slots, right-censored |
| Present / Error input attempts | 4 / 1 |
| Receipt-delay p50 / p95 / p99 | 0 / 10 / 10 ms |

Tests pin the complete manually derived report and separately compare 24 bounded generated scenarios against a deliberately slow per-millisecond projection oracle. Tests also cover delayed start, exact expiry, same-time order, reset/gap fences, rejected arrivals, non-present states, endpoint recovery, extreme timestamps, null metrics, percentiles, immutable output and deterministic serialization.

CLI regressions load copied repository fixtures from the test executable's directory, not an absolute research path. A fixture with a valid prefix and malformed final record proves atomic rejection without partial stdout. The unchanged parser continues to reject live/historical origin claims.

The 1,000 ms budgets belong to the synthetic harness. They are not acceptance limits for real combat, movement, camera or transport. Real-provider gates are in [provider feasibility](automation-platform-provider-feasibility.md); actual commands and results are in [status](automation-platform-status.md).
