# Disposable-fixture regression tests for AutoFight Storage Guardian (Windows PowerShell 5.1+).
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$guardian = Join-Path $repo 'scripts\Storage-Guardian.ps1'
$fixtureBase = Join-Path $repo 'artifacts\local\storage-guardian-test-fixtures'
$fixture = Join-Path $fixtureBase ('fixture-' + [guid]::NewGuid().ToString('N'))
[int]$passed = 0
[int]$skipped = 0

function Assert([bool]$Condition, [string]$Case) {
    if (-not $Condition) { throw "FAILED: $Case" }
    $script:passed++
    Write-Output "PASS: $Case"
}
function Put-File([string]$File, [string]$Value, [int]$AgeDays = 0) {
    $parent = Split-Path -Parent $File
    if (-not (Test-Path -LiteralPath $parent)) {
        $null = New-Item -ItemType Directory -Path $parent -Force
    }
    [IO.File]::WriteAllText($File, $Value)
    if ($AgeDays -gt 0) {
        (Get-Item -LiteralPath $File).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-$AgeDays)
    }
}
function Run-Guardian([hashtable]$Options) {
    $result = @(& $guardian -Root $fixture @Options)
    return ,$result
}
function Denied([hashtable]$Options, [string]$ExpectedMessage) {
    try {
        $null = Run-Guardian $Options
        return $false
    } catch {
        return ([string]$_.Exception.Message).Contains($ExpectedMessage)
    }
}

$null = New-Item -ItemType Directory -Path $fixture -Force
try {
    $null = & git -C $fixture init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Unable to initialize isolated fixture Git checkout.' }
    Put-File (Join-Path $fixture 'Rift.Automation.slnx') 'fixture'
    $project = Join-Path $fixture 'src\Demo'
    Put-File (Join-Path $project 'Demo.csproj') '<Project />'
    $oldBin = Join-Path $project 'bin\Release\net10.0\old.dll'
    $oldObj = Join-Path $project 'obj\Release\net10.0\old.g.cs'
    $freshBuild = Join-Path $project 'bin\Release\net10.0\recent.dll'
    $oldSource = Join-Path $project 'source.cs'
    $oldTrackedBuild = Join-Path $project 'obj\Release\net10.0\tracked-old.txt'
    $oldNonProject = Join-Path $fixture 'src\NotAProject\bin\old.txt'
    $protectedArtifact = Join-Path $fixture 'artifacts\local\capture.bin'
    $protectedGit = Join-Path $fixture '.git\objects\retained'
    Put-File $oldBin 'old bin' 45
    Put-File $oldObj 'old obj' 45
    Put-File $freshBuild 'new build'
    Put-File $oldSource 'old source' 45
    Put-File $oldTrackedBuild 'intentionally tracked old file' 45
    $null = & git -C $fixture add --force -- 'src/Demo/obj/Release/net10.0/tracked-old.txt'
    if ($LASTEXITCODE -ne 0) { throw 'Unable to stage tracked-file protection fixture.' }
    Put-File $oldNonProject 'not a .NET project' 45
    Put-File $protectedArtifact 'captured evidence' 45
    Put-File $protectedGit 'git object' 45

    $audit = Run-Guardian @{ Mode='Audit' }
    Assert ([bool]($audit -match 'status=Normal')) 'audit defaults to non-destructive normal status'
    $healthyThreshold = Run-Guardian @{ Mode='Audit'; FailOnThreshold=$true }
    Assert ([bool]($healthyThreshold -match 'status=Normal')) 'healthy strict-threshold audit succeeds'
    $preview = Run-Guardian @{ Mode='Preview' }
    Assert ([bool]($preview -match 'Stale generated-file candidates: 2 files')) 'preview recognizes only the two old generated files'
    Assert ((Test-Path -LiteralPath $oldBin) -and (Test-Path -LiteralPath $oldObj)) 'preview does not delete candidates'
    Assert (Denied @{ Mode='Clean' } 'Clean requires') 'clean without confirmation is denied'
    Assert (Denied @{ Mode='Clean'; ConfirmCleanup='wrong' } 'Clean requires') 'incorrect cleanup confirmation is denied'
    Assert (Denied @{ Mode='Preview'; RecordHistory=$true } 'History can be recorded only') 'preview cannot silently mutate history'
    Assert (Denied @{ Mode='Preview'; FailOnThreshold=$true } 'Threshold exit status is supported only by Audit mode') 'strict threshold exit is audit-only'
    Assert (Denied @{ Mode='Audit'; WarningMiB=50; CriticalMiB=10 } 'CriticalMiB must exceed') 'inverted thresholds are denied'
    Assert (Denied @{ Mode='Audit'; MinimumAgeDays=0 } 'MinimumAgeDays') 'invalid retention age is denied'
    Assert (Denied @{ Mode='Audit'; ScanFileLimit=0 } 'ScanFileLimit') 'invalid audit scan cap is denied'
    Assert (Denied @{ Mode='Audit'; ScanDirectoryLimit=0 } 'ScanDirectoryLimit') 'invalid directory scan cap is denied'
    Assert (Denied @{ Mode='Audit'; ScanDirectoryLimit=1 } 'Storage directory scan limit exceeded') 'over-limit directory audit fails safely'
    Assert (Denied @{ Mode='Clean'; ConfirmCleanup='DELETE_STALE_BUILD_FILES'; ScanDirectoryLimit=1 } 'Storage directory scan limit exceeded') 'over-limit directory clean fails before deletion'
    Assert (Denied @{ Mode='Audit'; ScanFileLimit=2 } 'Storage audit file limit exceeded') 'over-limit audit fails without reporting partial totals'
    Assert (Denied @{ Mode='Preview'; ScanFileLimit=2 } 'Storage audit file limit exceeded') 'over-limit preview fails before making a cleanup inventory'
    Assert (Denied @{ Mode='Clean'; ConfirmCleanup='DELETE_STALE_BUILD_FILES'; ScanFileLimit=2 } 'Storage audit file limit exceeded') 'over-limit clean fails before deleting any file'
    Assert (Denied @{ Mode='Audit'; RecordHistory=$true; ScanFileLimit=2 } 'Storage audit file limit exceeded') 'over-limit audit cannot record inaccurate history'
    Assert (-not (Test-Path -LiteralPath (Join-Path $fixture 'artifacts\local\storage-guardian\history.json'))) 'failed scan does not create history file'
    Assert ((Test-Path -LiteralPath $oldBin) -and (Test-Path -LiteralPath $oldObj)) 'denied operations preserve existing build files'
    $fixtureGit = Join-Path $fixture '.git'
    $fixtureGitOffline = Join-Path $fixture '.git-temporarily-hidden'
    Move-Item -LiteralPath $fixtureGit -Destination $fixtureGitOffline -ErrorAction Stop
    try {
        Assert (Denied @{ Mode='Preview' } 'exact repository-root Git checkout') 'preview requires exact Git root rather than parent Git checkout'
        Assert (Denied @{ Mode='Clean'; ConfirmCleanup='DELETE_STALE_BUILD_FILES' } 'exact repository-root Git checkout') 'clean requires exact Git root rather than parent Git checkout'
    } finally {
        Move-Item -LiteralPath $fixtureGitOffline -Destination $fixtureGit -ErrorAction Stop
    }

    $outside = Join-Path $fixture 'outside\special-old.txt'
    Put-File $outside 'outside retained' 45
    $auditExtra = Run-Guardian @{ Mode='Audit' }
    Assert ([bool]($auditExtra -match 'outside')) 'unexpected top-level directory is counted in full audit'
    $junction = Join-Path $project 'bin\linked-external'
    try {
        $null = New-Item -ItemType Junction -Path $junction -Target (Split-Path -Parent $outside) -ErrorAction Stop
        $junctionPreview = Run-Guardian @{ Mode='Preview' }
        Assert ([bool]($junctionPreview -match 'Stale generated-file candidates: 2 files')) 'junction target excluded from preview'
        Assert ((Test-Path -LiteralPath $outside)) 'external junction target untouched'
    } catch {
        if (Test-Path -LiteralPath $junction) { throw }
        $script:skipped++
        Write-Output 'SKIP: junction fixture unsupported by this PowerShell/Windows setup'
    }

    $clean = Run-Guardian @{ Mode='Clean'; ConfirmCleanup='DELETE_STALE_BUILD_FILES' }
    Assert ([bool]($clean -match 'Cleanup: removed 2 generated files')) 'confirmed cleanup deletes only stale fixture build files'
    Assert ((-not (Test-Path -LiteralPath $oldBin)) -and (-not (Test-Path -LiteralPath $oldObj))) 'stale generated fixture files are absent'
    Assert ((Test-Path -LiteralPath $freshBuild)) 'recent build file preserved'
    Assert ((Test-Path -LiteralPath $oldSource) -and (Test-Path -LiteralPath $oldNonProject)) 'old source and nonproject files preserved'
    Assert (Test-Path -LiteralPath $oldTrackedBuild) 'old Git-tracked build file preserved despite matching cleanup age and location'
    Assert ((Test-Path -LiteralPath $protectedArtifact) -and (Test-Path -LiteralPath $protectedGit)) 'artifact evidence and Git bytes preserved'
    Assert (Test-Path -LiteralPath $outside) 'junction target preserved after confirmed cleanup'

    $hist = Join-Path $fixture 'artifacts\local\storage-guardian\history.json'
    $recordOne = Run-Guardian @{ Mode='Audit'; RecordHistory=$true }
    Assert ((Test-Path -LiteralPath $hist) -and [bool]($recordOne -match 'Recorded one bounded history entry')) 'explicit audit-history recording creates one ignored record'
    $recordTwo = Run-Guardian @{ Mode='Audit'; RecordHistory=$true }
    $state = Get-Content -LiteralPath $hist -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert (@($state.entries).Count -eq 2) 'repeated audit appends exactly one history sample'
    Assert ([bool]($recordTwo -match 'Change since last recorded audit')) 'subsequent audit reports storage delta'

    $prior = @()
    for ($i = 0; $i -lt 52; $i++) {
        $prior += [pscustomobject]@{timestampUtc='2026-11-01T00:00:00Z';bytes=[long]($i + 1);status='Normal'}
    }
    $seed = @{schemaVersion=1;entries=$prior} | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText($hist,$seed)
    $null = Run-Guardian @{ Mode='Audit'; RecordHistory=$true }
    $bounded = Get-Content -LiteralPath $hist -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert (@($bounded.entries).Count -eq 52) 'history retains at most 52 samples'
    Assert ([long]$bounded.entries[0].bytes -eq 2) 'bounded history evicts oldest entry first'

    $corrupted = '{"schemaVersion":33,"entries":[]}'
    [IO.File]::WriteAllText($hist, $corrupted)
    Assert (Denied @{ Mode='Audit'; RecordHistory=$true } 'Invalid storage-history schema') 'invalid prior history is rejected'
    Assert (([IO.File]::ReadAllText($hist)) -ceq $corrupted) 'invalid existing history remains unchanged'

    $large = Join-Path $fixture 'artifacts\local\large-test.bin'
    [IO.File]::WriteAllBytes($large,(New-Object 'byte[]' (1536 * 1024)))
    $warning = Run-Guardian @{ Mode='Audit'; WarningMiB=1; CriticalMiB=2 }
    Assert ([bool]($warning -match 'status=Warning')) 'configurable warning threshold catches disk growth'
    Assert (Denied @{ Mode='Audit'; WarningMiB=1; CriticalMiB=2; FailOnThreshold=$true } 'Storage threshold exceeded: Warning') 'strict audit fails when warning limit exceeded'
    $extraLarge = Join-Path $fixture 'artifacts\local\another-large.bin'
    [IO.File]::WriteAllBytes($extraLarge,(New-Object 'byte[]' (768 * 1024)))
    $critical = Run-Guardian @{ Mode='Audit'; WarningMiB=1; CriticalMiB=2 }
    Assert ([bool]($critical -match 'status=Critical')) 'configurable critical threshold catches larger disk growth'
    Assert (Denied @{ Mode='Audit'; WarningMiB=1; CriticalMiB=2; FailOnThreshold=$true } 'Storage threshold exceeded: Critical') 'strict audit fails when critical limit exceeded'
    Assert (Denied @{ Mode='Audit'; WarningMiB=1; CriticalMiB=1 } 'CriticalMiB must exceed') 'equal threshold values are rejected'
} finally {
    # Delete only this test run's newly created GUID-named fixture; never project outputs.
    if ($fixture.StartsWith($fixtureBase + '\', [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $fixture) -match '^fixture-[0-9a-f]{32}$') {
        if (Test-Path -LiteralPath $fixture) {
            Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction Stop
        }
    } else { throw 'Unsafe disposable-fixture cleanup path.' }
}
Write-Output ('Storage Guardian regression result: {0} PASS, {1} SKIP' -f $passed, $skipped)
