# AutoFight YEA

AutoFight is a standalone Windows 11 C#/.NET 10 application for RIFT observation and eventual combat/movement automation. **Its only application source repository is C:\work\AutoFight.**

AutoFight was separated from the unrelated RIFT API research repository on October 31, 2026. The earlier mistaken development location, C:\work\Rift API, remains preserved for provenance and for its independent RiftApiCollector project. Do not develop AutoFight there. RiftReader2 remains a separately owned reference and optional integration provider, not a compiled or bundled dependency.

## Current capabilities — development version 0.5.7

- Windows WPF application, offline synthetic replay and analysis.
- Read-only host RIFT process inventory and manual RAC-TSV/1 unit snapshot import.
- Existing VMware Lab 1/Lab 2 controller status and strict source-bound telemetry diagnostics.
- An eight-minute, one-lab source-pinned telemetry qualification in CLI and WPF; WPF can save a bounded redacted historical JSON result.
- One *explicitly supervised* legacy input-pulse interface that requires a previously authorized RR2 controller grant/arm and independently checked lab/source binding.
- AutoFight-owned guest reader source port (RIFT process identity, addon wire decoder, full/fast memory scanning, authenticated delivery) and six-file AutoFight Lua addon, independently compilable with no RR2 assemblies. The authenticated in-process host telemetry ingest/listener is available through a read-only `--owned-telemetry` CLI watch and a separate WPF receiver panel. The bounded synthetic key executor is not a deployed guest controller.

**Not yet demonstrated:** fresh current live eight-minute telemetry in AutoFight, an actual game input followed by proven position response, native actor/target memory reading, automatic combat/spellcasting, measured movement, obstacle-aware navigation or multiclient combat.

The application must not be described as a finished autofighting bot. Controller acceptance, telemetry freshness and actual game response are different facts.

**Standalone-runtime boundary:** The WPF EXE and guest-reader EXE each build self-contained, and the AutoFight addon assets are bundled with the guest publisher. The new host receiver is available in an independent CLI and dedicated WPF read-only panel; the existing legacy telemetry/controller UI still uses the RR2 bridge. Guest deployment, live config/binding, and authorized lab testing remain outstanding. The next milestone proves fresh XYZ and independent movement feedback with no RR2 runtime service. Reference repositories remain read-only scaffolding.

## Build and run

Use **Windows PowerShell**:

~~~powershell
Set-Location 'C:\work\AutoFight'
dotnet build .\Rift.Automation.slnx -c Release
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release
~~~

The root global.json pins the .NET SDK 10.0.302 feature band (latest patch allowed). The WPF executable targets Windows x64 and .NET 10. Existing default UI paths into C:\work\riftreader2 are *optional local development suggestions*, not bundled private configs. Verify and reselect current source pins before using live diagnostics.

### Tests

~~~powershell
dotnet run --project .\tests\Rift.Automation.Specs\Rift.Automation.Specs.csproj -c Release -- selftest
dotnet run --project .\tests\Rift.Automation.Windows.Specs\Rift.Automation.Windows.Specs.csproj -c Release
dotnet run --project .\tests\Rift.Automation.Guest.Specs\Rift.Automation.Guest.Specs.csproj -c Release
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --smoke-test
~~~

The smoke test constructs controls without proving the visible window on a real desktop. See docs/current-status.md for measured results.

### Self-contained Windows package

~~~powershell
dotnet publish .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false -o .\artifacts\local\autofight-0.5.7-win-x64-singleexe
~~~

To publish the optional AutoFight-owned guest reader plus six addon assets (no guest install or startup):

~~~powershell
dotnet publish .\src\Rift.Automation.Guest\Rift.Automation.Guest.csproj -c Release -p:PublishTrimmed=false
~~~

The guest executable requires an explicit `--live` invocation and a private `autofight-guest-reader.json` beside it; `--help` performs no game scan. Do not commit tokens or copy addons into a VM as a build side effect. See docs/M1-standalone-guest-port-2026-10-31.md.

### AutoFight-owned read-only telemetry diagnostic

After independently verifying the current guest process and reader binding, create a private host configuration with a filename ending in `.local.json` (ignored by Git). It must contain a `binding` object with `lab`, `guestIpv4`, `readerInstanceId`, `readerNonce`, `gameProcessId`, `gameProcessStartUtcTicks`, `gameExecutablePath`, and `gameExecutableSha256`; the root also requires `bindIpv4` (the host's actual private IPv4), `port` (1024–65535), and `deliveryToken` (the same private token configured on the guest).

The reader instance and nonce are freshly generated when the guest reader starts; do not guess them or reuse an old session's binding. The host and guest addresses must be private IPv4 addresses. The guest sends `POST /observation` using the configured token.

From `C:\work\AutoFight` in PowerShell:

~~~powershell
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --owned-telemetry .\artifacts\local\host-lab1.local.json 15
~~~

The CLI watches for 1–120 seconds, emits redacted JSON progress/status and XYZ only if fresh, and returns exit 0 only with at least two advancing current-position samples and no session/order change. The VMware tab's separate **AUTOFIGHT STANDALONE TELEMETRY** panel accepts the same private host configuration and has explicit Start/Stop, age, sequence, producer-progress (advance/repeat), and XYZ display; it starts inactive and clears XYZ on unavailable/stale data. Neither diagnostic is the 480-second sustained qualification, game input, or proof of movement. A missing/invalid private configuration or absent guest stream fails closed. Running the CLI never starts a guest reader, installs an addon, or issues input.

The verified local AutoFight 0.5.7 executable is at C:\work\AutoFight\artifacts\local\autofight-0.5.7-win-x64-singleexe\Rift.Automation.Desktop.exe (one file, 139,984,037 bytes; SHA-256 87487b8a511585450af022128869611678f30ff36507a8f1b516966e6a24ce23). Direct --help and --smoke-test checks passed. See docs/release-autofight-0.5.7-2026-10-31.md. This is not an installer or signed public release. The original 0.5.6 release under C:\work\Rift API is historical and must not be confused with a package built from AutoFight.

## Project navigation

- SESSION_HANDOFF.md — resume point, exact Git/source routing, verification, and next functional milestone

- docs/current-status.md — current factual implementation and next gate
- docs/M1-standalone-guest-port-2026-10-31.md — guest/source port provenance, offline evidence, remaining live gates
- docs/standalone-autonomous-directive-2026-10-31.md — current user-directed standalone runtime milestone and parallel development requirements
- docs/automation-platform-master-prompt-v3.md — earlier corrected design directive
- docs/automation-platform-architecture.md — design and contracts
- docs/automation-platform-roadmap.md — milestones
- docs/MIGRATION-2026-10-31.md — exact migration and source history
- docs/README.md — index and historical-document warnings
