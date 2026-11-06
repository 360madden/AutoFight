<# Offline static tests for Lab2-only reader launcher. Never invoke the launcher. #>
$ErrorActionPreference='Stop'
$source=Join-Path (Split-Path -Parent $PSScriptRoot) '..\scripts\start_lab2_guest_reader.ps1'
$source=[IO.Path]::GetFullPath($source)
if(-not (Test-Path -LiteralPath $source)){throw 'missing-guest-launcher'}
$text=[IO.File]::ReadAllText($source)
$tokens=$null
$parseErrors=$null
[void][System.Management.Automation.Language.Parser]::ParseFile(
  $source,[ref]$tokens,[ref]$parseErrors)
$passed=0
function Check([string]$name,[bool]$ok) {
 if(-not $ok){throw ('FAIL: '+$name)}
 $script:passed++
 Write-Output ('PASS: '+$name)
}
function Has([string]$literal) {
 return $text.Contains($literal)
}
Check 'powershell-syntax' ($parseErrors.Count -eq 0)
Check 'one-executable-only' (Has "Rift.Automation.GuestReader.exe")
Check 'lab2-exact-machine' (Has "RIFT-LAB-2")
Check 'lab2-exact-user' (Has "UserName -cne 'mrkoo'")
Check 'lab2-installed-reader-only' (Has '$readerNames=@($exeName,$configName)')
Check 'lab2-stage-kind' (Has "autofight-lab2-reader-stage")
Check 'lab2-stage-hash-pin' (Has 'ExpectedManifestSha256')
Check 'lab2-exe-hash-pin' (Has 'ExpectedReaderExeSha256')
Check 'lab2-config-hash-pin' (Has 'ExpectedConfigSha256')
Check 'launcher-self-hash' (Has 'ExpectedLauncherSha256')
Check 'reader-file-assertions' (Has 'Assert-File (Join-Path $reader $name) $entry')
Check 'staged-file-assertions' (Has 'Assert-File (Join-Path $stage $name) $entry')
Check 'exact-reader-file-count' (Has '$actualReader.Count -ne 2')
Check 'addon-source-hash-before-and-after' ([regex]::Matches($text,'Assert-Addons @\(\$manifest.addonFiles\)').Count -ge 2)
Check 'cloud-file-placeholder-acceptance' (Has 'Test-CloudReparse')
Check 'addon-write-absent' (-not [regex]::IsMatch($text,
  '(?i)(Copy-Item|Move-Item|Remove-Item|Set-Content|Out-File|Add-Content|New-Item)[^\r\n]*\$addon'))
Check 'game-pid-start-exe-hash' ((Has '$ExpectedGameProcessId') -and
  (Has '$ExpectedGameProcessStartUtcTicks') -and
  (Has '$ExpectedGameExecutablePath') -and
  (Has '$ExpectedGameExecutableSha256'))
Check 'one-rift-process' (Has '$all.Count -ne 1')
Check 'competing-readers-check' ((Has "RiftReader2.Live.exe") -and
  (Has "RiftReader2.GuestReader.exe") -and (Has 'Assert-NoReaders'))
Check 'launch-exact-live-argument' (Has "-ArgumentList '--live'")
Check 'one-launch-operation' ([regex]::Matches($text,'\bStart-Process\b').Count -eq 1)
Check 'no-rift-ui-command' (-not [regex]::IsMatch($text,
  '(?im)^[^#\r\n]*(?:SendKeys|keyPress|SendInput|SetForegroundWindow|Invoke-VMScript|Restart-VM|Stop-VM|Start-VM)'))
Check 'unique-start-id' (Has "StartRunId -cnotmatch '^[a-f0-9]{32}$'")
Check 'receipt-absent-only' (Has '[IO.FileMode]::CreateNew')
Check 'receipt-state-no-reader-bound-claim' (Has "reader-launched-not-bound")
Check 'uncertain-start-distinct' (Has "uncertain-reader-start")
Check 'no-auto-retry-loop' (-not [regex]::IsMatch($text,
  '(?im)^\s*(?:while|do)\s*[\(\{]'))
Check 'no-input-flag' (Has 'noGameInput=$true')
Check 'no-reload-flag' (Has 'noGameReload=$true')
Check 'no-addon-write-flag' (Has 'noAddonWrite=$true')
Check 'no-vm-lifecycle-flag' (Has 'noVmLifecycleChange=$true')
Check 'bearer-token-not-printed' (-not [regex]::IsMatch($text,
  '(?im)^\s*(?:Write-Output|Write-Host|Write-Error)[^\r\n]*token'))
Write-Output ("LAB2_READER_START_STATIC_TESTS_PASSED="+$passed)
