#Requires -Version 5.1
<#
Offline-only static/syntax contract for the two VMware Labs firewall helper.
This test PARSES the source; it never dot-sources or invokes it, nor calls
New-NetFirewallRule/Remove-NetFirewallRule.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path (Join-Path $PSScriptRoot '..\..') 'scripts\enable_vmware_labs_telemetry_firewall.ps1'
$sourcePath = [IO.Path]::GetFullPath($sourcePath)
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw ('Missing firewall script: ' + $sourcePath)
}
$source = [IO.File]::ReadAllText($sourcePath)
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($sourcePath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) {
    throw ('PowerShell parse errors: ' + (($parseErrors | ForEach-Object Message) -join ' | '))
}

$checks = [ordered]@{
    'exact owned rule name' = [bool]($source -match [regex]::Escape("'AutoFight-VMware-Labs-Telemetry-48761-VMnet8'"))
    'exact display name' = [bool]($source -match [regex]::Escape("'AutoFight VMware Labs Telemetry TCP 48761'"))
    'host VMnet8 alias pinned' = [bool]($source -match [regex]::Escape("'VMware Network Adapter VMnet8'"))
    'host 192.168.56.1 pinned' = [bool]($source -match [regex]::Escape("'192.168.56.1'"))
    'Lab1 192.168.56.128 pinned' = [bool]($source -match [regex]::Escape("'192.168.56.128'"))
    'Lab2 192.168.56.129 pinned' = [bool]($source -match [regex]::Escape("'192.168.56.129'"))
    'exactly two remote host IPs' = [bool]($source -match '\$remoteAddresses\s*=\s*@\(' -and $source -match 'Assert-RemoteAddressSet')
    'TCP 48761 pinned' = [bool]($source -match [regex]::Escape("'48761'"))
    'default Plan parameter set' = [bool]($source -match "DefaultParameterSetName\s*=\s*'Plan'")
    'exclusive DryRun switch' = [bool]($source -match '\[switch\]\$DryRun')
    'exclusive Enable switch' = [bool]($source -match '\[switch\]\$Enable')
    'exclusive Undo switch' = [bool]($source -match '\[switch\]\$Undo')
    'UAC elevation required' = [bool]($source -match 'IsInRole\(' -and $source -match 'Assert-Elevated')
    'host adapter status verified' = [bool]($source -match 'Get-NetAdapter' -and $source -match 'Get-NetIPAddress' -and $source -match 'PrefixLength')
    'exact match before Undo' = [bool]($source -match 'Assert-OwnedRule' -and $source -match 'collisions\.Persistent\.Count -ne 1')
    'named rule collision check' = [bool]($source -match 'Get-CollidingNamedRules' -and $source -match 'Get-NetFirewallRule -PolicyStore PersistentStore')
    'port collision check' = [bool]($source -match 'Assert-NoOtherPort48761Rules')
    'narrow inbound Allow creation' = [bool]($source -match 'New-NetFirewallRule\s+-Name \$ruleName\s+-DisplayName \$displayName\s+-PolicyStore PersistentStore\s+-Direction Inbound\s+-Action Allow\s+-Enabled True\s+-Profile Any\s+-EdgeTraversalPolicy Block\s+-Protocol TCP\s+-LocalPort \$localPort\s+-LocalAddress \$localAddress\s+-RemoteAddress \$remoteAddresses\s+-InterfaceAlias \$interfaceAlias')
    'edge policy verified on existing rule' = [bool]($source -match 'rule\.EdgeTraversalPolicy' -and $source -match '-EdgeTraversalPolicy Block')
    'program and service default filters pinned' = [bool]($source -match 'Get-NetFirewallApplicationFilter' -and $source -match 'Get-NetFirewallServiceFilter')
    'post-create active verification' = [bool]($source -match 'postCreate\.Active\.Count -ne 1' -and $source -match 'Assert-OwnedRule \$postCreate\.Active\[0\]')
    'exact input-object removal' = [bool]($source -match 'Remove-NetFirewallRule\s+-InputObject \$collisions\.Persistent\[0\]')
    'no script UAC launch' = [bool]($source -notmatch 'Start-Process|Verb RunAs')
    'source has no dynamic evaluation' = [bool]($source -notmatch 'Invoke-Expression|iex\s+')
    'no broad firewall mutation APIs' = [bool]($source -notmatch '(?im)^\s*(Set|Disable|Enable|Remove)-NetFirewall(Profile|PortFilter|AddressFilter|InterfaceFilter)\b')
}
$mutators = @($ast.FindAll({
    param($node)
    if ($node -isnot [System.Management.Automation.Language.CommandAst]) { return $false }
    $name = $node.GetCommandName()
    if ([string]::IsNullOrEmpty($name)) { return $false }
    return $name -match '^(New|Remove|Set|Enable|Disable)-NetFirewall'
}, $true) | ForEach-Object { $_.GetCommandName() })
$checks['only expected firewall mutators in source'] = [bool]($mutators.Count -eq 2 -and $mutators -contains 'New-NetFirewallRule' -and $mutators -contains 'Remove-NetFirewallRule')

$failed = @($checks.GetEnumerator() | Where-Object { -not $_.Value } | ForEach-Object Key)
if ($failed.Count -gt 0) {
    throw ('FAILED ' + $failed.Count + '/' + $checks.Count + ' checks: ' + ($failed -join '; '))
}
[pscustomobject]@{
    state = 'passed-static-no-firewall-execution'
    checks = $checks.Count
    source = 'scripts/enable_vmware_labs_telemetry_firewall.ps1'
    firewallChanged = $false
} | ConvertTo-Json -Compress
