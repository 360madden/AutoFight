<#
AutoFight-owned SHA256 verification for a unique, inactive Lab1 staging directory.
No install, reader start, addon reload, VM lifecycle, or gameplay input.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$StageRoot,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$ReportPath
)

$ErrorActionPreference = 'Stop'
$allowed = @(
    'Rift.Automation.GuestReader.exe',
    'autofight-guest-reader.json',
    'addon\AutoFight\RiftAddon.toc',
    'addon\AutoFight\wire.lua',
    'addon\AutoFight\producer.lua',
    'addon\AutoFight\live.lua',
    'addon\AutoFight\guidance.lua',
    'addon\AutoFight\main.lua'
)
$receipt = @{
    schemaVersion = 1
    kind = 'autofight-staging-only'
    stageId = ''
    machine = [Environment]::MachineName
    verified = $false
    files = @()
    reason = 'verification-failed'
}
$reportSafe = $false
try {
    if ($StageRoot -cnotmatch '^C:\\Users\\mrkoo\\AppData\\Local\\AutoFight\\staging\\([a-f0-9]{32})$') {
        throw 'invalid-stage-root'
    }
    $receipt.stageId = $Matches[1]
    if ($receipt.machine -cne 'RIFT-LAB-1') { throw 'wrong-machine' }
    if ($ManifestPath -cne ($StageRoot + '\manifest.json') -or
        $ReportPath -cne ($StageRoot + '\receipt.json')) {
        throw 'invalid-metadata-paths'
    }
    # Only the exact, internal receipt path may be written. No junction ancestors.
    $directories = @(
        'C:\Users', 'C:\Users\mrkoo', 'C:\Users\mrkoo\AppData',
        'C:\Users\mrkoo\AppData\Local',
        'C:\Users\mrkoo\AppData\Local\AutoFight',
        'C:\Users\mrkoo\AppData\Local\AutoFight\staging',
        $StageRoot, ($StageRoot + '\addon'), ($StageRoot + '\addon\AutoFight')
    )
    foreach ($directory in $directories) {
        $folder = Get-Item -LiteralPath $directory -Force -ErrorAction Stop
        if (-not $folder.PSIsContainer -or
            ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'staging-directory-reparse-or-not-folder'
        }
    }
    $reportSafe = $true
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    $fileCount = @($manifest.files).Count
    if ($manifest.schemaVersion -ne 1 -or
        $manifest.stageId -cne $receipt.stageId -or
        $manifest.kind -cne 'autofight-staging-only' -or
        $fileCount -lt ($allowed.Count - 1) -or $fileCount -gt $allowed.Count) {
        throw 'invalid-manifest'
    }
    $seen = @{}
    $verified = @()
    foreach ($item in @($manifest.files)) {
        $relative = [string]$item.relativePath
        $key = $relative.ToLowerInvariant()
        if (-not ($allowed -ccontains $relative) -or $seen.ContainsKey($key)) {
            throw 'invalid-manifest-file'
        }
        $seen[$key] = $true
        if ([string]$item.sha256 -cnotmatch '^[a-f0-9]{64}$') {
            throw 'invalid-expected-hash'
        }
        $expectedSize = [long]$item.size
        if ($expectedSize -le 0 -or $expectedSize -gt 268435456) {
            throw 'invalid-expected-size'
        }
        $path = Join-Path $StageRoot $relative
        $file = Get-Item -LiteralPath $path -Force -ErrorAction Stop
        if ($file.PSIsContainer -or
            ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            $file.Length -ne $expectedSize) {
            throw 'file-size-or-type-mismatch'
        }
        $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -cne [string]$item.sha256) { throw 'file-sha256-mismatch' }
        $verified += @{ relativePath = $relative; sha256 = $actualHash; size = $expectedSize }
    }
    # Reader EXE plus all six Lua assets required; private JSON optional.
    if ($seen.Count -ne $fileCount -or
        (@($allowed | Where-Object {
            $_ -ne 'autofight-guest-reader.json' -and
            -not $seen.ContainsKey($_.ToLowerInvariant())
        }).Count -ne 0)) {
        throw 'incomplete-manifest'
    }
    $receipt.files = @($verified)
    $receipt.verified = $true
    $receipt.reason = 'verified-staged-only'
} catch {
    # Deliberately discard raw exception text and private data.
    $receipt.verified = $false
    $receipt.reason = 'verification-failed'
} finally {
    if ($reportSafe) {
        $json = $receipt | ConvertTo-Json -Depth 5 -Compress
        [IO.File]::WriteAllText(
            $ReportPath, $json, (New-Object System.Text.UTF8Encoding($false))
        )
    }
}
if (-not $receipt.verified) { exit 2 }
