# AutoFight repository instructions

## Project identity and boundaries

The authoritative AutoFight application repository is **C:\work\AutoFight**. It is an independent Git repository on main. Never route AutoFight source, tests, releases, or roadmap edits to **C:\work\Rift API** merely because that older project contains the collector and RIFT API research. Rift API and **C:\work\riftreader2** are separate reference projects; do not modify them without an explicit task for those repositories.

AutoFight is a C#/.NET 10 Windows WPF application. The existing single-file EXE is package-self-contained, but its current live VMware telemetry and input adapters still depend on separately running RR2 services. The final runtime must own and bundle any required guest reader, host receiver, and input controller; RR2, Rift API, RRE and Workbridge are development references, never required runtime services or source paths. Keep platform-neutral Core, Windows-specific readers/controllers, and WPF UI separate. Keep required parser/source code in this repository. Existing namespace/assembly names beginning Rift.Automation are historical internal identifiers, not a repository routing instruction.

Read SESSION_HANDOFF.md first, then README.md, docs/current-status.md, docs/automation-platform-architecture.md and docs/automation-platform-roadmap.md before substantive development. The latest user execution directive is docs/standalone-autonomous-directive-2026-10-31.md; docs/automation-platform-master-prompt-v3.md remains an earlier design directive where consistent. Older copied documents and old commit IDs are historical evidence from Rift API, not a current path or a second source of truth.

## Workflow

- Inspect Git status, relevant files and tests before changing anything. Preserve concurrent/uncommitted work. Source was imported with incomplete v0.5.7 evidence export code and repaired here; do not silently overwrite it.
- Use the connected Workbridge tools for the actual Windows files. The ChatGPT scratch filesystem is not a mirror of C:\work\AutoFight.
- Prefer small functional milestones: change, deterministic tests, independent review when available, documentation, then scoped Git commit. Keep practical features ahead of process overhead.
- Use maximum *useful* reasoning-agent concurrency where available, with exclusive ownership and an independent reviewer. Keep a lightweight prioritized queue, promptly give idle agents the next meaningful nonconflicting task, and reassign blocked lanes to tests or review; do not create busywork. Never equate the six Workbridge command slots with reasoning agents. Coordinator owns integration, documentation, and commits. Do not invoke cloud/guest Codex CLI as a workaround for usage limitations.
- Determine app version from src/Directory.Build.props or the actual build; do not assume a version from old handoffs.
- Program defensively: nullable C#, warnings as errors, bounded input/queues/resources, typed unavailable/error/uncertain states, monotonic timing, explicit session/lab identity, cancellation/deadlines, proper disposal, single-owner input, and no hidden OS/game side effects. Test wrong identity, stale/reordered telemetry, restart, timeouts, partial input, and cancellation. Avoid fabricated game feedback or stale-source promotion.
- Keep private configs, credentials, captured gameplay data and generated artifacts untracked. No blanket copy of artifacts/local, bin, obj, installed addons, or other repository .git directories.

## Build and acceptance

In Windows PowerShell, from C:\work\AutoFight:

~~~powershell
dotnet build .\Rift.Automation.slnx -c Release
dotnet run --project .\tests\Rift.Automation.Specs\Rift.Automation.Specs.csproj -c Release -- selftest
dotnet run --project .\tests\Rift.Automation.Windows.Specs\Rift.Automation.Windows.Specs.csproj -c Release
dotnet run --project .\tests\Rift.Automation.Guest.Specs\Rift.Automation.Guest.Specs.csproj -c Release
dotnet run --project .\src\Rift.Automation.Desktop\Rift.Automation.Desktop.csproj -c Release -- --smoke-test
~~~

These checks exercise offline/synthetic behavior. Actual VMware telemetry, movement, targeting and casting require separately recorded current-build evidence. No input or VM lifecycle changes are incidental to building, reading, or packaging this application.

## Current practical priority

Implement and independently verify AutoFight-owned guest telemetry, host receiver, and bounded controller capabilities, adapting proven RR2 code as needed without changing that repository. RR2-owned source pins may be temporary comparison fixtures, but do not satisfy standalone-runtime acceptance. Qualify fresh sustained XYZ in one existing VMware lab without RR2 runtime services; then demonstrate one specifically authorized bounded command and independently observed position feedback. Do not equate controller HTTP success with in-game response. Combat/target/navigation features follow an accepted closed-loop movement baseline.

The first AutoFight-owned guest reader and addon source port plus its exact remaining runtime gates are documented in docs/M1-standalone-guest-port-2026-10-31.md. It does not authorize installing an addon or starting a VM/game scan, and it does not yet replace legacy controller services in the WPF runtime.

The copied historical master prompt v2 incorrectly names Rift API as the target. Never follow that path choice; version 3 supersedes it.
