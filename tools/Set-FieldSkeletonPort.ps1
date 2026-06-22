[CmdletBinding()]
param(
    [ValidateSet("Status", "Direct", "Tee")]
    [string] $Mode = "Status",

    [string] $ConfigPath = "config\wall-a.local.json",

    [string] $AppRoot = "C:\Users\o77do\Developer\theme-toon-dance",

    [string] $AppConfigPath = "config\show.local.json",

    [int] $Port = 55130,

    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Resolve-PathFromBase {
    param(
        [Parameter(Mandatory = $true)][string] $BasePath,
        [Parameter(Mandatory = $true)][string] $Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $BasePath $Path
}

function Assert-Port {
    param(
        [Parameter(Mandatory = $true)][int] $Value,
        [Parameter(Mandatory = $true)][string] $Name
    )

    if ($Value -lt 1 -or $Value -gt 65535) {
        throw "$Name must be between 1 and 65535."
    }
}

function Get-AppSkeletonPortFromConfig {
    param([Parameter(Mandatory = $true)][string] $Path)

    $config = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($null -ne $config.receiver -and $null -ne $config.receiver.port) {
        return [int] $config.receiver.port
    }

    if ($null -ne $config.tracker -and $null -ne $config.tracker.port) {
        return [int] $config.tracker.port
    }

    return 55010
}

function Set-JsonProperty {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)] $Value
    )

    if ($null -eq $Object.PSObject.Properties[$Name]) {
        $Object | Add-Member -MemberType NoteProperty -Name $Name -Value $Value
    }
    else {
        $Object.$Name = $Value
    }
}

function New-BackupPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    return "$Path.bak-$timestamp"
}

$resolvedConfigPath = Resolve-RepoPath $ConfigPath
if (-not (Test-Path -LiteralPath $resolvedConfigPath)) {
    throw "DSCC config not found: $resolvedConfigPath"
}
$resolvedConfigPath = (Resolve-Path -LiteralPath $resolvedConfigPath).Path

$resolvedAppRoot = if ([System.IO.Path]::IsPathRooted($AppRoot)) { $AppRoot } else { Resolve-RepoPath $AppRoot }
$resolvedAppConfigPath = Resolve-PathFromBase $resolvedAppRoot $AppConfigPath
if (-not (Test-Path -LiteralPath $resolvedAppConfigPath)) {
    throw "Tauri app config not found: $resolvedAppConfigPath"
}
$resolvedAppConfigPath = (Resolve-Path -LiteralPath $resolvedAppConfigPath).Path

$appPort = Get-AppSkeletonPortFromConfig $resolvedAppConfigPath
Assert-Port $appPort "Tauri receiver port"

$config = Get-Content -LiteralPath $resolvedConfigPath -Raw | ConvertFrom-Json
if ($null -eq $config.PSObject.Properties["unity"] -or $null -eq $config.unity) {
    Set-JsonProperty $config "unity" ([pscustomobject]@{})
}

$currentPort = if ($null -eq $config.unity.PSObject.Properties["skeletonPort"] -or $null -eq $config.unity.skeletonPort) {
    55010
}
else {
    [int] $config.unity.skeletonPort
}

$routeMode = if ($currentPort -eq $appPort) {
    "Direct"
}
elseif ($currentPort -eq $Port) {
    "Tee"
}
else {
    "Custom"
}

if ($Mode -eq "Status") {
    Write-Host "[config] DSCC unity.skeletonPort=$currentPort; Tauri receiver.port=$appPort; teePort=$Port; mode=$routeMode"
    Write-Host ""
    if ($routeMode -eq "Direct") {
        Write-Host "[pass] DSCC is configured for direct DSCC-to-Tauri routing."
        Write-Host "To switch to tee validation:"
        Write-Host "  .\tools\Set-FieldSkeletonPort.ps1 -Mode Tee -Port $Port"
    }
    elseif ($routeMode -eq "Tee") {
        Write-Host "[pass] DSCC is configured for tee validation input on port $Port."
        Write-Host "To restore direct DSCC-to-Tauri routing:"
        Write-Host "  .\tools\Set-FieldSkeletonPort.ps1 -Mode Direct"
    }
    else {
        Write-Host "[warn] DSCC is using a custom skeleton port. Pass -Port $currentPort when running tee checks, or restore direct routing."
        Write-Host "To restore direct DSCC-to-Tauri routing:"
        Write-Host "  .\tools\Set-FieldSkeletonPort.ps1 -Mode Direct"
    }

    return
}

$targetPort = if ($Mode -eq "Direct") { $appPort } else { $Port }
Assert-Port $targetPort "DSCC skeleton port"

if ($Mode -eq "Tee" -and $targetPort -eq $appPort) {
    throw "Tee mode requires a DSCC probe input port different from the Tauri receiver port $appPort."
}

Write-Host "[config] DSCC unity.skeletonPort=$currentPort; Tauri receiver.port=$appPort; target=$targetPort mode=$Mode"

if ($currentPort -eq $targetPort) {
    Write-Host "[pass] DSCC skeleton port already matches requested $Mode mode."
}
else {
    Set-JsonProperty $config.unity "skeletonPort" $targetPort

    if ($DryRun) {
        Write-Host "[dry-run] DSCC config would be updated: $resolvedConfigPath"
    }
    else {
        $backupPath = New-BackupPath $resolvedConfigPath
        Copy-Item -LiteralPath $resolvedConfigPath -Destination $backupPath
        $json = $config | ConvertTo-Json -Depth 100
        Set-Content -LiteralPath $resolvedConfigPath -Value $json -Encoding UTF8
        Write-Host "[backup] $backupPath"
        Write-Host "[pass] DSCC unity.skeletonPort updated to $targetPort in $resolvedConfigPath"
    }
}

Write-Host ""
if ($Mode -eq "Direct") {
    Write-Host "Next check:"
    Write-Host "  .\tools\Run-FieldSmoke.ps1 -Mode App -NoBuild"
}
else {
    Write-Host "Next check:"
    Write-Host "  .\tools\Run-FieldSmoke.ps1 -Mode Config -ForwardToApp -Port $targetPort -NoBuild"
    Write-Host "Live tee:"
    Write-Host "  .\tools\Run-FieldSmoke.ps1 -Mode Live -ForwardToApp -Port $targetPort -DurationSeconds 60 -NoBuild"
}
