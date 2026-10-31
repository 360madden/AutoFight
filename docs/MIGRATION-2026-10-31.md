# AutoFight repository separation — 2026-10-31

## Why

The previous assistant incorrectly routed standalone AutoFight development into an existing API-discovery repository. Joey confirmed that AutoFight belongs in its existing folder, **C:\work\AutoFight**, as an independent Git repository. This migration corrects ownership without deleting or rewriting the older source.

## Original source and provenance

- Original worktree: C:\work\Rift API, branch main.
- Source HEAD at selection: 122d8d0b77718d4c3f7597d84f83401f67a463be (self-contained 0.5.6 Windows release acceptance).
- Historical platform milestone commits in the source retain their original commit IDs. A fresh AutoFight Git history starts with the verified transplant and documents this source HEAD; it does not falsely claim the collector and AutoFight ever had independent prior histories.
- Original manual RiftApiCollector implementation, addons, Python collector tests and historical API documentation remain exclusively in Rift API.
- Existing source modifications were **copied**, never reset or committed in the original repository.

Five source work-in-progress files were byte-verified against the destination immediately after the selective copy:

| Source-relative path | SHA-256 at import |
| --- | --- |
| src/Directory.Build.props | 274d264e8d617ab723e2fe3b36e784e357678499d583cc04c842e78a0819bd32 |
| src/Rift.Automation.Desktop/App.xaml.cs | 2a9c742c073050ff7c908cec32bc3d534893643118b2527312a65e0706418c1e |
| src/Rift.Automation.Desktop/MainWindow.VmOperations.cs | ee027bfa7561ed91f754bd59bd818713cbe3213bed15378b716f9f11ce5e0561 |
| src/Rift.Automation.Desktop/MainWindow.VmQualification.cs | d6b4fd519231c215edbc657eaf6542317bffd8cd8c73107fcb9a2c982dfd2422 |
| src/Rift.Automation.Windows/VmSustainedEvidenceExport.cs | a6ea8b7512af1727553b15be880dd45331762055ff951bc492cf301f29748851 |

## What was imported

- Three C# application projects and two .NET test projects, solution/global SDK configuration and exact required replay/RAC unit fixtures.
- One self-contained RAC-TSV parser source file copied into AutoFight's Windows assembly; the prior linked compile path into Rift API tooling was removed.
- Eight automation-platform Markdown references, the original v2 prompt for audit, and historical 0.5.6 release receipt.
- No source-repository .git, Lua addon, unrelated Python API tests, private RR2 host configuration, generated build output, screenshots or runtime captures.

The selective copy Workbridge job was autofight-migrate-copy-20261031-01 (exit 0, eight sets copied, 65 files before new AutoFight-only files). The five WIP hashes were compared between original and destination before AutoFight fixes. The original v0.5.7 draft was incomplete: new Save controls were absent from XAML, and its path-picker test seam triggered warnings-as-errors. The destination adds those controls, places the parser locally and exercises the save/export path. Keep historical original bytes in Rift API until future cleanup is explicitly coordinated.

## Post-migration evidence

Jobs executed **from C:\work\AutoFight**:

- autofight-migrate-release-build-fixed-20261031-01: five-project Release build exit 0, zero warnings/errors.
- af-migration-core-specs-20261031-01: 149/149 Core, exit 0.
- af-migration-windows-specs-20261031-01: initial 115/115 Windows/VM, exit 0.
- af-migration-wpf-smoke-20261031-01: five WPF smoke groups passed, exit 0.
- af-m4p-specs-build-20261031-01: updated Release build exit 0, zero warnings/errors.
- af-m4p-export-specs-20261031-01: 119/119 Windows/VM including four new export tests, exit 0.

These checks do not prove a visible window, current live source, movement, casting or gameplay automation. The new AutoFight 0.5.7 self-contained executable was independently published (af-migrate-selfcontained-publish-20261031-01, exit 0), with direct published-EXE --help (af-migrate-exe-help-20261031-01, exit 0), nonshown smoke (af-migrate-exe-smoke-20261031-01, exit 0), and SHA-256 (af-migrate-exe-hash-20261031-01, exit 0). Exact bytes and limits are in release-autofight-0.5.7-2026-10-31.md. The old Rift API 0.5.6 package remains separate.

No original files were deleted. The original repository remains a historical source snapshot, not the authority for future AutoFight development.
