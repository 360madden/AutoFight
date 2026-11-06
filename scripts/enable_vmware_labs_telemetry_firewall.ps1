#Requires -Version 5.1
<#
.SYNOPSIS
  Plan, enable, or undo one narrowly scoped VMware Labs AutoFight telemetry firewall rule.
.DESCRIPTION
  Applies only on the Windows host, never in a VMware guest. -DryRun (the default)
  is read-only and requires no elevation. -Enable and -Undo require an already
  elevated administrator PowerShell; this script never attempts UAC elevation.

  VMnet8 has no observed NLA connection profile, so the rule deliberately uses
  Profile Any. Its exposure remains bounded by the exact VMnet8 adapter alias,
  host IPv4 192.168.56.1, Lab1 .128 and Lab2 .129 and inbound TCP port 48761.
  No other remote IP, subnet or interface is authorized.

  A successful Enable does not start a listener or GuestReader, send game input,
  reload RIFT, or prove network reachability. Undo refuses a drifted rule.
.EXAMPLE
  .\enable_vmware_labs_telemetry_firewall.ps1 -DryRun
.EXAMPLE
  # Run from an ALREADY elevated Windows PowerShell console:
  .\enable_vmware_labs_telemetry_firewall.ps1 -Enable
.EXAMPLE
  # Run from an ALREADY elevated Windows PowerShell console:
  .\enable_vmware_labs_telemetry_firewall.ps1 -Undo
#>
[CmdletBinding(DefaultParameterSetName = 'Plan')]
param(
    [Parameter(ParameterSetName = 'Plan')]
    [switch]$DryRun,

    [Parameter(Mandatory = $true, ParameterSetName = 'Enable')]
    [switch]$Enable,

    [Parameter(Mandatory = $true, ParameterSetName = 'Undo')]
    [switch]$Undo
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ruleName = 'AutoFight-VMware-Labs-Telemetry-48761-VMnet8'
$displayName = 'AutoFight VMware Labs Telemetry TCP 48761'
$interfaceAlias = 'VMware Network Adapter VMnet8'
$localAddress = '192.168.56.1'
$remoteAddresses = @('192.168.56.128', '192.168.56.129')
$localPort = '48761'

function Assert-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'BLOCKED: Enable/Undo requires an already elevated Administrator PowerShell. No UAC launch was attempted.'
    }
}

function Assert-HostVmnet8 {
    $adapters = @(Get-NetAdapter -Name $interfaceAlias -ErrorAction Stop)
    if ($adapters.Count -ne 1 -or [string]$adapters[0].Name -cne $interfaceAlias) {
        throw 'BLOCKED: Expected exactly one named VMnet8 adapter.'
    }
    $adapter = $adapters[0]
    if ([string]$adapter.Status -ne 'Up') {
        throw 'BLOCKED: VMnet8 is not Up.'
    }
    $addresses = @(Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction Stop)
    $pinnedAddresses = @($addresses | Where-Object { $_.IPAddress -eq $localAddress -and [int]$_.PrefixLength -eq 24 })
    if ($pinnedAddresses.Count -ne 1) {
        throw 'BLOCKED: Host VMnet8 does not have the single expected 192.168.56.1/24 assignment.'
    }
    return $adapter
}

function Assert-ExactlyOne([object]$value, [string]$expected, [string]$field) {
    $items = @($value)
    if ($items.Count -ne 1 -or [string]$items[0] -cne $expected) {
        throw ('BLOCKED: Firewall rule field mismatch: ' + $field)
    }
}

function Assert-OwnedRule([object]$rule) {
    if ([string]$rule.Name -cne $ruleName -or [string]$rule.DisplayName -cne $displayName) {
        throw 'BLOCKED: Rule identity/display name differs.'
    }
    if ([string]$rule.Direction -ne 'Inbound' -or [string]$rule.Action -ne 'Allow' -or [string]$rule.Enabled -ne 'True') {
        throw 'BLOCKED: Rule direction/action/enabled differs.'
    }
    if ([string]$rule.Profile -ne 'Any') {
        throw 'BLOCKED: Rule network profile differs.'
    }
    if ([string]$rule.EdgeTraversalPolicy -ne 'Block') {
        throw 'BLOCKED: Rule edge traversal differs.'
    }

    $portFilters = @($rule | Get-NetFirewallPortFilter -ErrorAction Stop)
    $addressFilters = @($rule | Get-NetFirewallAddressFilter -ErrorAction Stop)
    $interfaceFilters = @($rule | Get-NetFirewallInterfaceFilter -ErrorAction Stop)
    $applicationFilters = @($rule | Get-NetFirewallApplicationFilter -ErrorAction Stop)
    $serviceFilters = @($rule | Get-NetFirewallServiceFilter -ErrorAction Stop)
    if ($portFilters.Count -ne 1 -or $addressFilters.Count -ne 1 -or $interfaceFilters.Count -ne 1 -or $applicationFilters.Count -ne 1 -or $serviceFilters.Count -ne 1) {
        throw 'BLOCKED: Missing or duplicated associated firewall filters.'
    }
    Assert-ExactlyOne $applicationFilters[0].Program 'Any' 'Program'
    Assert-ExactlyOne $serviceFilters[0].Service 'Any' 'Service'
    $port = $portFilters[0]
    if ([string]$port.Protocol -notin @('TCP', '6')) {
        throw 'BLOCKED: Rule protocol is not TCP.'
    }
    Assert-ExactlyOne $port.LocalPort $localPort 'LocalPort'
    Assert-ExactlyOne $port.RemotePort 'Any' 'RemotePort'

    $address = $addressFilters[0]
    Assert-ExactlyOne $address.LocalAddress $localAddress 'LocalAddress'
    Assert-RemoteAddressSet $address.RemoteAddress

    $interface = $interfaceFilters[0]
    Assert-ExactlyOne $interface.InterfaceAlias $interfaceAlias 'InterfaceAlias'
}

function Assert-RemoteAddressSet([object]$actual) {
    # Both guests, each as a host IP. Reject subnet/wildcard, omitted or extra IP.
    $items = @($actual)
    if ($items.Count -ne 2) {
        throw 'BLOCKED: Expected exactly two remote host IPv4 addresses.'
    }
    $unique = @($items | Select-Object -Unique)
    if ($unique.Count -ne 2) {
        throw 'BLOCKED: Duplicate remote host IPv4 address.'
    }
    foreach ($item in $items) {
        if (-not ($remoteAddresses -ccontains [string]$item)) {
            throw 'BLOCKED: Unexpected remote address in existing firewall rule.'
        }
    }
}

function Get-CollidingNamedRules {
    # Explicit collision protection in BOTH stores; never replace a third-party rule.
    $persistent = @(Get-NetFirewallRule -PolicyStore PersistentStore -ErrorAction Stop |
        Where-Object { $_.Name -ceq $ruleName -or $_.DisplayName -ceq $displayName })
    $active = @(Get-NetFirewallRule -PolicyStore ActiveStore -ErrorAction Stop |
        Where-Object { $_.Name -ceq $ruleName -or $_.DisplayName -ceq $displayName })
    return [pscustomobject]@{ Persistent = $persistent; Active = $active }
}

function Assert-NoOtherPort48761Rules {
    # Conservative conflict check: any other explicit active TCP 48761 rule blocks
    # installation. Wildcard port rules are not exhaustively audited by this check.
    $filters = @(Get-NetFirewallPortFilter -PolicyStore ActiveStore -ErrorAction Stop |
        Where-Object {
            ([string]$_.Protocol -in @('TCP', '6')) -and
            (@($_.LocalPort) -contains $localPort)
        })
    foreach ($filter in $filters) {
        $owners = @($filter | Get-NetFirewallRule -ErrorAction Stop)
        if ($owners.Count -ne 1 -or [string]$owners[0].Name -cne $ruleName) {
            throw 'BLOCKED: Another effective TCP 48761 firewall rule exists; manual review required.'
        }
    }
}

$mode = if ($Enable) { 'Enable' } elseif ($Undo) { 'Undo' } else { 'DryRun' }

try {
    if ($mode -eq 'DryRun') {
        $adapter = Assert-HostVmnet8
        [pscustomobject]@{
            state = 'dry-run-no-change'
            mode = 'DryRun'
            name = $ruleName
            displayName = $displayName
            direction = 'Inbound'
            action = 'Allow'
            profile = 'Any'
            protocol = 'TCP'
            localPort = [int]$localPort
            localAddress = $localAddress
            remoteAddresses = $remoteAddresses
            interfaceAlias = $interfaceAlias
            interfaceIndex = [int]$adapter.ifIndex
            elevatedMutationRequired = $true
            firewallChanged = $false
            warning = 'VMnet8 NLA profile unclassified; Profile Any is used ONLY with exact VMnet8 interface and both pinned guest IPv4 endpoints.'
        } | ConvertTo-Json -Compress
        return
    }

    Assert-Elevated

    if ($mode -eq 'Enable') {
        $null = Assert-HostVmnet8
    }

    $collisions = Get-CollidingNamedRules
    if ($collisions.Persistent.Count -gt 1 -or $collisions.Active.Count -gt 1) {
        throw 'BLOCKED: Multiple firewall rules have the expected name/display name.'
    }
    foreach ($rule in @($collisions.Persistent) + @($collisions.Active)) {
        Assert-OwnedRule $rule
    }

    if ($mode -eq 'Undo') {
        if ($collisions.Persistent.Count -ne 1) {
            throw 'BLOCKED: Exact owned PersistentStore rule missing; nothing removed.'
        }
        # Active policy disagreement is a separate manual-review condition.
        if ($collisions.Active.Count -ne 1) {
            throw 'BLOCKED: Effective rule missing/drifted; nothing removed.'
        }
        Remove-NetFirewallRule -InputObject $collisions.Persistent[0] -ErrorAction Stop
        [pscustomobject]@{ state = 'undone'; name = $ruleName; firewallChanged = $true } | ConvertTo-Json -Compress
        return
    }

    Assert-NoOtherPort48761Rules
    if ($collisions.Persistent.Count -eq 1) {
        if ($collisions.Active.Count -ne 1) {
            throw 'BLOCKED: Persisted rule is not effective; no automatic retry.'
        }
        [pscustomobject]@{ state = 'already-enabled'; name = $ruleName; firewallChanged = $false } | ConvertTo-Json -Compress
        return
    }
    if ($collisions.Active.Count -ne 0) {
        throw 'BLOCKED: Active rule name exists without matching owned persisted rule.'
    }

    $newRule = New-NetFirewallRule -Name $ruleName -DisplayName $displayName -PolicyStore PersistentStore -Direction Inbound -Action Allow -Enabled True -Profile Any -EdgeTraversalPolicy Block -Protocol TCP -LocalPort $localPort -LocalAddress $localAddress -RemoteAddress $remoteAddresses -InterfaceAlias $interfaceAlias -ErrorAction Stop
    # A post-create verification failure is NOT automatically rolled back or
    # retried; caller must inspect the exact rule before further action.
    Assert-OwnedRule $newRule
    $postCreate = Get-CollidingNamedRules
    if ($postCreate.Persistent.Count -ne 1 -or $postCreate.Active.Count -ne 1) {
        throw 'BLOCKED: Rule was created but is not uniquely effective; manual review required. Do not automatically retry.'
    }
    Assert-OwnedRule $postCreate.Persistent[0]
    Assert-OwnedRule $postCreate.Active[0]
    [pscustomobject]@{ state = 'enabled'; name = $ruleName; firewallChanged = $true } | ConvertTo-Json -Compress
}
catch {
    Write-Error -Message ('Firewall operation stopped: ' + $_.Exception.Message) -ErrorAction Continue
    exit 2
}
