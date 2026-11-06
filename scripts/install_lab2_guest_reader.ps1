<# AutoFight-only Lab2 GuestReader promotion.
   Installs two hash-pinned reader files only. Never writes the existing RIFT addon,
   starts the reader, touches game UI, reloads the game, or changes VM lifecycle.
   Designed for a separately verified, inactive 32-hex staging directory. #>
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
$stageParent=Join-Path $localRoot 'staging'
$stageRoot=Join-Path $stageParent $StageId
$readerFinal=Join-Path $localRoot 'GuestReader'
$readerPending=Join-Path $localRoot ('.GuestReader-'+$StageId+'.pending')
$addonFinal='C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\AutoFight'
$manifestPath=Join-Path $stageRoot 'manifest.json'
$reportPath=Join-Path $stageRoot ('install-result-'+$InstallRunId+'.json')
$readerNames=@('Rift.Automation.GuestReader.exe','autofight-guest-reader.json')
$addonNames=@('RiftAddon.toc','wire.lua','producer.lua','live.lua','guidance.lua','main.lua')
$report=[ordered]@{
  schemaVersion=1
  kind='autofight-lab2-reader-install'
  stageId=$StageId
  installRunId=$InstallRunId
  machine=[Environment]::MachineName
  state='blocked-no-final-install'
  reason='preflight-failed'
  readerPromoted=$false
  addonUntouched=$true
  readerFinal=$readerFinal
  readerPending=$readerPending
  noReaderStart=$true
  noGameReload=$true
  noGameInput=$true
  noVmLifecycleChange=$true
}
$reportSafe=$false

function Test-CloudReparse([string]$Path) {
  $lines=@(& (Join-Path $env:SystemRoot 'System32\fsutil.exe') reparsepoint query $Path 2>$null)
  if($LASTEXITCODE -ne 0 -or $lines.Count -eq 0){return $false}
  $match=[regex]::Match([string]$lines[0],'0x([0-9a-fA-F]{8})')
  if(-not $match.Success){return $false}
  $tag=[Convert]::ToUInt32($match.Groups[1].Value,16)
  return (([uint64]$tag -band [uint64]4294905855) -eq [uint64]2415919130)
}
function Assert-Folder([string]$Path,[bool]$AllowCloud) {
  $item=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
  if(-not $item.PSIsContainer){throw 'not-directory'}
  if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){
    if(-not $AllowCloud -or [string]$item.LinkType -ne '' -or
        -not (Test-CloudReparse $item.FullName)){throw 'reparse-directory-rejected'}
  }
}
function Assert-File([string]$Path,[object]$Expected,[bool]$AllowCloudFile=$false) {
  $item=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
  if($item.PSIsContainer -or $item.Length -ne [long]$Expected.size){
    throw 'file-type-or-size-invalid'
  }
  if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){
    # Only the existing, exact-name OneDrive addon files can carry Cloud Files
    # tags. Staged/pending/final Reader EXE and private JSON must be ordinary files.
    if(-not $AllowCloudFile){throw 'reader-file-reparse-rejected'}
    if([string]$item.LinkType -ne '' -or -not (Test-CloudReparse $item.FullName)){
      throw 'addon-reparse-tag-untrusted'
    }
  }
  try {
    $hash=(Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
  } catch {
    if($AllowCloudFile){throw 'addon-content-unavailable'}
    throw 'reader-file-unavailable'
  }
  if($hash -cne [string]$Expected.sha256){
    if($AllowCloudFile){throw 'addon-hash-mismatch'}
    throw 'file-hash-mismatch'
  }
}
function Assert-Properties([object]$Value,[string[]]$Allowed) {
  if($null -eq $Value){throw 'manifest-object-null'}
  $actual=@($Value.PSObject.Properties.Name)
  if($actual.Count -ne $Allowed.Count){throw 'manifest-property-count'}
  foreach($name in $actual){if(-not ($Allowed -ccontains $name)){throw 'manifest-property-invalid'}}
}
function Assert-Game {
  $cim=Get-CimInstance Win32_Process -Filter ('ProcessId = '+$ExpectedGameProcessId) -ErrorAction Stop
  if($null -eq $cim -or @($cim).Count -ne 1 -or
     [string]$cim.Name -ine 'rift_x64.exe' -or
     [string]$cim.ExecutablePath -ine $ExpectedGameExecutablePath -or
     $null -eq $cim.CreationDate){throw 'game-process-invalid'}
  $process=Get-Process -Id $ExpectedGameProcessId -ErrorAction Stop
  if($process.Id -ne $ExpectedGameProcessId -or
     [string]$process.ProcessName -ine 'rift_x64' -or
     [long]$process.StartTime.ToUniversalTime().Ticks -ne $ExpectedGameProcessStartUtcTicks){
    throw 'game-process-restarted'
  }
  $actualHash=(Get-FileHash -LiteralPath $cim.ExecutablePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if($actualHash -cne $ExpectedGameExecutableSha256){throw 'game-image-changed'}
}
function Assert-NoReaders {
  $running=@(Get-CimInstance Win32_Process -Filter (
    "Name = 'Rift.Automation.GuestReader.exe' OR Name = 'RiftReader2.Live.exe' OR Name = 'RiftReader2.GuestReader.exe'"
  ) -ErrorAction Stop)
  if($running.Count -ne 0){throw 'other-reader-running'}
}
function Assert-Addon([object]$ExpectedFiles) {
  $found=@(Get-ChildItem -LiteralPath $addonFinal -Force -ErrorAction Stop)
  if($found.Count -ne $addonNames.Count){throw 'addon-directory-content-drift'}
  $seen=@{}
  foreach($entry in $found){
    if($entry.PSIsContainer -or -not ($addonNames -ccontains $entry.Name) -or
       $seen.ContainsKey($entry.Name)){throw 'addon-directory-content-drift'}
    if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){
      if([string]$entry.LinkType -ne '' -or -not (Test-CloudReparse $entry.FullName)){
        throw 'addon-reparse-tag-untrusted'
      }
    }
    $seen[$entry.Name]=$true
  }
  if($seen.Count -ne $addonNames.Count){throw 'addon-directory-content-drift'}
  foreach($expected in $ExpectedFiles){
    Assert-File (Join-Path $addonFinal ([string]$expected.relativePath)) $expected $true
  }
}
function Copy-Verified([string]$Source,[string]$Destination,[object]$Expected) {
  # CreateNew is atomic: an unexpected raced file is never overwritten.
  $inputStream=$null
  $outputStream=$null
  try {
    $inputStream=[IO.File]::Open($Source,[IO.FileMode]::Open,
      [IO.FileAccess]::Read,[IO.FileShare]::Read)
    $outputStream=[IO.File]::Open($Destination,[IO.FileMode]::CreateNew,
      [IO.FileAccess]::Write,[IO.FileShare]::None)
    $inputStream.CopyTo($outputStream)
    $outputStream.Flush($true)
  } finally {
    if($null -ne $outputStream){$outputStream.Dispose()}
    if($null -ne $inputStream){$inputStream.Dispose()}
  }
  Assert-File $Destination $Expected
}
try {
  if($StageId -cnotmatch '^[a-f0-9]{32}$' -or
     $InstallRunId -cnotmatch '^[a-f0-9]{32}$' -or
     $ExpectedManifestSha256 -cnotmatch '^[a-f0-9]{64}$' -or
     $ExpectedGameProcessId -le 0 -or $ExpectedGameProcessStartUtcTicks -le 0 -or
     $ExpectedGameExecutablePath -cnotmatch '^[A-Za-z]:\\' -or
     $ExpectedGameExecutableSha256 -cnotmatch '^[a-f0-9]{64}$' -or
     $report.machine -cne 'RIFT-LAB-2'){throw 'stage-machine-or-game-pin-invalid'}
  foreach($path in @('C:\Users','C:\Users\mrkoo','C:\Users\mrkoo\AppData',
    'C:\Users\mrkoo\AppData\Local',$localRoot,$stageParent,$stageRoot)){
    Assert-Folder $path $false
  }
  if([Environment]::UserName -cne 'mrkoo' -or
     [Environment]::GetFolderPath('LocalApplicationData') -ine
     'C:\Users\mrkoo\AppData\Local'){throw 'guest-user-context-invalid'}
  if(Test-Path -LiteralPath $reportPath){throw 'existing-install-receipt'}
  $reportSafe=$true
  foreach($path in @('C:\Users\mrkoo\OneDrive',
     'C:\Users\mrkoo\OneDrive\Documents',
     'C:\Users\mrkoo\OneDrive\Documents\RIFT',
     'C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface',
     'C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns')){
    Assert-Folder $path $true
  }
  Assert-Folder $addonFinal $true
  if((Test-Path -LiteralPath $readerFinal) -or
     (Test-Path -LiteralPath $readerPending)){throw 'reader-target-already-exists'}
  Assert-NoReaders
  Assert-Game
  $manifestItem=Get-Item -LiteralPath $manifestPath -Force -ErrorAction Stop
  if($manifestItem.PSIsContainer -or ($manifestItem.Attributes -band
     [IO.FileAttributes]::ReparsePoint) -or
     $manifestItem.Length -lt 64 -or $manifestItem.Length -gt 16384){throw 'manifest-file-invalid'}
  if((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne
     $ExpectedManifestSha256){throw 'manifest-hash-changed'}
  $manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
  Assert-Properties $manifest @('schemaVersion','kind','stageId','files','addonFiles')
  if($manifest.schemaVersion -ne 1 -or
     $manifest.kind -cne 'autofight-lab2-reader-stage' -or
     $manifest.stageId -cne $StageId -or
     @($manifest.files).Count -ne 2 -or
     @($manifest.addonFiles).Count -ne 6){throw 'manifest-invalid'}
  $readerIndex=@{}
  foreach($entry in @($manifest.files)){
    Assert-Properties $entry @('relativePath','sha256','size')
    $name=[string]$entry.relativePath
    if(-not ($readerNames -ccontains $name) -or $readerIndex.ContainsKey($name) -or
       [string]$entry.sha256 -cnotmatch '^[a-f0-9]{64}$' -or
       [long]$entry.size -le 0 -or
       [long]$entry.size -gt $(if($name -ceq $readerNames[0]){268435456}else{65536})){
      throw 'reader-manifest-item-invalid'
    }
    $readerIndex[$name]=$entry
    Assert-File (Join-Path $stageRoot $name) $entry
  }
  if($readerIndex.Count -ne $readerNames.Count){throw 'reader-manifest-incomplete'}
  $addonIndex=@{}
  foreach($entry in @($manifest.addonFiles)){
    Assert-Properties $entry @('relativePath','sha256','size')
    $name=[string]$entry.relativePath
    if(-not ($addonNames -ccontains $name) -or $addonIndex.ContainsKey($name) -or
       [string]$entry.sha256 -cnotmatch '^[a-f0-9]{64}$' -or
       [long]$entry.size -le 0 -or [long]$entry.size -gt 1048576){
      throw 'addon-manifest-item-invalid'
    }
    $addonIndex[$name]=$entry
  }
  if($addonIndex.Count -ne $addonNames.Count){throw 'addon-manifest-incomplete'}
  Assert-Addon @($manifest.addonFiles)
  # Validate Lab2 delivery configuration in memory only; never log its token.
  $privateFile=Join-Path $stageRoot 'autofight-guest-reader.json'
  $config=Get-Content -LiteralPath $privateFile -Raw -Encoding UTF8 | ConvertFrom-Json
  if($config.schemaVersion -ne 1 -or [string]$config.processName -ine 'rift_x64' -or
     [string]$config.delivery.endpoint -cne 'http://192.168.56.1:48761/observation' -or
     $null -eq $config.delivery.token -or
     [string]$config.delivery.token -cnotmatch '^[!-~]{32,512}$'){
    throw 'lab2-private-delivery-config-invalid'
  }
  if((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne
     $ExpectedManifestSha256){throw 'manifest-hash-changed'}
  Assert-Game
  Assert-NoReaders
  Assert-Addon @($manifest.addonFiles)
  if((Test-Path -LiteralPath $readerFinal) -or
     (Test-Path -LiteralPath $readerPending)){throw 'reader-target-raced'}
  [void](New-Item -ItemType Directory -Path $readerPending -ErrorAction Stop)
  foreach($name in $readerNames){
    Copy-Verified (Join-Path $stageRoot $name) (Join-Path $readerPending $name) $readerIndex[$name]
  }
  Assert-Game
  Assert-NoReaders
  Assert-Addon @($manifest.addonFiles)
  if(Test-Path -LiteralPath $readerFinal){throw 'reader-target-raced'}
  [IO.Directory]::Move($readerPending,$readerFinal)
  $report.readerPromoted=$true
  foreach($name in $readerNames){
    Assert-File (Join-Path $readerFinal $name) $readerIndex[$name]
  }
  Assert-Game
  Assert-Addon @($manifest.addonFiles)
  $report.state='installed-not-running'
  $report.reason='verified-reader-installed-addon-untouched'
} catch {
  # Do not disclose raw exceptions, tokens, guest paths or manifest content.
  $report.state=if($report.readerPromoted){
    'partial-install-needs-review'
  }else{
    'blocked-no-final-install'
  }
  # Report only a small whitelisted reason code; never raw OS exceptions.
  $reason=[string]$_.Exception.Message
  $known=@('addon-reparse-tag-untrusted','addon-content-unavailable',
    'addon-hash-mismatch','addon-directory-content-drift')
  $report.reason=if($known -ccontains $reason){$reason}else{
    'install-failed-or-identity-changed'
  }
} finally {
  if($reportSafe){
    $json=$report | ConvertTo-Json -Depth 4 -Compress
    $bytes=(New-Object System.Text.UTF8Encoding($false)).GetBytes($json)
    if($bytes.Length -gt 16384){throw 'receipt-too-large'}
    $stream=[IO.File]::Open($reportPath,[IO.FileMode]::CreateNew,
      [IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
  }
}
if($report.state -ne 'installed-not-running'){exit 2}
