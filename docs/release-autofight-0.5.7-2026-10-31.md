# AutoFight 0.5.7 — first standalone repository executable

Date: October 31, 2026
Repository: **C:\work\AutoFight**, independent Git repository.
Platform: Windows 11 x64, C#/.NET 10 WPF, self-contained single-file publish.

## Verified published output

- Exact path: C:\work\AutoFight\artifacts\local\autofight-0.5.7-win-x64-singleexe\Rift.Automation.Desktop.exe
- File count in output directory: **1** (no PDB/auxiliary files).
- Size: **139,984,037 bytes**.
- SHA-256: **87487b8a511585450af022128869611678f30ff36507a8f1b516966e6a24ce23**.
- Publish Workbridge job: af-migrate-selfcontained-publish-20261031-01; exit 0.
- SHA-256 was calculated by certutil from the exact published file, job af-migrate-exe-hash-20261031-01; exit 0.

Reproduce from Windows PowerShell in C:\work\AutoFight:

~~~powershell
dotnet publish .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false -o .\artifacts\local\autofight-0.5.7-win-x64-singleexe
~~~

## Actual executable checks

Executed the **published EXE directly** from C:\work (outside the repository) rather than relying on dotnet run:

- af-migrate-exe-help-20261031-01: --help exit 0; printed AutoFight version 0.5.7 and supported CLI controls.
- af-migrate-exe-smoke-20261031-01: --smoke-test exit 0; five nonshown WPF/VM/UI smoke categories passed.
- No VM controller, game input, native RIFT memory, installed addon, or research checkout was changed by these checks.

This is a locally verified developer executable, **not a signed installer, visible desktop UI acceptance, clean-machine validation, live RIFT movement proof, or finished combat automation**. It retains historical default local RR2 config/pin path strings but bundles no private config, authorization files or RR2 binary. The executable filename retains the Rift.Automation namespace-era name; the application title and help identify AutoFight.

The original 0.5.6 self-contained executable under C:\work\Rift API is a different historical release and is not the source of these bytes.
