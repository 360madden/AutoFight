# AutoFight YEA

AutoFight is a standalone Windows 11 C#/.NET 10 application for RIFT observation and eventual combat/movement automation. **Its only application source repository is C:\work\AutoFight.**

AutoFight was separated from the unrelated RIFT API research repository on October 31, 2026. The earlier mistaken development location, C:\work\Rift API, remains preserved for provenance and for its independent RiftApiCollector project. Do not develop AutoFight there. RiftReader2 remains a separately owned reference and optional integration provider, not a compiled or bundled dependency.

## Current capabilities — development version 0.5.7

- Windows WPF application, offline synthetic replay and analysis.
- Read-only host RIFT process inventory and manual RAC-TSV/1 unit snapshot import.
- Existing VMware Lab 1/Lab 2 controller status and strict source-bound telemetry diagnostics.
- An eight-minute, one-lab source-pinned telemetry qualification in CLI and WPF; WPF can save a bounded redacted historical JSON result.
- One *explicitly supervised* input-pulse interface that requires a previously authorized RR2 controller grant/arm and independently checked lab/source binding.

**Not yet demonstrated:** fresh current live eight-minute telemetry in AutoFight, an actual game input followed by proven position response, native actor/target memory reading, automatic combat/spellcasting, measured movement, obstacle-aware navigation or multiclient combat.

The application must not be described as a finished autofighting bot. Controller acceptance, telemetry freshness and actual game response are different facts.

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
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --smoke-test
~~~

The smoke test constructs controls without proving the visible window on a real desktop. See docs/current-status.md for measured results.

### Self-contained Windows package

~~~powershell
dotnet publish .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false -o .\artifacts\local\autofight-0.5.7-win-x64-singleexe
~~~

The verified local AutoFight 0.5.7 executable is at C:\work\AutoFight\artifacts\local\autofight-0.5.7-win-x64-singleexe\Rift.Automation.Desktop.exe (one file, 139,984,037 bytes; SHA-256 87487b8a511585450af022128869611678f30ff36507a8f1b516966e6a24ce23). Direct --help and --smoke-test checks passed. See docs/release-autofight-0.5.7-2026-10-31.md. This is not an installer or signed public release. The original 0.5.6 release under C:\work\Rift API is historical and must not be confused with a package built from AutoFight.

## Project navigation

- SESSION_HANDOFF.md — resume point, exact Git/source routing, verification, and next functional milestone

- docs/current-status.md — current factual implementation and next gate
- docs/automation-platform-master-prompt-v3.md — corrected development directive
- docs/automation-platform-architecture.md — design and contracts
- docs/automation-platform-roadmap.md — milestones
- docs/MIGRATION-2026-10-31.md — exact migration and source history
- docs/README.md — index and historical-document warnings
