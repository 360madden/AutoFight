<# AutoFight-only Lab1 stage promotion. No reader launch, addon reload or game input. #>
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$StageId,
 [Parameter(Mandatory=$true)][string]$InstallRunId,
 [Parameter(Mandatory=$true)][string]$ExpectedManifestSha256,
 [Parameter(Mandatory=$true)][int]$ExpectedGameProcessId,
 [Parameter(Mandatory=$true)][long]$ExpectedGameProcessStartUtcTicks,
 [Parameter(Mandatory=$true)][string]$ExpectedGameExecutablePath,
 [Parameter(Mandatory=$true)][string]$ExpectedGameExecutableSha256
)
$ErrorActionPreference='Stop'
$localRoot='C:\Users\mrkoo\AppData\Local\AutoFight'
$stageParent=$localRoot+'\staging'
$interfaceRoot='C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface'
$addonRoot=$interfaceRoot+'\AddOns'
$stageRoot=$stageParent+'\'+$StageId
$readerFinal=$localRoot+'\GuestReader'
$addonFinal=$addonRoot+'\AutoFight'
$readerPending=$localRoot+'\.GuestReader-'+$StageId+'.pending'
$addonPending=$interfaceRoot+'\.AutoFight-'+$StageId+'.pending'
$reportPath=$stageRoot+'\install-result-'+$InstallRunId+'.json'
$required=@(
 'Rift.Automation.GuestReader.exe','autofight-guest-reader.json',
 'addon\AutoFight\RiftAddon.toc','addon\AutoFight\wire.lua',
 'addon\AutoFight\producer.lua','addon\AutoFight\live.lua',
 'addon\AutoFight\guidance.lua','addon\AutoFight\main.lua'
)
$report=@{
 schemaVersion=1;kind='autofight-lab1-install';stageId=$StageId
 installRunId=$InstallRunId
 machine=[Environment]::MachineName;state='blocked';reason='preflight-failed'
 readerPromoted=$false;addonPromoted=$false;readerFinal=$readerFinal
 addonFinal=$addonFinal;readerPending=$readerPending;addonPending=$addonPending
 noReaderStart=$true;noGameReload=$true;noGameInput=$true
}
$reportSafe=$false
# Adapted by AutoFight from RR2's read-only Cloud Files tag check; no import.
function Test-CloudReparse([string]$path) {
 $fsutil=Join-Path $env:SystemRoot 'System32\fsutil.exe'
 $lines=@(& $fsutil reparsepoint query $path 2>$null)
 if($LASTEXITCODE -ne 0 -or $lines.Count -eq 0){return $false}
 $match=[regex]::Match([string]$lines[0],'0x([0-9a-fA-F]{8})')
 if(-not $match.Success){return $false}
 $tag=[Convert]::ToUInt32($match.Groups[1].Value,16)
 # Cloud Files family 0x9000001A..0x9000F01A; never accept junction tags.
 return (([uint64]$tag -band [uint64]4294905855) -eq [uint64]2415919130)
}
function Assert-Folder([string]$path,[bool]$allowCloud) {
 $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
 if(-not $item.PSIsContainer){throw 'not-directory'}
 if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){
  if(-not $allowCloud -or [string]$item.LinkType -ne '' -or
     -not (Test-CloudReparse $item.FullName)){throw 'reparse-rejected'}
 }
}
function Assert-Game {
 if($ExpectedGameProcessId -le 0 -or $ExpectedGameProcessStartUtcTicks -le 0 -or
    $ExpectedGameExecutablePath -cnotmatch '^[A-Za-z]:\\' -or
    $ExpectedGameExecutableSha256 -cnotmatch '^[a-f0-9]{64}$'){
  throw 'invalid-game-pin'
 }
 $record=Get-CimInstance Win32_Process -Filter ('ProcessId = '+$ExpectedGameProcessId) -ErrorAction Stop
 if($null -eq $record -or @($record).Count -ne 1 -or
    [string]$record.Name -ine 'rift_x64.exe' -or
    [string]$record.ExecutablePath -ine $ExpectedGameExecutablePath -or
    $null -eq $record.CreationDate){throw 'wrong-game-process'}
 # The pin came from kernel GetProcessTimes (100 ns ticks). CIM Win32_Process
 # CreationDate has only microsecond precision, so cannot retain all ticks.
 # Get-Process.StartTime is backed by the exact kernel process creation time.
 $live=Get-Process -Id $ExpectedGameProcessId -ErrorAction Stop
 if($live.Id -ne $ExpectedGameProcessId -or
    [string]$live.ProcessName -ine 'rift_x64' -or
    [long]$live.StartTime.ToUniversalTime().Ticks -ne
       $ExpectedGameProcessStartUtcTicks){throw 'game-restarted'}
 $hash=(Get-FileHash -LiteralPath $record.ExecutablePath -Algorithm SHA256).Hash.ToLowerInvariant()
 if($hash -cne $ExpectedGameExecutableSha256){throw 'game-hash-changed'}
}
function Assert-File([string]$path,[object]$item) {
 $file=Get-Item -LiteralPath $path -Force -ErrorAction Stop
 if($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
    $file.Length -ne [long]$item.size){throw 'invalid-file'}
 $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
 if($hash -cne [string]$item.sha256){throw 'hash-mismatch'}
}
function Copy-Verified([string]$source,[string]$destination,[object]$item) {
 if(Test-Path -LiteralPath $destination){throw 'pending-file-collision'}
 Copy-Item -LiteralPath $source -Destination $destination -ErrorAction Stop
 Assert-File $destination $item
}
try {
 if($StageId -cnotmatch '^[a-f0-9]{32}$' -or
    $InstallRunId -cnotmatch '^[a-f0-9]{32}$' -or
    $ExpectedManifestSha256 -cnotmatch '^[a-f0-9]{64}$' -or
    $report.machine -cne 'RIFT-LAB-1'){
  throw 'stage-or-machine-invalid'
 }
 foreach($dir in @('C:\Users','C:\Users\mrkoo','C:\Users\mrkoo\AppData',
     'C:\Users\mrkoo\AppData\Local',$localRoot,$stageParent,$stageRoot)){
  Assert-Folder $dir $false
 }
 if(Test-Path -LiteralPath $reportPath){throw 'existing-install-receipt'}
 # Only write the result inside this exact pin-validated stage directory.
 $reportSafe=$true
 foreach($dir in @('C:\Users\mrkoo\OneDrive',
     'C:\Users\mrkoo\OneDrive\Documents',
     'C:\Users\mrkoo\OneDrive\Documents\RIFT',$interfaceRoot,$addonRoot)){
  Assert-Folder $dir $true
 }
 if((Get-ChildItem -LiteralPath $addonRoot -Force -ErrorAction Stop | Measure-Object).Count -lt 20){
  throw 'unrecognized-addon-root'
 }
 foreach($path in @($readerFinal,$addonFinal,$readerPending,$addonPending)){
  if(Test-Path -LiteralPath $path){throw 'install-target-already-exists'}
 }
 $runningReaders=@(Get-CimInstance Win32_Process -Filter (
  "Name = 'Rift.Automation.GuestReader.exe' OR Name = 'RiftReader2.Live.exe'"
 ) -ErrorAction Stop)
 if($runningReaders.Count -gt 0){throw 'other-reader-owner-running'}
 Assert-Game
 $manifestPath=$stageRoot+'\manifest.json'
 $manifestHash=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
 if($manifestHash -cne $ExpectedManifestSha256){throw 'stage-manifest-changed'}
 $manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
 if($manifest.schemaVersion -ne 1 -or $manifest.kind -cne 'autofight-staging-only' -or
    $manifest.stageId -cne $StageId -or @($manifest.files).Count -ne 8){
  throw 'manifest-invalid'
 }
 $index=@{}
 foreach($item in @($manifest.files)){
  $rel=[string]$item.relativePath
  $key=$rel.ToLowerInvariant()
  if(-not ($required -ccontains $rel) -or $index.ContainsKey($key) -or
     [string]$item.sha256 -cnotmatch '^[a-f0-9]{64}$' -or
     [long]$item.size -le 0 -or [long]$item.size -gt 268435456){
   throw 'invalid-manifest-item'
  }
  $index[$key]=$item
  Assert-File (Join-Path $stageRoot $rel) $item
 }
 if($index.Count -ne $required.Count){throw 'manifest-incomplete'}
 if((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne
    $ExpectedManifestSha256){throw 'stage-manifest-changed'}
 [void](New-Item -ItemType Directory -Path $readerPending -ErrorAction Stop)
 [void](New-Item -ItemType Directory -Path $addonPending -ErrorAction Stop)
 foreach($name in @('Rift.Automation.GuestReader.exe','autofight-guest-reader.json')){
  $item=$index[$name.ToLowerInvariant()]
  Copy-Verified (Join-Path $stageRoot $name) (Join-Path $readerPending $name) $item
 }
 foreach($name in @('RiftAddon.toc','wire.lua','producer.lua','live.lua','guidance.lua','main.lua')){
  $rel='addon\AutoFight\'+$name
  $item=$index[$rel.ToLowerInvariant()]
  Copy-Verified (Join-Path $stageRoot $rel) (Join-Path $addonPending $name) $item
 }
 Assert-Game
 if((Test-Path -LiteralPath $readerFinal) -or (Test-Path -LiteralPath $addonFinal)){
  throw 'install-target-raced'
 }
 # Directory.Move fails if a destination already exists: no merge/overwrite.
 [IO.Directory]::Move($readerPending,$readerFinal)
 $report.readerPromoted=$true
 foreach($name in @('Rift.Automation.GuestReader.exe','autofight-guest-reader.json')){
  Assert-File (Join-Path $readerFinal $name) $index[$name.ToLowerInvariant()]
 }
 Assert-Game
 if(Test-Path -LiteralPath $addonFinal){throw 'addon-target-raced'}
 [IO.Directory]::Move($addonPending,$addonFinal)
 $report.addonPromoted=$true
 foreach($name in @('RiftAddon.toc','wire.lua','producer.lua','live.lua','guidance.lua','main.lua')){
  $rel='addon\AutoFight\'+$name
  Assert-File (Join-Path $addonFinal $name) $index[$rel.ToLowerInvariant()]
 }
 Assert-Game
 $report.state='installed-not-running'
 $report.reason='verified-installed-no-reload'
} catch {
 # Raw exception strings may include private paths; never emit them.
 $report.state=if($report.readerPromoted -or $report.addonPromoted){
  'partial-install-needs-review'
 }else{'blocked-no-final-install'}
 $report.reason='install-failed-or-identity-changed'
} finally {
 if($reportSafe){
  $json=$report | ConvertTo-Json -Depth 5 -Compress
  $bytes=(New-Object System.Text.UTF8Encoding($false)).GetBytes($json)
  $stream=[IO.File]::Open($reportPath,[IO.FileMode]::CreateNew,
                          [IO.FileAccess]::Write,[IO.FileShare]::None)
  try{$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
 }
}
if($report.state -ne 'installed-not-running'){exit 2}
