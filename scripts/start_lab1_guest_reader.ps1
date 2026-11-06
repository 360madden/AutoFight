<# AutoFight-only Lab1 reader launch. No UI input, reload, or VM lifecycle change. #>
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$StageId,
 [Parameter(Mandatory=$true)][string]$StartRunId,
 [Parameter(Mandatory=$true)][string]$ExpectedManifestSha256,
 [Parameter(Mandatory=$true)][string]$ExpectedLauncherSha256,
 [Parameter(Mandatory=$true)][int]$ExpectedGameProcessId,
 [Parameter(Mandatory=$true)][long]$ExpectedGameProcessStartUtcTicks,
 [Parameter(Mandatory=$true)][string]$ExpectedGameExecutablePath,
 [Parameter(Mandatory=$true)][string]$ExpectedGameExecutableSha256
)
$ErrorActionPreference='Stop'
$localRoot='C:\Users\mrkoo\AppData\Local\AutoFight'
$stageRoot=$localRoot+'\staging\'+$StageId
$readerRoot=$localRoot+'\GuestReader'
$addonRoot='C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns'
$installedAddon=$addonRoot+'\AutoFight'
$readerExe=$readerRoot+'\Rift.Automation.GuestReader.exe'
$reportPath=$stageRoot+'\reader-start-'+$StartRunId+'.json'
$stdoutPath=$readerRoot+'\reader-'+$StartRunId+'.stdout.log'
$stderrPath=$readerRoot+'\reader-'+$StartRunId+'.stderr.log'
$required=@(
 'Rift.Automation.GuestReader.exe','autofight-guest-reader.json',
 'addon\AutoFight\RiftAddon.toc','addon\AutoFight\wire.lua',
 'addon\AutoFight\producer.lua','addon\AutoFight\live.lua',
 'addon\AutoFight\guidance.lua','addon\AutoFight\main.lua'
)
$report=[ordered]@{
 schemaVersion=1;kind='autofight-lab1-reader-start';stageId=$StageId
 startRunId=$StartRunId;machine=[Environment]::MachineName
 state='blocked-before-start';reason='preflight-failed'
 launchAttempted=$false;readerMayHaveStarted=$false
 readerProcessId=0;readerProcessStartUtcTicks=0
 gameInput=$false;gameReloaded=$false;vmLifecycleChanged=$false
}
$reportSafe=$false
function Test-CloudReparse([string]$path){
 $lines=@(& (Join-Path $env:SystemRoot 'System32\fsutil.exe') reparsepoint query $path 2>$null)
 if($LASTEXITCODE -ne 0 -or $lines.Count -eq 0){return $false}
 $match=[regex]::Match([string]$lines[0],'0x([0-9a-fA-F]{8})')
 if(-not $match.Success){return $false}
 $tag=[Convert]::ToUInt32($match.Groups[1].Value,16)
 return (([uint64]$tag -band [uint64]4294905855) -eq [uint64]2415919130)
}
function Assert-Folder([string]$path,[bool]$allowCloud){
 $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
 if(-not $item.PSIsContainer){throw 'not-directory'}
 if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){
  if(-not $allowCloud -or [string]$item.LinkType -ne '' -or
      -not (Test-CloudReparse $item.FullName)){throw 'reparse-rejected'}
 }
}
function Assert-File([string]$path,[object]$expected){
 $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
 if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
    $item.Length -ne [long]$expected.size){throw 'file-invalid'}
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne
     [string]$expected.sha256){throw 'file-hash-changed'}
}
function Assert-Game {
 $cim=Get-CimInstance Win32_Process -Filter ('ProcessId = '+$ExpectedGameProcessId) -ErrorAction Stop
 if($null -eq $cim -or @($cim).Count -ne 1 -or
    [string]$cim.Name -ine 'rift_x64.exe' -or
    [string]$cim.ExecutablePath -ine $ExpectedGameExecutablePath -or
    $null -eq $cim.CreationDate){throw 'wrong-game-process'}
 $process=Get-Process -Id $ExpectedGameProcessId -ErrorAction Stop
 if($process.Id -ne $ExpectedGameProcessId -or
    [string]$process.ProcessName -ine 'rift_x64' -or
    [long]$process.StartTime.ToUniversalTime().Ticks -ne
       $ExpectedGameProcessStartUtcTicks){throw 'game-identity-changed'}
 if((Get-FileHash -LiteralPath $cim.ExecutablePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne
      $ExpectedGameExecutableSha256){throw 'game-hash-changed'}
}
try {
 if($StageId -cnotmatch '^[a-f0-9]{32}$' -or
    $StartRunId -cnotmatch '^[a-f0-9]{32}$' -or
    $ExpectedManifestSha256 -cnotmatch '^[a-f0-9]{64}$' -or
    $ExpectedLauncherSha256 -cnotmatch '^[a-f0-9]{64}$' -or
    $ExpectedGameProcessId -le 0 -or $ExpectedGameProcessStartUtcTicks -le 0 -or
    $ExpectedGameExecutablePath -cnotmatch '^[A-Za-z]:\\' -or
    $ExpectedGameExecutableSha256 -cnotmatch '^[a-f0-9]{64}$' -or
    $report.machine -cne 'RIFT-LAB-1'){throw 'invalid-pin-or-machine'}
 foreach($path in @('C:\Users','C:\Users\mrkoo',
  'C:\Users\mrkoo\AppData','C:\Users\mrkoo\AppData\Local',
  $localRoot,($localRoot+'\staging'),$stageRoot,$readerRoot)){
  Assert-Folder $path $false
 }
 if([Environment]::UserName -cne 'mrkoo' -or
    [Environment]::GetFolderPath('LocalApplicationData') -ine
       'C:\Users\mrkoo\AppData\Local'){throw 'wrong-guest-user-context'}
 if(Test-Path -LiteralPath $reportPath){throw 'existing-start-receipt'}
 $launcher=Get-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction Stop
 if($launcher.PSIsContainer -or ($launcher.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
    (Get-FileHash -LiteralPath $launcher.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne
    $ExpectedLauncherSha256){throw 'launcher-hash-changed'}
 $reportSafe=$true
 foreach($path in @('C:\Users\mrkoo\OneDrive',
  'C:\Users\mrkoo\OneDrive\Documents','C:\Users\mrkoo\OneDrive\Documents\RIFT',
  'C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface',$addonRoot)){
  Assert-Folder $path $true
 }
 Assert-Folder $installedAddon $false
 foreach($path in @($stdoutPath,$stderrPath)){
  if(Test-Path -LiteralPath $path){throw 'existing-reader-log'}
 }
 Assert-Game
 $readers=@(Get-CimInstance Win32_Process -Filter (
  "Name = 'Rift.Automation.GuestReader.exe' OR Name = 'RiftReader2.Live.exe'"
 ) -ErrorAction Stop)
 if($readers.Count -ne 0){throw 'other-reader-already-running'}
 $manifestPath=$stageRoot+'\manifest.json'
 $manifestFile=Get-Item -LiteralPath $manifestPath -Force -ErrorAction Stop
 if($manifestFile.PSIsContainer -or ($manifestFile.Attributes -band
     [IO.FileAttributes]::ReparsePoint) -or $manifestFile.Length -gt 16384){
  throw 'stage-manifest-invalid'
 }
 if((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne
     $ExpectedManifestSha256){throw 'manifest-hash-changed'}
 $manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
 if($manifest.schemaVersion -ne 1 -or $manifest.kind -cne 'autofight-staging-only' -or
    $manifest.stageId -cne $StageId -or @($manifest.files).Count -ne 8){
  throw 'stage-manifest-invalid'
 }
 $indexed=@{}
 foreach($item in @($manifest.files)){
  $rel=[string]$item.relativePath
  $key=$rel.ToLowerInvariant()
  if(-not ($required -ccontains $rel) -or $indexed.ContainsKey($key) -or
     [string]$item.sha256 -cnotmatch '^[a-f0-9]{64}$' -or
     [long]$item.size -le 0 -or [long]$item.size -gt 268435456){
   throw 'manifest-file-invalid'
  }
  $indexed[$key]=$item
  $target=if($rel.StartsWith('addon\AutoFight\',[StringComparison]::Ordinal)){
    Join-Path $installedAddon $rel.Substring(16)
  }else{Join-Path $readerRoot $rel}
  Assert-File $target $item
 }
 if($indexed.Count -ne $required.Count){throw 'manifest-incomplete'}
 if((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne
     $ExpectedManifestSha256){throw 'manifest-hash-changed'}
 Assert-Game
 $readers=@(Get-CimInstance Win32_Process -Filter (
  "Name = 'Rift.Automation.GuestReader.exe' OR Name = 'RiftReader2.Live.exe'"
 ) -ErrorAction Stop)
 if($readers.Count -ne 0){throw 'reader-start-race'}
 # The observer is the only process-side effect; no RIFT input or restart.
 $report.launchAttempted=$true
 $report.readerMayHaveStarted=$true
 $child=Start-Process -FilePath $readerExe -ArgumentList '--live' -WorkingDirectory $readerRoot -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -WindowStyle Hidden -PassThru -ErrorAction Stop
 $report.readerProcessId=[int]$child.Id
 $child.Refresh()
 if($child.HasExited){throw 'reader-exited-during-launch'}
 $report.readerProcessStartUtcTicks=[long]$child.StartTime.ToUniversalTime().Ticks
 $readerCim=Get-CimInstance Win32_Process -Filter ('ProcessId = '+$child.Id) -ErrorAction Stop
 if($null -eq $readerCim -or [string]$readerCim.Name -ine 'Rift.Automation.GuestReader.exe' -or
    [string]$readerCim.ExecutablePath -ine $readerExe){throw 'wrong-reader-process'}
 Assert-Game
 $child.Refresh()
 if($child.HasExited){throw 'reader-exited-after-start'}
 $report.state='reader-launched-not-bound'
 $report.reason='reader-process-started-no-input'
} catch {
 # Raw exception text is intentionally discarded; never show token/config.
 $report.state=if($report.launchAttempted){'uncertain-reader-start'}else{'blocked-before-start'}
 $report.reason=if($report.launchAttempted){'start-or-postcheck-failed'}else{'preflight-failed'}
} finally {
 if($reportSafe){
  $bytes=(New-Object System.Text.UTF8Encoding($false)).GetBytes(
   ($report | ConvertTo-Json -Depth 5 -Compress))
  $stream=[IO.File]::Open($reportPath,[IO.FileMode]::CreateNew,
   [IO.FileAccess]::Write,[IO.FileShare]::None)
  try{$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
 }
}
if($report.state -ne 'reader-launched-not-bound'){exit 2}
