# AutoFight Storage Guardian

> **Development status (2026-11-16):** Working and tested on AMD-DESKTOP. Independent code/architecture review remains pending before integration into main. No real AutoFight build files or evidence have been deleted.

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
```

The default audit covers **all real files under the project root** (including unexpected top-level folders), grouping size by top-level directory. It does not follow junctions or symbolic links. It reports `Warning` at **2,048 MiB** and `Critical` at **5,120 MiB**, with adjustable `-WarningMiB` and `-CriticalMiB` thresholds. These statuses print to the console; they are not automatic notifications or enforced quotas.

`-RecordHistory` writes only `artifacts/local/storage-guardian/history.json`, a path excluded by Git. The JSON history is limited to **52 samples** and subsequent recordings show growth since the previous sample. Corrupt, oversized, or linked history is rejected without overwriting it. The default Audit mode does not write anything.

## Manual cleanup — destructive and not scheduled

**Stop active builds, tests, and AutoFight programs and inspect Preview first.** A deliberate, separately confirmed cleanup command is:

```powershell
.\scripts\Storage-Guardian.ps1 -Mode Clean -MinimumAgeDays 14 -ConfirmCleanup DELETE_STALE_BUILD_FILES
```

Clean is limited to files under immediate `bin` and `obj` directories of projects at `src/<project>/` or `tests/<project>/`, with a `.csproj` in that project folder. Each file must be older than the selected retention age. Preview and Clean require an exact Git repository root; **Git-tracked files are always excluded**, even when manually tracked inside `bin`/`obj`. Directory junctions, symbolic links, changed files and out-of-scope paths are not deleted. The tool caps eligible files at 50,000 and rechecks paths before removal.

Nothing in `artifacts/`, `.git/`, ordinary source, documentation or private captures is a cleanup candidate. Removed outputs may need to be regenerated with a .NET build. No cleanup is completely safe while other programs concurrently edit those files.

## Regression tests

```powershell
.\tests\scripts\Test-Storage-Guardian.ps1
```

Tests create a separate, uniquely named **disposable** fixture under ignored `artifacts/local/storage-guardian-test-fixtures`. They test: audit/preview safety, rejected unauthorized cleanup, exact file-age and project-directory eligibility, junction protection, source/Git/private-data preservation, actual deletion of **fixture files only**, Git-tracked file protection, history bounds/corruption checks, and warning thresholds.

### Verified November 16 evidence

- **27/27 regression assertions PASSED, 0 skipped** using Windows PowerShell.
- Real read-only Preview PASSED: **930.28 MiB** for the whole repository at the time of measurement, including 1,293 stale generated-file candidates totaling **526.24 MiB**. **None was deleted.**
- First recorded local history sample: **930.23 MiB**, before the audit was broadened to include repository-root files.
- Full .NET 10 Release build passed with **0 warnings/errors**; **173 Core + 232 Windows + 39 Guest = 444/444 specs**, plus **7/7 WPF nonshown smoke checks**, all passed (no application source changed).
- PowerShell parser reported **0 syntax errors**; PSScriptAnalyzer was unavailable.
- **Independent elevated code/architecture review is pending.** No GitHub integration or weekly scheduling has yet been performed.

## Current limits and next stage

There is **no automatic deletion**, OS task registration, external notification, hard storage quota, or evidence-archive policy. Only the local opt-in history tracks changes across runs. The recommended later stage is **read-only weekly monitoring** after independent review. Artifact retention should receive separate per-folder rules; never delete raw research evidence on a simple age threshold.
