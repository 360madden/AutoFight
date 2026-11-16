# AutoFight Storage Guardian. Audit is read-only; deletion requires an explicit, reviewed command.
[CmdletBinding()]
param(
    [ValidateSet('Audit','Preview','Clean')][string]$Mode = 'Audit',
    [ValidateRange(1,3650)][int]$MinimumAgeDays = 14,
    [string]$ConfirmCleanup = '',
    [string]$Root = '',
    [switch]$RecordHistory,
    [ValidateRange(1,1000000)][int]$WarningMiB = 2048,
    [ValidateRange(1,1000000)][int]$CriticalMiB = 5120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($CriticalMiB -le $WarningMiB) { throw 'CriticalMiB must exceed WarningMiB.' }
if ($RecordHistory -and $Mode -ne 'Audit') { throw 'History can be recorded only by Audit mode.' }
if ($Mode -eq 'Clean' -and $ConfirmCleanup -cne 'DELETE_STALE_BUILD_FILES') {
    throw 'Clean requires -ConfirmCleanup DELETE_STALE_BUILD_FILES. Run Preview first.'
}
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Split-Path -Parent $PSScriptRoot }
$rootItem = Get-Item -LiteralPath (Resolve-Path -LiteralPath $Root).Path -Force
if (-not $rootItem.PSIsContainer -or (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
    throw 'The repository root must be a real directory (not a link or junction).'
}
$rootPath = $rootItem.FullName.TrimEnd('\')
if (-not (Test-Path -LiteralPath (Join-Path $rootPath 'Rift.Automation.slnx') -PathType Leaf)) {
    throw 'Not an AutoFight repository root.'
}

function Test-Reparse([IO.FileSystemInfo]$Item) {
    return (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
}

# Walk explicit real directories only; never recurse through junctions or symlinks.
function Get-SafeFiles([string]$Directory) {
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { return }
    $stack = New-Object 'System.Collections.Generic.Stack[string]'
    $stack.Push($Directory)
    while ($stack.Count -gt 0) {
        $here = $stack.Pop()
        $currentDir = Get-Item -LiteralPath $here -Force -ErrorAction Stop
        if (Test-Reparse $currentDir) { continue }
        foreach ($entry in (Get-ChildItem -LiteralPath $here -Force -ErrorAction Stop)) {
            if (Test-Reparse $entry) { continue }
            if ($entry.PSIsContainer) { $stack.Push($entry.FullName) }
            elseif ($entry -is [IO.FileInfo]) { Write-Output $entry }
        }
    }
}

# Audit the entire repository root, including new/unexpected top-level directories.
# Root-level files are grouped as (root). Links/junctions are never followed.
$metrics = @{}
[long]$totalBytes = 0
foreach ($file in (Get-SafeFiles $rootPath)) {
    $relativePath = $file.FullName.Substring($rootPath.Length + 1)
    $separator = $relativePath.IndexOf('\')
    $group = if ($separator -lt 0) { '(root)' } else { $relativePath.Substring(0, $separator) }
    if (-not $metrics.ContainsKey($group)) {
        $metrics[$group] = [pscustomobject]@{ Directory = $group; Files = 0; Bytes = [long]0 }
    }
    $metrics[$group].Files++
    $metrics[$group].Bytes += [long]$file.Length
    $totalBytes += [long]$file.Length
}
$totals = @($metrics.Values | Sort-Object -Property Directory | ForEach-Object {
    [pscustomobject]@{
        Directory = $_.Directory
        Files = $_.Files
        MiB = [math]::Round($_.Bytes / 1MB, 2)
    }
})
$level = if ($totalBytes -ge ([long]$CriticalMiB * 1MB)) {
    'Critical'
} elseif ($totalBytes -ge ([long]$WarningMiB * 1MB)) {
    'Warning'
} else { 'Normal' }

Write-Output ('AutoFight storage: {0:N2} MiB; status={1}' -f ($totalBytes / 1MB), $level)
$totals | Select-Object Directory, Files, MiB | Format-Table -AutoSize | Out-String | Write-Output

if ($RecordHistory) {
    $historyPath = Join-Path $rootPath 'artifacts\local\storage-guardian\history.json'
    $dir = $rootPath
    foreach ($part in @('artifacts','local','storage-guardian')) {
        $dir = Join-Path $dir $part
        if (Test-Path -LiteralPath $dir) {
            $item = Get-Item -LiteralPath $dir -Force
            if (-not $item.PSIsContainer -or (Test-Reparse $item)) {
                throw 'The storage-history path contains a link, junction, or file.'
            }
        } else {
            $null = New-Item -ItemType Directory -Path $dir -ErrorAction Stop
        }
    }
    $previous = @()
    if (Test-Path -LiteralPath $historyPath -PathType Leaf) {
        $historyFile = Get-Item -LiteralPath $historyPath -Force
        if ((Test-Reparse $historyFile) -or $historyFile.Length -gt 262144) {
            throw 'Refusing an unsafe or oversized history file.'
        }
        $stored = Get-Content -LiteralPath $historyPath -Encoding UTF8 -Raw | ConvertFrom-Json
        if ($null -eq $stored -or $stored.schemaVersion -ne 1 -or $null -eq $stored.entries) {
            throw 'Invalid storage-history schema; existing data preserved.'
        }
        $previous = @($stored.entries)
        if ($previous.Count -gt 52) { throw 'Oversized storage history; existing data preserved.' }
    }
    if ($previous.Count -gt 0) {
        $last = $previous[$previous.Count - 1]
        if ($null -eq $last.bytes -or [long]$last.bytes -lt 0) {
            throw 'Invalid prior storage measurement; existing data preserved.'
        }
        $difference = $totalBytes - [long]$last.bytes
        Write-Output ('Change since last recorded audit: {0:+0.00;-0.00;0.00} MiB' -f ($difference / 1MB))
    }
    $entry = [pscustomobject]@{
        timestampUtc = [DateTime]::UtcNow.ToString('o')
        bytes = $totalBytes
        status = $level
    }
    $next = @($previous + @($entry))
    if ($next.Count -gt 52) { $next = @($next | Select-Object -Last 52) }
    $payload = @{ schemaVersion = 1; entries = $next } | ConvertTo-Json -Depth 5
    $unique = [guid]::NewGuid().ToString('N')
    $tempFile = Join-Path $dir ('history-' + $unique + '.tmp')
    $backupFile = Join-Path $dir ('history-' + $unique + '.bak')
    try {
        [IO.File]::WriteAllText($tempFile, $payload, (New-Object Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $historyPath) {
            # .NET Framework requires a real backup path for this atomic replacement.
            [IO.File]::Replace($tempFile, $historyPath, $backupFile)
        } else {
            [IO.File]::Move($tempFile, $historyPath)
        }
    } finally {
        if (Test-Path -LiteralPath $tempFile) { Remove-Item -LiteralPath $tempFile -Force }
        if (Test-Path -LiteralPath $backupFile) { Remove-Item -LiteralPath $backupFile -Force }
    }
    Write-Output ('Recorded one bounded history entry (max 52) in ignored local data.')
}
if ($Mode -eq 'Audit') { return }

# Never clean tracked files, even if an operator force-added one under bin/obj.
# A recognized Git checkout, rooted exactly at -Root, is required for preview/clean.
if ($null -eq (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Preview and Clean require Git to protect source-controlled files.'
}
$gitTop = @(& git -C $rootPath rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or $gitTop.Count -ne 1 -or
    -not [string]::Equals([IO.Path]::GetFullPath($gitTop[0]).TrimEnd('\'), $rootPath,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Preview and Clean require an exact repository-root Git checkout.'
}
$trackedOutput = @(& git -C $rootPath ls-files -z -- src tests)
if ($LASTEXITCODE -ne 0) { throw 'Git tracked-file inventory failed; no cleanup allowed.' }
$trackedSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($name in ([string]::Join('', [string[]]$trackedOutput)).Split([char]0)) {
    if (-not [string]::IsNullOrEmpty($name)) { $null = $trackedSet.Add($name.Replace('\','/')) }
}

$cutoffUtc = [DateTime]::UtcNow.AddDays(-$MinimumAgeDays)
$candidates = New-Object 'System.Collections.Generic.List[object]'

# Only actual top-level .NET projects' immediate bin/obj directories qualify.
foreach ($tree in @('src','tests')) {
    $treeRoot = Join-Path $rootPath $tree
    if (-not (Test-Path -LiteralPath $treeRoot -PathType Container)) { continue }
    if (Test-Reparse (Get-Item -LiteralPath $treeRoot -Force)) { continue }
    foreach ($project in (Get-ChildItem -LiteralPath $treeRoot -Directory -Force)) {
        if (Test-Reparse $project) { continue }
        $csprojs = @(Get-ChildItem -LiteralPath $project.FullName -File -Force -Filter '*.csproj')
        if ($csprojs.Count -eq 0) { continue }
        foreach ($bucket in @('bin','obj')) {
            $bucketPath = Join-Path $project.FullName $bucket
            if (-not (Test-Path -LiteralPath $bucketPath -PathType Container)) { continue }
            if (Test-Reparse (Get-Item -LiteralPath $bucketPath -Force)) { continue }
            foreach ($file in (Get-SafeFiles $bucketPath)) {
                if ($file.LastWriteTimeUtc -ge $cutoffUtc) { continue }
                $repoRelative = $file.FullName.Substring($rootPath.Length + 1).Replace('\','/')
                if ($trackedSet.Contains($repoRelative)) { continue }
                if ($candidates.Count -ge 50000) { throw 'Candidate limit exceeded; no files removed.' }
                $candidates.Add([pscustomobject]@{
                    Path = $file.FullName
                    Bytes = [long]$file.Length
                    LastWriteTimeUtcTicks = $file.LastWriteTimeUtc.Ticks
                })
            }
        }
    }
}

[long]$candidateBytes = 0
foreach ($candidate in $candidates) { $candidateBytes += $candidate.Bytes }
Write-Output ('Stale generated-file candidates: {0} files; {1:N2} MiB; older than {2} days' -f $candidates.Count, ($candidateBytes / 1MB), $MinimumAgeDays)
if ($Mode -eq 'Preview') {
    $candidates | Select-Object -First 15 -ExpandProperty Path
    return
}

[int]$removed = 0
[long]$removedBytes = 0
[int]$changedOrMissing = 0
foreach ($candidate in $candidates) {
    $path = $candidate.Path
    if (-not $path.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Candidate escaped the repository root.'
    }
    $relative = $path.Substring($rootPath.Length + 1)
    $parts = $relative.Split([char]'\')
    if ($parts.Length -lt 4 -or $parts[0] -notin @('src','tests') -or
        $parts[2] -notin @('bin','obj')) {
        throw 'Unexpected candidate path.'
    }
    # Check every path component again immediately before deleting a file.
    $walk = $rootPath
    foreach ($part in $parts) {
        $walk = Join-Path $walk $part
        $item = Get-Item -LiteralPath $walk -Force -ErrorAction SilentlyContinue
        if ($null -eq $item) { break }
        if (Test-Reparse $item) { throw 'Reparse point appeared in a candidate path; cleanup halted.' }
    }
    $fresh = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    if ($null -eq $fresh -or $fresh.PSIsContainer -or
        $fresh.LastWriteTimeUtc.Ticks -ne $candidate.LastWriteTimeUtcTicks -or
        $fresh.Length -ne $candidate.Bytes -or $fresh.LastWriteTimeUtc -ge $cutoffUtc) {
        $changedOrMissing++
        continue
    }
    Remove-Item -LiteralPath $path -Force -ErrorAction Stop
    $removed++
    $removedBytes += $candidate.Bytes
}
Write-Output ('Cleanup: removed {0} generated files ({1:N2} MiB); skipped {2} changed/missing files.' -f $removed, ($removedBytes / 1MB), $changedOrMissing)
Write-Output 'No source, private captures, artifact archives, or Git files were targeted.'
