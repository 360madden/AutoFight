# M4o — self-contained Windows x64 distribution receipt

Date: 2026-10-31 (America/New_York)
**Historical pre-migration release receipt.** These actual 0.5.6 bytes and job results came from the previous Rift API source repository; they are not a release from the new AutoFight Git repository. See [current status](current-status.md).

Repository at time of build: `C:\work\Rift API`
Source commit: `91aea58ce3a6e7b4dc27e7bb23121af06d5b6c40` (M4n)
App version: 0.5.6
Purpose: local runnable Windows delivery; no installer, addon or VM change.

## Reproduce the release

PowerShell, from `C:\work\Rift API`:

```powershell
dotnet publish .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false -o .\artifacts\local\platform-m4o-0.5.6-win-x64-singleexe
```

Publish to a **new, absent folder** when repeating this procedure. The receipt's exact destination was absent before this publish; it must not be treated as an overwrite target. The SDK is 10.0.302 per `global.json`, with latest patch allowed. No NuGet dependency was newly declared.

The final publish command job `rift-m4o-final-oneexe-publish-20261031-01` exited 0. It targeted Desktop only, Release, `win-x64`, self-contained, single-file, bundled native libraries, no trimming, and no debug symbols. The existing C# source tests and Release build had already passed under M4n; this step changed no tracked source.

## Final local artifact

- Path: `C:\work\Rift API\artifacts\local\platform-m4o-0.5.6-win-x64-singleexe\Rift.Automation.Desktop.exe`
- Exact directory inventory: **one EXE and no sidecars**.
- Size: **139,968,677 bytes**.
- SHA-256: `79cb7839013c8d397ec5dab05be94c1307560658be79b5068794739df06fa57a` from read-only Windows `certutil -hashfile` (`rift-m4o-final-exe-sha256-20261031-01`, exit 0).
- Generated package is ignored by Git (`artifacts/local/`); a Git commit records the receipt, **not** the binary, and is not an off-machine binary backup.
- This is a native Windows x64, self-contained .NET 10 WPF build; other architectures/OSs are not claimed. Bundled native components can extract to the runtime's local cache. No Authenticode signing or clean-machine install was verified.

## Actual published-executable checks

All checks called the **EXE directly**, with literal arguments and explicit `C:\work` cwd outside the repository, not `dotnet run`:

| Job | Expected and observed |
| --- | --- |
| `rift-m4o-final-exe-help-other-cwd-20261031-01` | Exit 0; help reports 0.5.6 and current qualification/preflight/bound-pulse syntax |
| `rift-m4o-final-exe-smoke-other-cwd-20261031-01` | Exit 0; all five nonshown UI smoke groups passed, including qualification Stop/close and exclusion |
| `rift-m4o-final-exe-manual-fixture-20261031-01` | Exit 0; saved synthetic `RAC-TSV/1` player fixture parsed with explicitly missing Y and `CanAutomate=false` |
| `rift-m4o-final-exe-missingpin-20261031-01` | Expected exit 2; missing source pin generated incomplete `initial_lab_mismatch`, no input and no evidence pass |
| `rift-m4o-final-exe-invalid-20261031-01` | Expected exit 2; invalid option rejected without starting UI |
| `rift-m4o-final-exe-reject-short-pulse-20261031-01` | Expected exit 2; insufficient pulse args rejected without controller contact |

The earlier successful first publish (`rift-m4o-selfcontained-publish-20261031-01`) retained three optional PDBs; it is **not** the final single-EXE deliverable and remains an ignored diagnostic artifact. No cleanup/overwrite was performed.

## Boundaries and next work

This establishes a launchable local executable on **Joey's existing Windows 11 machine**. It does not establish an installer, signing, operation on a clean computer without .NET, visible UI pixel/DPI/accessibility acceptance, native game-memory reading, actual 480-second live telemetry, or automation.

The GUI's default lab-config/source-pin **path strings** reflect this host; **contents, tokens, private receipts, host.json/lease/arm files and RR2 binaries were not copied into the release**. The output is suitable as a local artifact, **not a sanitized public distribution**. Startup and smoke do not invoke controller or game input. RR2 readers/source pins still require owner-reviewed renewal before live telemetry qualification.

The next useful standalone-app feature is an **explicitly saved, redacted qualification receipt**, so a future real observation can be reviewed without sharing private receiver files.
