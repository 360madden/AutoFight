# AutoFight M3 — Physical RIFT XYZ change-history readiness

**Date:** November 15, 2026 (America/New_York)  
**Machine / repo:** Verified `AMD-DESKTOP`, `C:\work\AutoFight`, `main`  
**Starting commit:** `bcc071a89dc035823c77a60c2ad3814c5bd60e8e`  
**Application:** C# / .NET 10 WPF, `0.5.7`  
**Scope:** Preserve observed native coordinate differences while the user moves RIFT manually; correct evidence-provenance and fault-counting UI issues. AutoFight itself remains **read-only**.

**M3 outcome:** **PASS** for a bounded, identity-labelled, retained desktop change history and for newly observed native XYZ values from the same character across independent real snapshots. **Still pending:** a user-driven movement event observed *during* one continuous session, independent producer freshness, visible manual WPF click-through, input-to-movement feedback and sustained native qualification. **No programmatic game input, focus, addon reload, or VMware operation occurred.**

## 1. Functional problem resolved

Prior to M3, WPF displayed only the **last** native coordinate comparison. After a user manually focused RIFT, moved and returned to AutoFight, the `Changed` indication could already have been overwritten by the next `Unchanged` reading (~1.1 seconds after the previous one). That made the next manual test hard to inspect.

The first/default **Physical RIFT – Live XYZ** tab now has a separate **Retained coordinate-change evidence** section:

- Sticky **total observed coordinate-value changes** for the current manually started session.
- Last observed change's **UTC sample timestamp** and 3D distance, distinct from live XYZ.
- Scrollable, selectable **last 20** `Changed` records with UTC timestamp, absolute ΔX/ΔY/ΔZ, 3D Euclidean distance and resulting XYZ. Select text and press Ctrl+C to copy manually. Older entries evict first; no unbounded memory growth or automatic disk writes.
- **Historical source label** pinned to PID + process-start UTC ticks **only after fresh Start identity validation**, updated with the first accepted player unit key; retains original source while a user merely refreshes or selects another client.
- Historical change evidence remains visible after **Stop**, unavailable reads, identity faults and stale live-position clearing. It is explicitly labelled historical and **never** presented as a current position or permission to automate.
- Only a new **successfully process-pinned Start** resets the session history. No automatic process attachment, game input or foreground change.
- Accurate **Identity faults** vs **Continuity faults** counters (non-identity gap/order/limit failures no longer count as identity switches).

Exclusive implementation files: `src/Rift.Automation.Desktop/MainWindow.xaml` and `src/Rift.Automation.Desktop/MainWindow.PhysicalXyz.cs`. Existing standalone native reader, pure comparison tracker and CLI watch contract remain unchanged.

## 2. Fresh physical XYZ evidence — value change across timed snapshots

Read-only AutoFight process listing at `2026-11-15T21:29:55Z` (`af-m3-xyz-readonly-clients-20261115-01`, exit 0) reverified:

| Source identity | Value |
| --- | --- |
| Computer | `AMD-DESKTOP` |
| RIFT executable | Physical `rift_x64.exe`, session 1, window `RIFT` |
| PID / process-start ticks | `37928` / `639303416167439797` |
| Executable SHA-256 | `1935A05C9DB6D468E34E0055014A4A99C1B5AFCEB1695884E24600F5C20A4BE8` |
| Player unit key | `239816685251645959` |

The earlier M2 live watch at ~4:19 PM EST had repeated identical XYZ. A new single native read `af-m3-xyz-readonly-single-20261115-01` at **4:30:07 PM EST**, exit 0, returned the same pinned player identity but different values in **106.1753 ms**:

| Axis | M2 at ~4:19 PM | M3 at 4:30:07 PM | Signed difference |
| --- | ---: | ---: | ---: |
| X | 7191.6084 | 7189.1750 | −2.4334 |
| Y | 873.9848 | 871.5005 | −2.4843 |
| Z | 3019.1848 | 3074.9150 | +55.7302 |

**3D coordinate-value difference:** `sqrt(2.4334² + 2.4843² + 55.7302²) ≈ 55.8386` RIFT coordinate units.

A second read-only four-second watch `af-m3-xyz-continuity-readonly-watch-20261115-01` at **4:31:34–4:31:38 PM EST** returned **4/4 Candidate**, `Baseline → Unchanged → Unchanged → Unchanged`, all at the **new** XYZ (7189.1750, 871.5005, 3074.9150); zero unavailable, identity faults, continuity faults or read errors; exit 0 and elapsed 4011 ms.

**Precise interpretation:** The native memory reader can observe a different XYZ value under an unchanged game/process/build/player identity. Because the two positions were approximately eleven minutes apart, their difference does **not** time-resolve the motion event or establish a cause, continuous tracking, an AutoFight action result, or passive game-producer freshness. AutoFight issued **no input**. The short later watch establishes repeated reacquisition at the newer value, not game producer progress.

The observed process reader uses read-only Windows process rights and memory queries and has no foreground/keyboard/Lua control. Its source acquisition itself does not depend on the game being focused; whether RIFT's internal position producer updates in background/minimized conditions remains unverified.

## 3. Code and test acceptance

| Check | Job | Result |
| --- | --- | --- |
| Final .NET 10 Release solution build | `af-m3-physical-xyz-final-build-20261115-01` | Exit 0, 0 warnings/errors |
| Core offline regressions | `af-m3-sticky-xyz-core-20261115-01` | **149/149** |
| Windows/native/VM regressions | `af-m3-sticky-xyz-windows-20261115-01` | **169/169** |
| GuestReader regressions | `af-m3-sticky-xyz-guest-20261115-01` | **15/15** |
| WPF nonshown smoke | `af-m3-physical-xyz-final-wpf-smoke-20261115-01` | Exit 0, **7/7** categories |
| Fresh native single read | `af-m3-xyz-readonly-single-20261115-01` | Exit 0, different pinned XYZ |
| Second four-second watch | `af-m3-xyz-continuity-readonly-watch-20261115-01` | Exit 0, 4/4 candidates, no faults |

WPF synthetic acceptance exercises the presentation formatter, **23 synthetic change records**, bounded retention to the newest **20**, explicit historical warnings, source-label formatting and reset. It does **not** exercise an actual visible desktop window, physical Start/Stop clicks, timer-driven stale expiry or actual game movement.

An independent read-only review found the bounded history, sticky state and accurate fault counters sound. Final source provenance label was added after a reviewer highlighted ambiguity between multiple RIFT clients; a second independent review of the final exact two-file source hashes **passed with no blockers**. Git diff whitespace check passed (only the repository's LF→CRLF advisory notices).

The read-only Workbridge desktop window inventory for `AutoFight` found **no open WPF app window** at the time of this milestone. A visual interaction test requires an explicit user launch; it has not been simulated or claimed.

## 4. Next user-assisted acceptance, without programmatic input

On **AMD-DESKTOP**, from PowerShell:

```powershell
Set-Location 'C:\work\AutoFight'
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release
```

1. In the first **Physical RIFT – Live XYZ** tab, click **Refresh physical RIFT clients** and select the current RIFT process. Click **Start read-only XYZ**. Confirm live coordinates and a few `Unchanged` results.
2. Switch manually to RIFT, move the player a short distance with **W** (safe location, normal game keyboard focus), release, and **remain at the new position for 3–5 seconds**. This allows the ~1-Hz native reader to sample the new value.
3. Return to AutoFight. Inspect **Changes count**, **Last observed difference**, and **Retained coordinate-change evidence**. The history remains after you click **Stop sampling** and can be copied with Ctrl+C. Do not confuse the latest `Unchanged` sample with no changes during the session.
4. For a strong acceptance report, capture the source label, time, before/after coordinates and any change records. Distinguish observed value differences from proof of specific input causation or independent producer freshness. If no change appears, investigate native cache/source update timing while preserving the read-only boundary.

**Remaining gate:** continuous before/during/after manual movement under the same player identity. This is **user-driven** only; it has not been performed by the development agent.
