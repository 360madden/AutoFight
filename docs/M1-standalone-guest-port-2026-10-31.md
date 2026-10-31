# Milestone 1 — AutoFight-owned VMware/RIFT telemetry port

Updated October 31, 2026. Workspace: C:\work\AutoFight, branch main.
Version: 0.5.7 in src/Directory.Build.props.

## Decision and provenance

RiftReader2 is the **implementation reference**. Joey explicitly authorized
adapting/copying it as scaffolding. Its checkout remains unmodified.
AutoFight must compile and eventually operate without RiftReader2 executables,
receivers, controller processes, source folders or services.

The first guest reader is a deliberately small source port of the proven
RiftReader2 live pipeline. The 16 copied/adapted C# sources are owned under
src/Rift.Automation.Guest:

- Domain: AddonObservation, LiveAddonTypes.
- Application: LiveAddonFreshnessTracker.
- Infrastructure: AddonFrameDecoder, LiveAddonEnvelopeDecoder,
  LiveAddonCandidateSelector, AddonDecodedReport, JsonFormat.
- Guest loop: Program, TargetDiscovery, WindowsMemoryScanner,
  EnvelopeCandidateAccumulator, FastObservationSession, LiveOutput,
  LiveConfiguration, LiveDeliveryClient.

AutoFight-owned namespaces and project/assembly names replace RR2 code
dependencies. Native-power and unrelated control/position-panel command paths
were removed from this reader-only executable. The explicit --live command
requires autofight-guest-reader.json beside the binary; --help performs no
process scan. Default observation window is 900 seconds. The config defaults
do not enable HTTP delivery until the operator supplies a private endpoint
and token.

AutoFight/addon/AutoFight contains the six adapted Lua addon files:
RiftAddon.toc, wire.lua, producer.lua, live.lua, guidance.lua and main.lua.
Its addon identity/globals and slash commands are AutoFight-owned:
**/afbind <32-hex-nonce>** and /afguide. Do not install it as a build step.
The guest reader prints the binding challenge; addon installation, UI reload,
live reader startup and binding require separate lab authorization.

The existing RR2A1/RR2L1 frame markers, payload shape, host HTTP
POST /observation endpoint and X-RiftReader-Token header are retained for
wire compatibility. The payload still contains the **riftReader2** JSON
observation block; that is a schema field, not a runtime library reference.

## AutoFight host-side components

- StandaloneTelemetryIngest: in-memory authenticated observation intake,
  strict peer and source identity pin, duplicate JSON rejection,
  outer-sequence high-water, addon epoch/sequence continuity and stale
  rereads. Its typed delivery result distinguishes accepted transport from
  fresh XYZ. An unavailable RIFT target is still a recorded delivery with
  no position or action permission.
- StandaloneTelemetryHttpListener: in-process, exact private IPv4 listener
  for one configured guest with bounded HTTP parsing, 128 KiB body maximum,
  three-second deadline, constant-time token verification, and no command
  endpoint. It exposes a test-only loopback seam and observes connection
  failures without bringing down its parent.
- StandaloneVmKeyPulseExecutor: no native input yet; injectable keyboard
  interface, w/a/s/d/q/e only, 20–250 ms, target checks during hold,
  cancellation and attempted key release on uncertain outcomes.
- WPF: Refresh and pinned Telemetry reads now participate in the same
  window-local VM operation exclusion and cancellation as other VM controls.

These are intentionally **not yet wired into a live AutoFight host receiver
session or a guest control agent**. The existing RR2 controller bridge remains
a legacy optional integration; it is not proof of a standalone action path.

## Verified offline evidence

| Check | Workbridge job | Result |
| --- | --- | --- |
| Seven-project Release solution build | af-m1-final-seven-project-build-20261031-01 | Exit 0, zero errors/warnings |
| Core synthetic regression | af-m1-final-core-verified-20261031-01 | 149/149 |
| Windows/VM and socket tests | af-m1-final-windows-verified-20261031-01 | 141/141 |
| Guest decoder/transport and real guest-to-host JSON roundtrip | af-m1-final-guest-verified-20261031-01 | 13/13 |
| WPF nonshown smoke | af-m1-final-desktop-smoke-verified-20261031-01 | All five categories |
| Guest --help, no scan | af-m1-guest-help-no-scan-20261031-01 | Exit 0 |
| Guest no args, unknown mode or --live without config | af-m1-guest-noargs-no-scan-20261031-01; af-m1-guest-unknown-no-scan-20261031-01; af-m1-guest-no-config-no-scan-20261031-01 | All intentionally exit 2 before any scan |
| Guest self-contained publish | af-m1-final-guest-standalone-publish-20261031-01 | Exit 0; one EXE and six Lua assets, hashes match source |

The tests are synthetic. A cross-assembly regression serializes a real
LiveReaderSnapshot using the AutoFight guest JsonFormat, ingests it through
StandaloneTelemetryIngest, accepts fresh XYZ, then proves target:null
advances high-water without granting position or automation authority.
All six published addon asset SHA-256 hashes were checked equal to their
AutoFight source hashes; no RR2 DLLs were present in the publish tree.

The guest decoder uses actual RR2A1/RR2L1 framing
with nonce, SHA256 identities, full-scan versus candidate selection,
chunk boundaries, region boundaries, malformed markers and private endpoint
checks. The host socket tests use only test-scoped 127.0.0.1 listeners, not
VMware guests. No game process was scanned by the tests.

## Explicitly not yet established

1. A running AutoFight-owned reader inside an actual VMware RIFT lab.
2. A live host XYZ reading delivered by that reader with no RR2 services.
3. Sustained 480-second progress, including the strict sub-4,500-ms
   maximum gap.
4. A reviewed capture, short arm, one native movement pulse and independent
   before/after XYZ evidence from the exact game process.
5. An integrated AutoFight host binding/configuration lifecycle, secure
   guest deployment and restart recovery for transport sequence high-water.
6. A compiled AutoFight-owned guest controller, host control transport,
   combat logic, navigation, or multi-client orchestration.

The reader’s in-memory transport high-water resets on host restart; a
durable single-owner sequence journal is a required follow-up before
production deployment. The candidate-region reader is based on RR2's
working acquisition path but the previous Lab 1 sustained freshness problem
is not considered solved merely by porting source.

## Next implementation slice

1. Compose the host ingest and HTTP listener behind an AutoFight-owned
   session manager with private configuration and a pinned, explicitly
   reviewed guest/game identity. Add a read-only UI/CLI XYZ display.
2. Package the guest EXE and the AutoFight addon assets together.
   Verify hashes after any separately authorized guest copy and ensure
   there is only one reader owner per VM.
3. With lab approval, start one guest reader, bind /afbind, and record live
   XYZ with no RiftReader2 service running. Diagnose full/fast scan gaps.
4. Complete the 480-second source-bound qualification. Then port RR2's
   existing guest control input/protocol safety path into AutoFight.
5. Only with explicit supervised input authority, issue one bounded key
   pulse and verify displacement from independently attributed fresh
   before/after observations. Never auto-retry uncertain delivery.

Do not claim observed movement or combat capability until step 5 passes.
