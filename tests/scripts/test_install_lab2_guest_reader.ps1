<# Offline, non-mutating structural contract for Lab2 GuestReader-only promotion.
   NEVER runs installer. No guest, addon or VM access. #>
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$path=Join-Path $root 'scripts\install_lab2_guest_reader.ps1'
if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw 'helper-missing'}
$raw=Get-Content -LiteralPath $path -Raw -Encoding UTF8
$errors=$null
$tokens=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
$failures=New-Object System.Collections.Generic.List[string]
$passed=0
function Check([string]$name,[bool]$valid){
  if($valid){$script:passed++; Write-Output ('PASS '+$name)}
  else{$script:failures.Add($name); Write-Output ('FAIL '+$name)}
}
Check 'bounded-source' ($raw.Length -gt 1000 -and $raw.Length -lt 40000)
Check 'powershell-parse' (@($errors).Count -eq 0)
$declared=@($ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })
$expected=@('StageId','InstallRunId','ExpectedManifestSha256','ExpectedGameProcessId',
  'ExpectedGameProcessStartUtcTicks','ExpectedGameExecutablePath','ExpectedGameExecutableSha256')
Check 'seven-pin-parameters' ($declared.Count -eq $expected.Count -and
  (@($declared | Where-Object { $expected -cnotcontains $_ }).Count -eq 0))
$commands=@($ast.FindAll({param($node) $node -is [System.Management.Automation.Language.CommandAst]},$true))
$called=@($commands | ForEach-Object { $_.GetCommandName() })
$forbidden=@('Start-Process','Stop-Process','Restart-Computer','Stop-Computer',
  'Remove-Item','Move-Item','Set-Acl','Invoke-Command','Invoke-WebRequest')
$forbiddenHits=@($called | Where-Object {$forbidden -icontains $_})
Check 'no-forbidden-process-or-os-command' ($forbiddenHits.Count -eq 0)
if($forbiddenHits.Count){Write-Output ('forbidden-hit-names '+($forbiddenHits -join ','))}
Check 'lab2-machine-fence' ($raw.Contains("'RIFT-LAB-2'") -and
  -not $raw.Contains("'RIFT-LAB-1'"))
Check 'private-stage-root' ($raw.Contains('AppData\Local\AutoFight') -and
  $raw.Contains("'staging'") -and $raw.Contains('$stageRoot'))
Check 'manifest-kind' ($raw.Contains("'autofight-lab2-reader-stage'"))
Check 'manifest-exact-five-properties' ($raw.Contains("'schemaVersion','kind','stageId','files','addonFiles'"))
Check 'two-reader-payloads' ($raw.Contains("'Rift.Automation.GuestReader.exe','autofight-guest-reader.json'"))
Check 'six-addon-payloads' ($raw.Contains("'RiftAddon.toc','wire.lua','producer.lua','live.lua','guidance.lua','main.lua'"))
Check 'manifest-eight-entries-by-role' ($raw.Contains('@($manifest.files).Count -ne 2') -and
  $raw.Contains('@($manifest.addonFiles).Count -ne 6'))
Check 'stage-manifest-sha-twice' ([regex]::Matches($raw,'manifest-hash-changed').Count -eq 2)
Check 'file-size-and-sha-check' ($raw.Contains('Get-FileHash -LiteralPath $Path -Algorithm SHA256') -and
  $raw.Contains('$item.Length -ne [long]$Expected.size'))
Check 'read-only-addon-validation' ($raw.Contains('Assert-Addon @($manifest.addonFiles)') -and
  $raw.Contains('addon-directory-content-drift'))
Check 'addon-cloud-reparse-exact-file-gate' ($raw.Contains('Assert-File (Join-Path $addonFinal ([string]$expected.relativePath)) $expected $true') -and
  $raw.Contains('if(-not $AllowCloudFile)') -and
  $raw.Contains('Test-CloudReparse $item.FullName') -and
  $raw.Contains("'addon-reparse-tag-untrusted'"))
Check 'reader-files-still-reject-reparse' ($raw.Contains("'reader-file-reparse-rejected'") -and
  $raw.Contains('Assert-File (Join-Path $stageRoot $name) $entry') -and
  $raw.Contains('Assert-File $Destination $Expected'))
Check 'cloud-hydration-failure-distinct-from-mismatch' ($raw.Contains("'addon-content-unavailable'") -and
  $raw.Contains("'addon-hash-mismatch'") -and
  $raw.Contains('$known -ccontains $reason'))
Check 'addon-never-target-of-copy' ($raw.Contains('Copy-Verified (Join-Path $stageRoot $name) (Join-Path $readerPending $name)') -and
  -not $raw.Contains('Copy-Item -Destination $addonFinal'))
Check 'absent-only-pending-final' ($raw.Contains('reader-target-already-exists') -and
  $raw.Contains('reader-target-raced') -and
  $raw.Contains('[IO.Directory]::Move($readerPending,$readerFinal)'))
Check 'game-identity-preflight' ($raw.Contains('$ExpectedGameProcessStartUtcTicks') -and
  $raw.Contains('Assert-Game') -and
  $raw.Contains('game-image-changed'))
Check 'reader-exclusion' ($raw.Contains('Assert-NoReaders') -and
  $raw.Contains('other-reader-running'))
Check 'explicit-lab2-endpoint' ($raw.Contains('http://192.168.56.1:48761/observation'))
Check 'no-secret-output' (-not $raw.Contains('Write-Host $config.delivery.token') -and
  -not $raw.Contains('Write-Output $config.delivery.token'))
Check 'bounded-unique-receipt' ($raw.Contains('[IO.FileMode]::CreateNew') -and
  $raw.Contains('if($bytes.Length -gt 16384)'))
Check 'receipt-fail-closed' ($raw.Contains("'partial-install-needs-review'") -and
  $raw.Contains("'blocked-no-final-install'") -and
  $raw.Contains('if($report.state -ne '))
Check 'receipt-no-side-effect-flags' ($raw.Contains('addonUntouched=$true') -and
  $raw.Contains('noReaderStart=$true') -and
  $raw.Contains('noGameReload=$true') -and
  $raw.Contains('noGameInput=$true') -and
  $raw.Contains('noVmLifecycleChange=$true'))
Write-Output ('SUMMARY '+$passed+'/'+($passed+$failures.Count))
if($failures.Count){exit 2}
