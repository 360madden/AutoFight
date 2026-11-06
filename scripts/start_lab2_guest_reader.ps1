<# AutoFight Lab2-only installed GuestReader start. No addon writes, RIFT input,
   reload, game restart or VM lifecycle operations. Never retry uncertain launches. #>
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$StageId,
 [Parameter(Mandatory=$true)][string]$StartRunId,
 [Parameter(Mandatory=$true)][string]$ExpectedManifestSha256,
 [Parameter(Mandatory=$true)][string]$ExpectedLauncherSha256,
 [Parameter(Mandatory=$true)][string]$ExpectedReaderExeSha256,
 [Parameter(Mandatory=$true)][string]$ExpectedConfigSha256,
 [Parameter(Mandatory=$true)][int]$ExpectedGameProcessId,
 [Parameter(Mandatory=$true)][long]$ExpectedGameProcessStartUtcTicks,
 [Parameter(Mandatory=$true)][string]$ExpectedGameExecutablePath,
 [Parameter(Mandatory=$true)][string]$ExpectedGameExecutableSha256
)
$ErrorActionPreference='Stop'
$root='C:\Users\mrkoo\AppData\Local\AutoFight'
$stageParent=Join-Path $root 'staging'
$stage=Join-Path $stageParent $StageId
$reader=Join-Path $root 'GuestReader'
$addon='C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\AutoFight'
$exeName='Rift.Automation.GuestReader.exe'
$configName='autofight-guest-reader.json'
$exe=Join-Path $reader $exeName
$manifestPath=Join-Path $stage 'manifest.json'
$receipt=Join-Path $stage ('reader-start-'+$StartRunId+'.json')
$stdout=Join-Path $reader ('reader-'+$StartRunId+'.stdout.log')
$stderr=Join-Path $reader ('reader-'+$StartRunId+'.stderr.log')
$addonNames=@('RiftAddon.toc','wire.lua','producer.lua','live.lua','guidance.lua','main.lua')
$readerNames=@($exeName,$configName)
$report=[ordered]@{
 schemaVersion=1;kind='autofight-lab2-reader-start';stageId=$StageId
 startRunId=$StartRunId;machine=[Environment]::MachineName
 state='blocked-before-start';reason='preflight-failed'
 launchAttempted=$false;readerMayHaveStarted=$false
 readerProcessId=0;readerProcessStartUtcTicks=0
 noAddonWrite=$true;noGameInput=$true;noGameReload=$true;noVmLifecycleChange=$true
}
$reportSafe=$false
function Test-CloudReparse([string]$path) {
 $lines=@(& (Join-Path $env:SystemRoot 'System32\fsutil.exe') reparsepoint query $path 2>$null)
 if($LASTEXITCODE -ne 0 -or $lines.Count -eq 0){return $false}
 $match=[regex]::Match([string]$lines[0],'0x([0-9a-fA-F]{8})')
 if(-not $match.Success){return $false}
 $tag=[Convert]::ToUInt32($match.Groups[1].Value,16)
 return (([uint64]$tag -band [uint64]4294905855) -eq [uint64]2415919130)
}
function Assert-Folder([string]$path,[bool]$allowCloud) {
 $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
 if(-not $item.PSIsContainer){throw 'not-directory'}
 if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){
  if(-not $allowCloud -or [string]$item.LinkType -ne '' -or
      -not (Test-CloudReparse $item.FullName)){throw 'reparse-folder-rejected'}
 }
}
function Assert-File([string]$path,[object]$item,[bool]$allowCloud=$false) {
 $file=Get-Item -LiteralPath $path -Force -ErrorAction Stop
 if($file.PSIsContainer -or $file.Length -ne [long]$item.size){throw 'file-size-or-type-invalid'}
 if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){
  if(-not $allowCloud -or [string]$file.LinkType -ne '' -or
      -not (Test-CloudReparse $file.FullName)){throw 'file-reparse-rejected'}
 }
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant() -cne
    [string]$item.sha256){throw 'file-hash-mismatch'}
}
function Assert-Properties([object]$value,[string[]]$allowed) {
 if($null -eq $value){throw 'object-null'}
 $names=@($value.PSObject.Properties.Name)
 if($names.Count -ne $allowed.Count){throw 'property-count-invalid'}
 foreach($name in $names){if(-not ($allowed -ccontains $name)){throw 'property-name-invalid'}}
}
function Assert-NoReaders {
 $running=@(Get-CimInstance Win32_Process -Filter (
   "Name = 'Rift.Automation.GuestReader.exe' OR Name = 'RiftReader2.Live.exe' OR Name = 'RiftReader2.GuestReader.exe'"
 ) -ErrorAction Stop)
 if($running.Count -ne 0){throw 'other-reader-running'}
}
function Assert-Game {
 $all=@(Get-CimInstance Win32_Process -Filter "Name = 'rift_x64.exe'" -ErrorAction Stop)
 if($all.Count -ne 1 -or [int]$all[0].ProcessId -ne $ExpectedGameProcessId){
  throw 'game-not-unique-or-pid-changed'
 }
 $cim=$all[0]
 if([string]$cim.ExecutablePath -ine $ExpectedGameExecutablePath -or $null -eq $cim.CreationDate){
  throw 'game-path-changed'
 }
 $game=Get-Process -Id $ExpectedGameProcessId -ErrorAction Stop
 if($game.ProcessName -ine 'rift_x64' -or
    [long]$game.StartTime.ToUniversalTime().Ticks -ne $ExpectedGameProcessStartUtcTicks){
  throw 'game-start-changed'
 }
 if((Get-FileHash -LiteralPath $cim.ExecutablePath -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant() -cne
    $ExpectedGameExecutableSha256){throw 'game-image-changed'}
}
function Assert-Addons([object[]]$entries) {
 $items=@(Get-ChildItem -LiteralPath $addon -Force -ErrorAction Stop)
 if($items.Count -ne 6){throw 'addon-file-count-drift'}
 $seen=@{}
 foreach($file in $items){
  if($file.PSIsContainer -or -not ($addonNames -ccontains $file.Name) -or
      $seen.ContainsKey($file.Name)){throw 'addon-name-drift'}
  $seen[$file.Name]=$true
 }
 foreach($entry in $entries){
  Assert-File (Join-Path $addon ([string]$entry.relativePath)) $entry $true
 }
}
try {
 if($StageId -cnotmatch '^[a-f0-9]{32}$' -or
    $StartRunId -cnotmatch '^[a-f0-9]{32}$' -or
    $ExpectedManifestSha256 -cnotmatch '^[a-f0-9]{64}$' -or
    $ExpectedLauncherSha256 -cnotmatch '^[a-f0-9]{64}$' -or
    $ExpectedReaderExeSha256 -cnotmatch '^[a-f0-9]{64}$' -or
    $ExpectedConfigSha256 -cnotmatch '^[a-f0-9]{64}$' -or
    $ExpectedGameProcessId -le 0 -or $ExpectedGameProcessStartUtcTicks -le 0 -or
    $ExpectedGameExecutablePath -cnotmatch '^[A-Za-z]:\\(?:[A-Za-z0-9_ ().+\-]+\\)+rift_x64\.exe$' -or
    $ExpectedGameExecutableSha256 -cnotmatch '^[a-f0-9]{64}$' -or
    $report.machine -cne 'RIFT-LAB-2'){throw 'start-arguments-invalid'}
 foreach($path in @('C:\Users','C:\Users\mrkoo','C:\Users\mrkoo\AppData',
  'C:\Users\mrkoo\AppData\Local',$root,$stageParent,$stage,$reader)){
  Assert-Folder $path $false
 }
 if([Environment]::UserName -cne 'mrkoo' -or
    [Environment]::GetFolderPath('LocalApplicationData') -ine
       'C:\Users\mrkoo\AppData\Local'){throw 'wrong-guest-user'}
 if(Test-Path -LiteralPath $receipt){throw 'receipt-exists'}
 $launcher=Get-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction Stop
 if($launcher.PSIsContainer -or ($launcher.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
   (Get-FileHash -LiteralPath $launcher.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne
    $ExpectedLauncherSha256){throw 'launcher-hash-changed'}
 $reportSafe=$true
 foreach($path in @('C:\Users\mrkoo\OneDrive',
  'C:\Users\mrkoo\OneDrive\Documents','C:\Users\mrkoo\OneDrive\Documents\RIFT',
  'C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface',
  'C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns',$addon)){
  Assert-Folder $path $true
 }
 foreach($path in @($stdout,$stderr)){
  if(Test-Path -LiteralPath $path){throw 'reader-log-exists'}
 }
 Assert-NoReaders
 Assert-Game
 $manifestItem=Get-Item -LiteralPath $manifestPath -Force -ErrorAction Stop
 if($manifestItem.PSIsContainer -or
    ($manifestItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
    $manifestItem.Length -lt 64 -or $manifestItem.Length -gt 16384){
  throw 'stage-manifest-invalid'
 }
 if((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne
    $ExpectedManifestSha256){throw 'stage-manifest-hash-drift'}
 $manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
 Assert-Properties $manifest @('schemaVersion','kind','stageId','files','addonFiles')
 if($manifest.schemaVersion -ne 1 -or $manifest.kind -cne 'autofight-lab2-reader-stage' -or
    $manifest.stageId -cne $StageId -or @($manifest.files).Count -ne 2 -or
    @($manifest.addonFiles).Count -ne 6){throw 'stage-manifest-schema-invalid'}
 $readers=@{}
 foreach($entry in @($manifest.files)){
  Assert-Properties $entry @('relativePath','sha256','size')
  $name=[string]$entry.relativePath
  if(-not ($readerNames -ccontains $name) -or $readers.ContainsKey($name) -or
     [string]$entry.sha256 -cnotmatch '^[a-f0-9]{64}$' -or
     [long]$entry.size -lt 1 -or
     [long]$entry.size -gt $(if($name -ceq $exeName){268435456}else{65536})){
   throw 'reader-manifest-invalid'
  }
  $readers[$name]=$entry
  Assert-File (Join-Path $stage $name) $entry
  Assert-File (Join-Path $reader $name) $entry
 }
 if($readers.Count -ne 2 -or
    [string]$readers[$exeName].sha256 -cne $ExpectedReaderExeSha256 -or
    [string]$readers[$configName].sha256 -cne $ExpectedConfigSha256){
  throw 'reader-hash-pin-mismatch'
 }
 $actualReader=@(Get-ChildItem -LiteralPath $reader -Force -ErrorAction Stop)
 if($actualReader.Count -ne 2 -or
   @($actualReader | Where-Object {$_.PSIsContainer -or -not ($readerNames -ccontains $_.Name)}).Count -ne 0){
  throw 'reader-install-directory-drift'
 }
 $addons=@{}
 foreach($entry in @($manifest.addonFiles)){
  Assert-Properties $entry @('relativePath','sha256','size')
  $name=[string]$entry.relativePath
  if(-not ($addonNames -ccontains $name) -or $addons.ContainsKey($name) -or
      [string]$entry.sha256 -cnotmatch '^[a-f0-9]{64}$' -or
      [long]$entry.size -lt 1 -or [long]$entry.size -gt 1048576){
   throw 'addon-manifest-invalid'
  }
  $addons[$name]=$entry
 }
 if($addons.Count -ne 6){throw 'addon-manifest-incomplete'}
 Assert-Addons @($manifest.addonFiles)
 $config=Get-Content -LiteralPath (Join-Path $reader $configName) -Raw -Encoding UTF8 | ConvertFrom-Json
 Assert-Properties $config @('schemaVersion','processName','durationSeconds',
  'pollIntervalMilliseconds','maxScanBytes','maxScanSeconds','outputPath',
  'freshnessTimeoutSeconds','delivery')
 Assert-Properties $config.delivery @('endpoint','token')
 if($config.schemaVersion -ne 1 -or $config.processName -cne 'rift_x64' -or
    [string]$config.delivery.endpoint -cne 'http://192.168.56.1:48761/observation' -or
    $null -eq $config.delivery.token -or
    [string]$config.delivery.token -cnotmatch '^[!-~]{32,512}$' -or
    $null -ne $config.outputPath){throw 'private-config-invalid'}
 # Recheck all immutable inputs immediately before one process launch.
 Assert-File (Join-Path $reader $exeName) $readers[$exeName]
 Assert-File (Join-Path $reader $configName) $readers[$configName]
 Assert-Game
 Assert-NoReaders
 Assert-Addons @($manifest.addonFiles)
 $report.launchAttempted=$true
 $report.readerMayHaveStarted=$true
 $child=Start-Process -FilePath $exe -ArgumentList '--live' -WorkingDirectory $reader -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru -ErrorAction Stop
 $report.readerProcessId=[int]$child.Id
 $child.Refresh()
 if($child.HasExited){throw 'reader-exited-immediately'}
 $report.readerProcessStartUtcTicks=[long]$child.StartTime.ToUniversalTime().Ticks
 $post=Get-CimInstance Win32_Process -Filter ('ProcessId = '+$child.Id) -ErrorAction Stop
 if($null -eq $post -or [string]$post.Name -ine $exeName -or
    [string]$post.ExecutablePath -ine $exe){throw 'reader-process-not-matched'}
 Assert-Game
 Assert-Addons @($manifest.addonFiles)
 $child.Refresh()
 if($child.HasExited){throw 'reader-exited-after-start'}
 $report.state='reader-launched-not-bound'
 $report.reason='reader-process-started-no-input'
} catch {
 # Raw exception details may include paths or secrets: never emit them.
 $report.state=if($report.launchAttempted){'uncertain-reader-start'}else{'blocked-before-start'}
 $report.reason=if($report.launchAttempted){'start-or-postcheck-failed'}else{'preflight-failed'}
} finally {
 if($reportSafe){
  $json=$report | ConvertTo-Json -Depth 4 -Compress
  $bytes=(New-Object System.Text.UTF8Encoding($false)).GetBytes($json)
  if($bytes.Length -gt 16384){throw 'receipt-too-large'}
  $stream=[IO.File]::Open($receipt,[IO.FileMode]::CreateNew,
    [IO.FileAccess]::Write,[IO.FileShare]::None)
  try{$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
 }
}
if($report.state -ne 'reader-launched-not-bound'){exit 2}
