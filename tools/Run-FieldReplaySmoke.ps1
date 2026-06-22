[CmdletBinding()]
param(
    [string] $CapturePath = "",

    [string] $ConfigPath = "config\wall-a.local.json",

    [int] $CaptureSeconds = 4,

    [int] $ProbeSeconds = 0,

    [double] $SendIntervalMs = 8,

    [int] $CapturePort = 0,

    [int] $ProbePort = 0,

    [string] $StationIds = "all",

    [string] $ExpectedDeviceType = "",

    [string] $ExpectedSerials = "",

    [string] $SerialTemplate = "FAKE-SENDER-{0:000}",

    [switch] $NoSerialCheck,

    [switch] $NoBuild
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

function Assert-PositiveInt {
    param(
        [Parameter(Mandatory = $true)][int] $Value,
        [Parameter(Mandatory = $true)][string] $Name
    )

    if ($Value -le 0) {
        throw "$Name must be greater than zero."
    }
}

function Assert-NonNegativeDouble {
    param(
        [Parameter(Mandatory = $true)][double] $Value,
        [Parameter(Mandatory = $true)][string] $Name
    )

    if ($Value -lt 0) {
        throw "$Name must be greater than or equal to zero."
    }
}

function Invoke-ExternalStep {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][scriptblock] $Action
    )

    Write-Host ""
    Write-Host "== $Name"
    $global:LASTEXITCODE = 0
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

function Get-FreeUdpPort {
    $client = [System.Net.Sockets.UdpClient]::new(0)
    try {
        return [int](($client.Client.LocalEndPoint).Port)
    }
    finally {
        $client.Dispose()
    }
}

function Get-StationIdList {
    param([Parameter(Mandatory = $true)][string] $Value)

    if ($Value.Equals("all", [System.StringComparison]::OrdinalIgnoreCase)) {
        return @(1, 2, 3, 4)
    }

    $ids = @($Value.Split(',', [System.StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { [int]$_.Trim() } |
        Sort-Object -Unique)

    if ($ids.Count -eq 0) {
        throw "At least one station id is required."
    }

    foreach ($id in $ids) {
        if ($id -le 0) {
            throw "Station ids must be greater than zero."
        }
    }

    return $ids
}

function Format-SerialMapping {
    param(
        [Parameter(Mandatory = $true)][int[]] $Stations,
        [Parameter(Mandatory = $true)][string] $Template
    )

    return ($Stations | ForEach-Object {
        "$_=$([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, $Template, $_))"
    }) -join ","
}

Assert-PositiveInt $CaptureSeconds "CaptureSeconds"
if ($ProbeSeconds -lt 0) {
    throw "ProbeSeconds must be greater than or equal to zero. Use zero for automatic duration."
}
Assert-NonNegativeDouble $SendIntervalMs "SendIntervalMs"

$resolvedConfigPath = Resolve-RepoPath $ConfigPath
if (-not (Test-Path -LiteralPath $resolvedConfigPath)) {
    throw "DSCC config not found: $resolvedConfigPath"
}
$resolvedConfigPath = (Resolve-Path -LiteralPath $resolvedConfigPath).Path

$createFakeCapture = [string]::IsNullOrWhiteSpace($CapturePath)
if ($createFakeCapture) {
    if ($CaptureSeconds -lt 4) {
        throw "CaptureSeconds must be at least 4 for fake capture smoke so Active head/Nose motion can satisfy the field gate."
    }

    $artifactDir = Join-Path $repoRoot "artifacts\field-capture-smoke"
    New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
    $CapturePath = Join-Path $artifactDir ("fake-four-station-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".jsonl")
}
else {
    $CapturePath = Resolve-RepoPath $CapturePath
    if (-not (Test-Path -LiteralPath $CapturePath)) {
        throw "Capture file not found: $CapturePath"
    }
    $CapturePath = (Resolve-Path -LiteralPath $CapturePath).Path
}

if ($CapturePort -le 0) {
    $CapturePort = Get-FreeUdpPort
}
if ($ProbePort -le 0) {
    $ProbePort = Get-FreeUdpPort
}
if ($CapturePort -eq $ProbePort) {
    throw "CapturePort and ProbePort must differ."
}

$stationIdList = @(Get-StationIdList $StationIds)
$stationArg = ($stationIdList -join ",")
$appDrivingJointCsv = "Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight"
$appDrivingJoints = @($appDrivingJointCsv.Split(",", [System.StringSplitOptions]::RemoveEmptyEntries))
if ([string]::IsNullOrWhiteSpace($ExpectedDeviceType)) {
    $ExpectedDeviceType = if ($createFakeCapture) { "FakeSender" } else { "FemtoMega" }
}

if (-not $NoBuild) {
    Invoke-ExternalStep "Build capture/replay tools" {
        dotnet build .\tools\DsccUdpCapture\DsccUdpCapture.csproj --no-restore -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) {
            return
        }

        dotnet build .\tools\DsccUdpProbe\DsccUdpProbe.csproj --no-restore -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) {
            return
        }

        if ($createFakeCapture) {
            dotnet build .\tools\DsccFakeSender\DsccFakeSender.csproj --no-restore -p:UseSharedCompilation=false
        }
    }
}

if ($createFakeCapture) {
    Write-Host ""
    Write-Host "== Record fake four-station capture"
    Write-Host "Capture: $CapturePath"
    Write-Host "UDP: 127.0.0.1:$CapturePort"

    $captureJob = Start-Job -ScriptBlock {
        param($Root, $Port, $Seconds, $File)

        Set-Location $Root
        dotnet run --project .\tools\DsccUdpCapture --no-build -- record $Port $Seconds $File
        "CAPTURE_EXIT=$LASTEXITCODE"
    } -ArgumentList $repoRoot, $CapturePort, $CaptureSeconds, $CapturePath

    Start-Sleep -Milliseconds 800
    $senderJob = Start-Job -ScriptBlock {
        param($Root, $Port, $Stations, $Seconds, $Template)

        Set-Location $Root
        dotnet run --project .\tools\DsccFakeSender --no-build -- 127.0.0.1 $Port $Stations $Seconds $Template
        "SENDER_EXIT=$LASTEXITCODE"
    } -ArgumentList $repoRoot, $CapturePort, $stationArg, $CaptureSeconds, $SerialTemplate

    try {
        Wait-Job $captureJob -Timeout ([Math]::Max(10, $CaptureSeconds + 10)) | Out-Null
        $captureOutput = @(Receive-Job $captureJob -ErrorAction SilentlyContinue)
        $senderOutput = @(Receive-Job $senderJob -ErrorAction SilentlyContinue)
        $captureOutput
        $senderOutput | Select-Object -First 12

        if (($captureOutput -join "`n") -notmatch 'CAPTURE_EXIT=0') {
            throw "Capture command did not exit cleanly."
        }
    }
    finally {
        Stop-Job $captureJob -ErrorAction SilentlyContinue
        Stop-Job $senderJob -ErrorAction SilentlyContinue
        Remove-Job $captureJob -Force -ErrorAction SilentlyContinue
        Remove-Job $senderJob -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path -LiteralPath $CapturePath)) {
    throw "Capture file was not created: $CapturePath"
}

$lineCount = (Get-Content -LiteralPath $CapturePath | Measure-Object -Line).Lines
if ($lineCount -eq 0) {
    throw "Capture file contains no frames: $CapturePath"
}

if ($ProbeSeconds -eq 0) {
    $replaySeconds = if ($SendIntervalMs -eq 0) { 6 } else { [Math]::Ceiling(($lineCount * $SendIntervalMs) / 1000d) + 3 }
    $ProbeSeconds = [Math]::Max(6, [int]$replaySeconds)
}

$serialArgs = @()
if (-not $NoSerialCheck) {
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSerials)) {
        $serialArgs = @("--expect-serials", $ExpectedSerials)
    }
    elseif ($ExpectedDeviceType.Equals("FakeSender", [System.StringComparison]::OrdinalIgnoreCase)) {
        $serialArgs = @("--expect-serials", (Format-SerialMapping $stationIdList $SerialTemplate))
    }
    else {
        $serialArgs = @("--expect-serials-from-config", $resolvedConfigPath)
    }
}

$probeArgs = @(
    "--field-strict",
    "--max-active-body-count", "1",
    "--expect-device-type", $ExpectedDeviceType,
    "--expect-stations", $stationArg,
    "--min-player-ratio", "0.5",
    "--min-active-ratio", "0.5",
    "--min-active-confidence", "0.8",
    "--min-active-joint-confidence-ratio", "0.9",
    "--required-active-joints", $appDrivingJointCsv,
    "--min-required-active-joint-confidence-ratio", "0.8",
    "--min-active-joint-motion-m", "0.05",
    "--motion-joints", $appDrivingJointCsv,
    "--max-player-gap-ms", "3000",
    "--max-active-gap-ms", "3000"
) + $serialArgs

Write-Host ""
Write-Host "== Replay capture through UDP probe"
Write-Host "Capture: $CapturePath"
Write-Host "Frames: $lineCount"
Write-Host "Replay: 127.0.0.1:$ProbePort sendIntervalMs=$SendIntervalMs"
Write-Host "ExpectedDeviceType: $ExpectedDeviceType"

$probeJob = Start-Job -ScriptBlock {
    param($Root, $Port, $Seconds, $ArgsForProbe)

    Set-Location $Root
    dotnet run --project .\tools\DsccUdpProbe --no-build -- $Port $Seconds @ArgsForProbe
    "PROBE_EXIT=$LASTEXITCODE"
} -ArgumentList $repoRoot, $ProbePort, $ProbeSeconds, $probeArgs

Start-Sleep -Milliseconds 800
$replayJob = Start-Job -ScriptBlock {
    param($Root, $File, $Port, $IntervalMs)

    Set-Location $Root
    dotnet run --project .\tools\DsccUdpCapture --no-build -- replay $File 127.0.0.1 $Port --send-interval-ms $IntervalMs
    "REPLAY_EXIT=$LASTEXITCODE"
} -ArgumentList $repoRoot, $CapturePath, $ProbePort, ([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.###}", $SendIntervalMs))

try {
    Wait-Job $probeJob -Timeout ([Math]::Max(15, $ProbeSeconds + 10)) | Out-Null
    Wait-Job $replayJob -Timeout 10 | Out-Null
    $probeOutput = @(Receive-Job $probeJob -ErrorAction SilentlyContinue)
    $replayOutput = @(Receive-Job $replayJob -ErrorAction SilentlyContinue)

    Write-Host ""
    Write-Host "== Replay output"
    $replayOutput

    Write-Host ""
    Write-Host "== Probe summary"
    $probeOutput | Select-String -Pattern '^\[done\]|^\[station |^\[acceptance|^\[pass\]|^\[fail\]|PROBE_EXIT='

    if (($replayOutput -join "`n") -notmatch 'REPLAY_EXIT=0') {
        throw "Replay command did not exit cleanly."
    }
    if (($probeOutput -join "`n") -notmatch '\[pass\] validation passed') {
        throw "Replay probe validation did not pass."
    }
    $probeText = $probeOutput -join "`n"
    foreach ($jointName in $appDrivingJoints) {
        if (-not [regex]::IsMatch($probeText, "requiredJointConf=[^\r\n]*\b$([regex]::Escape($jointName)):100%")) {
            throw "Replay probe output is missing required joint confidence evidence: $jointName"
        }

        if (-not [regex]::IsMatch($probeText, "jointMotion=[^\r\n]*\b$([regex]::Escape($jointName)):[0-9.]+m")) {
            throw "Replay probe output is missing required joint motion evidence: $jointName"
        }
    }

    foreach ($requiredEvidence in @(
        "bodyStability=",
        "extraBodies>1=0",
        "missingSelected=0"
    )) {
        if ($probeText -notlike "*$requiredEvidence*") {
            throw "Replay probe output is missing required field evidence: $requiredEvidence"
        }
    }
}
finally {
    Stop-Job $probeJob -ErrorAction SilentlyContinue
    Stop-Job $replayJob -ErrorAction SilentlyContinue
    Remove-Job $probeJob -Force -ErrorAction SilentlyContinue
    Remove-Job $replayJob -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "[pass] field replay smoke completed"
