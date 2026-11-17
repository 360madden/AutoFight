# AutoFight Storage Guardian

> **Development status (2026-11-16):** Reviewed Storage Guardian is integrated into local `main` at `9eca9fb` on AMD-DESKTOP. Independent post-fix code/architecture review **PASS**. No real AutoFight build files or evidence have been deleted.

The guardian monitors **on-disk growth**. This is separate from .NET managed-memory garbage collection.

## Commands (Windows PowerShell 5.1+)

From `C:\work\AutoFight`:

```powershell
# Audit all real files within the repository; read-only
.\scripts\Storage-Guardian.ps1

# Explicitly save a bounded, ignored local history entry
.\scripts\Storage-Guardian.ps1 -Mode Audit -RecordHistory

# Read-only list of potential old .NET build-file cleanup candidates
.\scripts\Storage-Guardian.ps1 -Mode Preview -MinimumAgeDays 14

# Optional automation-friendly audit: nonzero exit if warning or critical
.\scripts\Storage-Guardian.ps1 -Mode Audit -FailOnThreshold
```

The default audit covers **all real files under the project root** (including unexpected top-level folders), grouping size by top-level directory. It does not follow junctions or symbolic links. It reports `Warning` at **2,048 MiB** and `Critical` at **5,120 MiB**, with adjustable `-WarningMiB` and `-CriticalMiB` thresholds. These statuses print to the console; they are not automatic notifications or enforced quotas. **`-FailOnThreshold`** is an opt-in Audit-only switch that returns a failing process exit status at either threshold. It is intended for a future monitoring task; none is installed.

The scanner streams file entries rather than holding a full file inventory in memory, with independent defaults of **250,000 files** (`-ScanFileLimit`) and **100,000 directories** (`-ScanDirectoryLimit`). Exceeding either limit fails the audit without reporting incomplete totals, writing history, or starting cleanup. Limits are configurable for large checkouts.

`-RecordHistory` writes only `artifacts/local/storage-guardian/history.json`, a path excluded by Git. The JSON history is limited to **52 samples** and subsequent recordings show growth since the previous sample. Corrupt, oversized, or linked history is rejected without overwriting it. The default Audit mode does not write anything.

## Manual cleanup — destructive and not scheduled

**Stop active builds, tests, and AutoFight programs and inspect Preview first.** A deliberate, separately confirmed cleanup command is:

```powershell
.\scripts\Storage-Guardian.ps1 -Mode Clean -MinimumAgeDays 14 -ConfirmCleanup DELETE_STALE_BUILD_FILES
```

Clean is limited to files under immediate `bin` and `obj` directories of projects at `src/<project>/` or `tests/<project>/`, with a `.csproj` in that project folder. Each file must be older than the selected retention age. Preview and Clean require an exact Git repository root; **Git-tracked files are excluded**, including non-ASCII tracked paths under Windows PowerShell 5.1. Nested Git checkouts, submodules and worktrees (identified by either `.git` directories or `.git` files) are pruned from cleanup but remain counted in Audit. Directory junctions, symbolic links, changed files and out-of-scope paths are not deleted. The tool caps eligible files at 50,000, preflights all candidates and rechecks paths before each removal.

Nothing in `artifacts/`, `.git/`, ordinary source, documentation or private captures is a cleanup candidate. Removed outputs may need to be regenerated with a .NET build. No cleanup is completely safe while other programs concurrently edit those files.

## Regression tests

```powershell
.\tests\scripts\Test-Storage-Guardian.ps1
```

Tests create a separate, uniquely named **disposable** fixture under ignored `artifacts/local/storage-guardian-test-fixtures`. They test: audit/preview safety, rejected unauthorized cleanup, bounded streaming scan failures, exact checkout validation, file-age/project-directory eligibility, junction protection, source/Git/private-data preservation, actual deletion of **fixture files only**, Git-tracked file protection (including OEM-437 Unicode names), nested `.git` directory/file and submodule protection, history bounds/corruption checks, and warning/critical thresholds with optional failing exit status.

### Verified November 16 evidence

- **54/54 regression assertions PASSED, 0 skipped** using Windows PowerShell 5.1 after fixing two independent-review findings: nested Git checkout protection and UTF-8 Git name decoding under OEM-437. Fixture-only cleanup retained nested repositories, their metadata, worktree markers and Unicode tracked old output exactly.
- Real read-only Audit and Preview both PASSED: **930.33 MiB** for the whole repository at the time of measurement, including 1,293 stale generated-file candidates totaling **526.24 MiB**. **None of the real project's files was deleted.**
- First recorded local history sample: **930.23 MiB**, before the audit was broadened to include repository-root files.
- Full .NET 10 Release build passed with **0 warnings/errors**; **173 Core + 232 Windows + 39 Guest = 444/444 specs**, plus **7/7 WPF nonshown smoke checks**, all passed (no application source changed).
- PowerShell parser reported **0 syntax errors**; PSScriptAnalyzer was unavailable.
- Independent reviewer `/root/guardian_review` initially found two P1 blockers (nested Git repositories and Windows PowerShell 5.1 Unicode tracked names). Both were fixed. Independent **post-fix review PASS**, with **0 new P0/P1**: source SHA-256 `3a5f87fb…cbd70fb`, test SHA-256 `3da542c0…8c50f5b`, 54/54 regression assertions, seven extra in-memory boundary checks and read-only real Preview. Reviewer did not modify files; non-atomic filesystem races and concurrent Clean remain limitations. The changes were fast-forwarded into local `main` after review; [GitHub PR #1](https://github.com/360madden/AutoFight/pull/1) was the original review candidate. No real cleanup or weekly scheduling has been performed.

## Current limits and next stage

There is **no automatic deletion**, OS task registration, external notification, hard storage quota, or evidence-archive policy. The optional nonzero threshold exit code makes a future scheduler integration possible without relying on text parsing. Only the local opt-in history tracks changes across runs. The recommended later stage is **read-only weekly monitoring** after independent review. Artifact retention should receive separate per-folder rules; never delete raw research evidence on a simple age threshold.
