# M8 physical RIFT live-test blockers — 2026-11-15

## Verified AMD-DESKTOP facts
- Physical rift_x64 PID 37928 was running during checks.
- Installed AutoFight addon was updated with seven staged files and SHA-256 verified; no in-game /reloadui confirmation.
- GuestReader M8 published with authenticated configuration copied from the November 5 VM workflow.
- Reader --live emitted a nonce and then `byte-budget-exceeded` and `delivery-timeout`; diagnostic run cancelled.
- Physical RIFT private bytes approximately 4.387 GiB, above the 4 GiB full-scan budget. Working set ~0.897 GiB is NOT an upper bound on readable private memory.
- Configured endpoint resolves to the host's VMware VMnet8 IPv4 interface; no listener on configured port 48761.
- The Windows desktop window was reported as 160x28, not foreground. No UI reload confirmed.

## Architecture decision
Do not silently increase scan limits or reuse VMnet8 as the default for same-host physical RIFT.
Introduce an explicitly opt-in, authenticated same-host transport that accepts loopback 127.0.0.1 only, with its own distinct config mode. Keep VM private-IP transport compatible for historical workflows. Do not use the test-only loopback seam in production.
Host and reader must agree on one nonce, token, process identity and bounded duration; terminate and invalidate on process restart, endpoint loss, or reader shutdown. Avoid logging tokens or memory addresses.

## First-scan optimization
Investigate allocation-aware and candidate-region anchored discovery, with a finite per-iteration byte/time budget. Incomplete scans must never be reported as complete; candidate hits in incomplete scans are diagnostic-only. Do not infer completeness from resident working-set size.
Only enable fast scoped reads after a verified full-scan anchor, or establish a separately reviewed qualification mechanism with explicit freshness and scope semantics.

## Acceptance
1. RIFT addon load and reader nonce binding visually confirmed on AMD-DESKTOP.
2. Local receiver accepts authenticated current-reader reports; rejects bad token/nonce and stale process.
3. Initial discovery stays within configured byte/time budgets and reports scan coverage truthfully.
4. Two successive advancing AFT2 frames show stable target ID and valid HP; target swap/clear invalidates.
5. Offline .NET and Lua regression suites pass; independent architecture review before M8 commit.

## Operational caution
The current M8 published reader must be refreshed after any source changes. No claim of live AFT2 capture until the physical game is reloaded, bound and reports are observed.
