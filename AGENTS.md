# AutoFight repository instructions

## Project identity and boundaries

The authoritative AutoFight application repository is **C:\work\AutoFight**. It is an independent Git repository on main. Never route AutoFight source, tests, releases, or roadmap edits to **C:\work\Rift API** merely because that older project contains the collector and RIFT API research. Rift API and **C:\work\riftreader2** are separate reference projects; do not modify them without an explicit task for those repositories.

The current implementation is a standalone C#/.NET 10 Windows WPF application. Keep the platform-neutral Core, Windows-specific readers/controllers, and WPF UI separate. Keep required parser source in this repository. Existing namespace/assembly names beginning Rift.Automation are historical internal identifiers, not a repository routing instruction.

Read SESSION_HANDOFF.md first, then README.md, docs/current-status.md, docs/automation-platform-architecture.md and docs/automation-platform-roadmap.md before substantive development. The saved directive is docs/automation-platform-master-prompt-v3.md. Older copied documents and old commit IDs are historical evidence from Rift API, not a current path or a second source of truth.

## Workflow

- Inspect Git status, relevant files and tests before changing anything. Preserve concurrent/uncommitted work. Source was imported with incomplete v0.5.7 evidence export code and repaired here; do not silently overwrite it.
- Use the connected Workbridge tools for the actual Windows files. The ChatGPT scratch filesystem is not a mirror of C:\work\AutoFight.
- Prefer small functional milestones: change, deterministic tests, independent review when available, documentation, then scoped Git commit. Keep practical features ahead of process overhead.
- Use maximum *useful* reasoning-agent concurrency where available, with exclusive ownership and an independent reviewer; never equate the six Workbridge command slots with reasoning agents. Coordinator owns integration, documentation, and commits. Do not invoke cloud/guest Codex CLI as a workaround for usage limitations.
- Determine app version from src/Directory.Build.props or the actual build; do not assume a version from old handoffs.
- Use nullable C#, warnings as errors, bounded input, typed unavailable/error states, monotonic timing, explicit session/lab identity and no hidden OS/game side effects. Avoid fabricated game feedback or stale-source promotion.
- Keep private configs, credentials, captured gameplay data and generated artifacts untracked. No blanket copy of artifacts/local, bin, obj, installed addons, or other repository .git directories.

## Build and acceptance

In Windows PowerShell, from C:\work\AutoFight:

~~~powershell
dotnet build .\Rift.Automation.slnx -c Release
dotnet run --project .\tests\Rift.Automation.Specs\Rift.Automation.Specs.csproj -c Release -- selftest
dotnet run --project .\tests\Rift.Automation.Windows.Specs\Rift.Automation.Windows.Specs.csproj -c Release
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --smoke-test
~~~

These checks exercise offline/synthetic behavior. Actual VMware telemetry, movement, targeting and casting require separately recorded current-build evidence. No input or VM lifecycle changes are incidental to building, reading, or packaging this application.

## Current practical priority

Restore fresh, RR2-owner-reviewed source pins for existing RIFT-Lab-1 and RIFT-Lab-2; independently qualify eight-minute telemetry; then demonstrate one specifically authorized bounded command and observed position feedback on one chosen lab. Do not equate controller HTTP success with in-game response. Combat/target/navigation features follow an accepted closed-loop movement baseline.

The copied historical master prompt v2 incorrectly names Rift API as the target. Never follow that path choice; version 3 supersedes it.
