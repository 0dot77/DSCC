[CmdletBinding()]
param(
    [string] $ConfigPath = "config\wall-a.local.json",

    [string] $AppRoot = "C:\Users\o77do\Developer\theme-toon-dance",

    [string] $AppConfigPath = "config\show.local.json",

    [int] $TeePort = 55130,

    [int] $AppSmokeDurationSeconds = 15,

    [int] $LiveDurationSeconds = 60,

    [int] $ReceiverSocketTimeoutSeconds = 10,

    [double] $MotionThresholdMeters = 0.05,

    [string] $LogPath = "",

    [switch] $ConfigureTee,

    [switch] $RestoreDirectOnFailure,

    [switch] $KeepTeeAfterFailure,

    [switch] $KeepTeeAfterSuccess,

    [switch] $NoBuild,

    [switch] $NoTranscript,

    [switch] $PlanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$script:acceptanceTranscriptStarted = $false
$script:acceptanceTranscriptPath = ""
$script:acceptanceConfigureTeeCompleted = $false
$script:acceptanceConfigPathForRestore = ""
$script:acceptanceAppRootForRestore = ""
$script:acceptanceAppConfigPathForRestore = ""

trap {
    if ((Should-RestoreDirectAfterFailure)) {
        Restore-DirectRoutingAfterFailure
    }

    if ($script:acceptanceTranscriptStarted) {
        try {
            Stop-Transcript | Out-Null
        }
        catch {
            Write-Warning "Failed to stop transcript: $_"
        }
        finally {
            $script:acceptanceTranscriptStarted = $false
        }
    }

    break
}

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

function Get-SkeletonPortFromConfig {
    param([Parameter(Mandatory = $true)][string] $Path)

    $config = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($null -eq $config.unity -or $null -eq $config.unity.skeletonPort) {
        return 55010
    }

    return [int] $config.unity.skeletonPort
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

function Assert-PositiveInt {
    param(
        [Parameter(Mandatory = $true)][int] $Value,
        [Parameter(Mandatory = $true)][string] $Name
    )

    if ($Value -le 0) {
        throw "$Name must be greater than zero."
    }
}

function Get-DefaultLogPath {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    return Join-Path $repoRoot "artifacts\field-acceptance\field-acceptance-$timestamp.log"
}

function Resolve-LogPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return Get-DefaultLogPath
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Start-AcceptanceTranscript {
    if ($NoTranscript) {
        return
    }

    $targetPath = Resolve-LogPath $LogPath
    $targetDirectory = Split-Path -Parent $targetPath
    if (-not [string]::IsNullOrWhiteSpace($targetDirectory)) {
        New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    }

    try {
        Start-Transcript -LiteralPath $targetPath -Force | Out-Null
        $script:acceptanceTranscriptStarted = $true
        $script:acceptanceTranscriptPath = $targetPath
        Write-Host "[log] transcript: $targetPath"
    }
    catch {
        if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
            throw
        }

        Write-Warning "Could not start acceptance transcript: $_"
    }
}

function Stop-AcceptanceTranscript {
    if (-not $script:acceptanceTranscriptStarted) {
        return
    }

    try {
        Stop-Transcript | Out-Null
    }
    finally {
        $script:acceptanceTranscriptStarted = $false
        if (-not [string]::IsNullOrWhiteSpace($script:acceptanceTranscriptPath)) {
            Write-Host "[log] transcript saved: $script:acceptanceTranscriptPath"
        }
    }
}

function Invoke-PowerShellScript {
    param(
        [Parameter(Mandatory = $true)][string] $ScriptPath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]] $Arguments
    )

    $resolvedScriptPath = Resolve-RepoPath $ScriptPath
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $resolvedScriptPath @Arguments
}

function Restore-DirectRoutingAfterFailure {
    if ([string]::IsNullOrWhiteSpace($script:acceptanceConfigPathForRestore)) {
        return
    }

    Write-Host ""
    Write-Host "== Restore direct DSCC-to-Tauri routing after failure"
    try {
        Invoke-PowerShellScript "tools\Set-FieldSkeletonPort.ps1" @(
            "-Mode", "Direct",
            "-ConfigPath", $script:acceptanceConfigPathForRestore,
            "-AppRoot", $script:acceptanceAppRootForRestore,
            "-AppConfigPath", $script:acceptanceAppConfigPathForRestore
        )
    }
    catch {
        Write-Warning "Failed to restore direct routing after acceptance failure: $_"
    }
}

function Should-RestoreDirectAfterFailure {
    if (-not $script:acceptanceConfigureTeeCompleted) {
        return $false
    }

    return -not $KeepTeeAfterFailure
}

function Invoke-DirectRoutingRestore {
    param([Parameter(Mandatory = $true)][string] $Reason)

    Invoke-AcceptanceStep $Reason `
        ".\tools\Set-FieldSkeletonPort.ps1 -Mode Direct -ConfigPath $ConfigPath -AppRoot $AppRoot -AppConfigPath $AppConfigPath" `
        {
            Invoke-PowerShellScript "tools\Set-FieldSkeletonPort.ps1" @(
                "-Mode", "Direct",
                "-ConfigPath", $ConfigPath,
                "-AppRoot", $AppRoot,
                "-AppConfigPath", $AppConfigPath
            )
        }
    $script:acceptanceConfigureTeeCompleted = $false
}

function Invoke-AcceptanceStep {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $CommandText,
        [Parameter(Mandatory = $true)][scriptblock] $Action
    )

    Write-Host ""
    Write-Host "== $Name"
    Write-Host $CommandText

    if ($PlanOnly) {
        Write-Host "[plan] skipped execution"
        return
    }

    $global:LASTEXITCODE = 0
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

function Invoke-PlanReadinessSnapshot {
    if (-not $PlanOnly) {
        return
    }

    Write-Host ""
    Write-Host "== Plan readiness snapshot (read-only)"
    Write-Host ".\tools\Get-FieldReadinessReport.ps1 -CheckDevices -RequireAppProcess -Strict -ConfigPath $ConfigPath -AppRoot $AppRoot -AppConfigPath $AppConfigPath -TeePort $TeePort"

    $global:LASTEXITCODE = 0
    Invoke-PowerShellScript "tools\Get-FieldReadinessReport.ps1" @(
        "-CheckDevices",
        "-RequireAppProcess",
        "-Strict",
        "-ConfigPath", $ConfigPath,
        "-AppRoot", $AppRoot,
        "-AppConfigPath", $AppConfigPath,
        "-TeePort", ([string]$TeePort)
    )

    if ($LASTEXITCODE -ne 0) {
        Write-Host "[plan-blocked] strict readiness currently fails; fix the blocking actions above before running live acceptance."
    }
    else {
        Write-Host "[plan] strict readiness currently passes."
    }
}

Assert-PositiveInt $TeePort "TeePort"
Assert-PositiveInt $AppSmokeDurationSeconds "AppSmokeDurationSeconds"
Assert-PositiveInt $LiveDurationSeconds "LiveDurationSeconds"
if ($ReceiverSocketTimeoutSeconds -lt 0) {
    throw "ReceiverSocketTimeoutSeconds must be greater than or equal to zero."
}
if ($MotionThresholdMeters -le 0) {
    throw "MotionThresholdMeters must be greater than zero."
}
if ($RestoreDirectOnFailure -and $KeepTeeAfterFailure) {
    throw "-RestoreDirectOnFailure and -KeepTeeAfterFailure cannot be used together."
}

Start-AcceptanceTranscript

$resolvedConfigPath = Resolve-RepoPath $ConfigPath
if (-not (Test-Path -LiteralPath $resolvedConfigPath)) {
    throw "DSCC config not found: $resolvedConfigPath"
}
$resolvedConfigPath = (Resolve-Path -LiteralPath $resolvedConfigPath).Path

$resolvedAppRoot = if ([System.IO.Path]::IsPathRooted($AppRoot)) { $AppRoot } else { Resolve-RepoPath $AppRoot }
if (-not (Test-Path -LiteralPath $resolvedAppRoot)) {
    throw "Tauri app root not found: $resolvedAppRoot"
}
$resolvedAppRoot = (Resolve-Path -LiteralPath $resolvedAppRoot).Path

$resolvedAppConfigPath = Resolve-PathFromBase $resolvedAppRoot $AppConfigPath
if (-not (Test-Path -LiteralPath $resolvedAppConfigPath)) {
    throw "Tauri app config not found: $resolvedAppConfigPath"
}
$resolvedAppConfigPath = (Resolve-Path -LiteralPath $resolvedAppConfigPath).Path

$dsccPort = Get-SkeletonPortFromConfig $resolvedConfigPath
$appPort = Get-AppSkeletonPortFromConfig $resolvedAppConfigPath

Write-Host "== Field acceptance"
Write-Host "DSCC config: $resolvedConfigPath"
Write-Host "Tauri config: $resolvedAppConfigPath"
Write-Host "[config] DSCC unity.skeletonPort=$dsccPort; teePort=$TeePort; Tauri receiver.port=$appPort"

if ($appPort -eq $TeePort) {
    throw "TeePort must differ from the Tauri receiver port $appPort."
}

Invoke-PlanReadinessSnapshot

if ($ConfigureTee) {
    Invoke-AcceptanceStep "Configure DSCC tee port" `
        ".\tools\Set-FieldSkeletonPort.ps1 -Mode Tee -Port $TeePort -ConfigPath $ConfigPath -AppRoot $AppRoot -AppConfigPath $AppConfigPath" `
        {
            Invoke-PowerShellScript "tools\Set-FieldSkeletonPort.ps1" @(
                "-Mode", "Tee",
                "-Port", ([string]$TeePort),
                "-ConfigPath", $ConfigPath,
                "-AppRoot", $AppRoot,
                "-AppConfigPath", $AppConfigPath
            )
        }
    $dsccPort = $TeePort
    if (-not $PlanOnly) {
        $script:acceptanceConfigureTeeCompleted = $true
        $script:acceptanceConfigPathForRestore = $ConfigPath
        $script:acceptanceAppRootForRestore = $AppRoot
        $script:acceptanceAppConfigPathForRestore = $AppConfigPath
    }
}
elseif ($dsccPort -ne $TeePort) {
    $message = "DSCC is not configured for tee acceptance. Current DSCC port is $dsccPort, expected tee input is $TeePort. Re-run with -ConfigureTee or run Set-FieldSkeletonPort first."
    if ($PlanOnly) {
        Write-Host "[plan-blocked] $message"
    }
    else {
        throw $message
    }
}

$noBuildArg = if ($NoBuild) { "-NoBuild" } else { "" }

Invoke-AcceptanceStep "Strict readiness report" `
    ".\tools\Get-FieldReadinessReport.ps1 -CheckDevices -RequireAppProcess -Strict -ConfigPath $ConfigPath -AppRoot $AppRoot -AppConfigPath $AppConfigPath -TeePort $TeePort" `
    {
        Invoke-PowerShellScript "tools\Get-FieldReadinessReport.ps1" @(
            "-CheckDevices",
            "-RequireAppProcess",
            "-Strict",
            "-ConfigPath", $ConfigPath,
            "-AppRoot", $AppRoot,
            "-AppConfigPath", $AppConfigPath,
            "-TeePort", ([string]$TeePort)
        )
    }

Invoke-AcceptanceStep "Tauri fake four-station app smoke through probe tee" `
    ".\tools\Run-FieldSmoke.ps1 -Mode App -ForwardToApp -DurationSeconds $AppSmokeDurationSeconds -ReceiverSocketTimeoutSeconds $ReceiverSocketTimeoutSeconds $noBuildArg -ConfigPath $ConfigPath -AppRoot $AppRoot -AppConfigPath $AppConfigPath" `
    {
        $stepArgs = @(
            "-Mode", "App",
            "-ForwardToApp",
            "-DurationSeconds", ([string]$AppSmokeDurationSeconds),
            "-ReceiverSocketTimeoutSeconds", ([string]$ReceiverSocketTimeoutSeconds),
            "-ConfigPath", $ConfigPath,
            "-AppRoot", $AppRoot,
            "-AppConfigPath", $AppConfigPath
        )
        if ($NoBuild) {
            $stepArgs += "-NoBuild"
        }

        Invoke-PowerShellScript "tools\Run-FieldSmoke.ps1" $stepArgs
    }

Invoke-AcceptanceStep "Live app-fed motion drill" `
    ".\tools\Run-FieldSmoke.ps1 -Mode Live -ForwardToApp -RequireMotionDrill -Port $TeePort -DurationSeconds $LiveDurationSeconds -ReceiverSocketTimeoutSeconds $ReceiverSocketTimeoutSeconds -MotionThresholdMeters $MotionThresholdMeters $noBuildArg -ConfigPath $ConfigPath -AppRoot $AppRoot -AppConfigPath $AppConfigPath" `
    {
        $stepArgs = @(
            "-Mode", "Live",
            "-ForwardToApp",
            "-RequireMotionDrill",
            "-Port", ([string]$TeePort),
            "-DurationSeconds", ([string]$LiveDurationSeconds),
            "-ReceiverSocketTimeoutSeconds", ([string]$ReceiverSocketTimeoutSeconds),
            "-MotionThresholdMeters", ([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.###}", $MotionThresholdMeters)),
            "-ConfigPath", $ConfigPath,
            "-AppRoot", $AppRoot,
            "-AppConfigPath", $AppConfigPath
        )
        if ($NoBuild) {
            $stepArgs += "-NoBuild"
        }

        Invoke-PowerShellScript "tools\Run-FieldSmoke.ps1" $stepArgs
    }

if ($ConfigureTee) {
    if ($KeepTeeAfterSuccess) {
        Write-Host ""
        Write-Host "[warn] Keeping DSCC configured for tee input port $TeePort after successful acceptance."
        Write-Host "[warn] The Tauri app will only receive live skeletons while a probe forwards $TeePort to the app receiver port."
    }
    else {
        Invoke-DirectRoutingRestore "Restore direct DSCC-to-Tauri routing after success"
    }
}

Write-Host ""
if ($PlanOnly) {
    Write-Host "[plan] field acceptance plan complete"
}
else {
    Write-Host "[pass] field acceptance completed"
}

Stop-AcceptanceTranscript
