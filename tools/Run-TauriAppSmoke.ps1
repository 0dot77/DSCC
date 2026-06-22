[CmdletBinding()]
param(
    [string] $AppRoot = "C:\Users\o77do\Developer\theme-toon-dance",

    [string] $AppConfigPath = "config\show.local.json",

    [string] $HostName = "127.0.0.1",

    [int] $Port = 0,

    [int] $DurationSeconds = 15,

    [string] $StationIds = "all",

    [string] $SerialTemplate = "FAKE-SENDER-{0:000}",

    [int] $PreflightDurationSeconds = 3,

    [int] $ReceiverSocketTimeoutSeconds = 5,

    [switch] $SkipFakeStreamPreflight,

    [switch] $ViaProbe,

    [int] $ProbePort = 0,

    [switch] $RequireAppProcess,

    [switch] $AllowMissingReceiverSocket,

    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

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

function Invoke-ExternalStep {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][scriptblock] $Action
    )

    Write-Host ""
    Write-Host "== $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

function Read-AppConfig {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Tauri app config not found: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-ReceiverPort {
    param([Parameter(Mandatory = $true)] $Config)

    if ($null -ne $Config.receiver -and $null -ne $Config.receiver.port) {
        return [int] $Config.receiver.port
    }

    if ($null -ne $Config.tracker -and $null -ne $Config.tracker.port) {
        return [int] $Config.tracker.port
    }

    return 55010
}

function Get-ReceiverAutoStart {
    param([Parameter(Mandatory = $true)] $Config)

    if ($null -eq $Config.receiver -or $null -eq $Config.receiver.autoStart) {
        return $false
    }

    return [bool] $Config.receiver.autoStart
}

function Find-TauriAppProcess {
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -like "*dscc*tauri*dance*" -or
        $_.MainWindowTitle -like "*DSCC Tauri Dance Player*"
    })
}

function Find-UdpReceiverEndpoint {
    param(
        [Parameter(Mandatory = $true)][int] $LocalPort,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][int[]] $ProcessIds
    )

    $endpoints = @(Get-NetUDPEndpoint -LocalPort $LocalPort -ErrorAction SilentlyContinue)
    if ($endpoints.Count -eq 0) {
        $endpoints = @(Get-NetstatUdpEndpoint $LocalPort)
    }

    if ($ProcessIds.Count -eq 0) {
        return $endpoints
    }

    return @($endpoints | Where-Object { $ProcessIds -contains [int]$_.OwningProcess })
}

function Get-NetstatUdpEndpoint {
    param([Parameter(Mandatory = $true)][int] $LocalPort)

    return @(netstat -ano -p udp | ForEach-Object {
        if ($_ -notmatch '^\s*UDP\s+(?<endpoint>\S+)\s+\*:\*\s+(?<pid>\d+)\s*$') {
            return
        }

        $localEndpoint = [string]$Matches.endpoint
        $separator = $localEndpoint.LastIndexOf(':')
        if ($separator -lt 0) {
            return
        }

        $portText = $localEndpoint.Substring($separator + 1)
        $parsedPort = 0
        if (-not [int]::TryParse($portText, [ref]$parsedPort) -or $parsedPort -ne $LocalPort) {
            return
        }

        [pscustomobject]@{
            LocalAddress = $localEndpoint.Substring(0, $separator)
            LocalPort = $parsedPort
            OwningProcess = [int]$Matches.pid
        }
    })
}

function Wait-UdpReceiverEndpoint {
    param(
        [Parameter(Mandatory = $true)][int] $LocalPort,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][int[]] $ProcessIds,
        [Parameter(Mandatory = $true)][int] $TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds([Math]::Max(0, $TimeoutSeconds))
    do {
        $endpoints = @(Find-UdpReceiverEndpoint $LocalPort $ProcessIds)
        if ($endpoints.Count -gt 0) {
            return $endpoints
        }

        if ((Get-Date) -ge $deadline) {
            break
        }

        Start-Sleep -Milliseconds 250
    } while ($true)

    return @()
}

function Assert-TauriReceiverAlive {
    param(
        [Parameter(Mandatory = $true)][int] $LocalPort,
        [Parameter(Mandatory = $true)][bool] $RequireSocket,
        [Parameter(Mandatory = $true)][string] $Phase
    )

    $processes = @(Find-TauriAppProcess)
    if ($processes.Count -eq 0) {
        throw "The DSCC Tauri app process was not detected after $Phase."
    }

    foreach ($process in $processes) {
        Write-Host "[app] $Phase pid=$($process.Id) name=$($process.ProcessName) title=$($process.MainWindowTitle)"
    }

    $processIds = @($processes | ForEach-Object { [int]$_.Id })
    $endpoints = @(Find-UdpReceiverEndpoint $LocalPort $processIds)
    if ($RequireSocket -and $endpoints.Count -eq 0) {
        throw "The Tauri receiver UDP socket on port $LocalPort was not detected after $Phase."
    }

    foreach ($endpoint in $endpoints) {
        Write-Host "[udp] $Phase local=$($endpoint.LocalAddress):$($endpoint.LocalPort) pid=$($endpoint.OwningProcess)"
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

function Get-FreeUdpPort {
    $client = [System.Net.Sockets.UdpClient]::new(0)
    try {
        return [int](($client.Client.LocalEndPoint).Port)
    }
    finally {
        $client.Dispose()
    }
}

function Invoke-FakeStreamPreflight {
    param(
        [Parameter(Mandatory = $true)][int[]] $Stations,
        [Parameter(Mandatory = $true)][string] $Template
    )

    if ($PreflightDurationSeconds -le 0) {
        throw "PreflightDurationSeconds must be greater than zero"
    }

    $preflightPort = Get-FreeUdpPort
    $preflightStationArg = ($Stations -join ",")
    $expectedSerials = Format-SerialMapping $Stations $Template
    $activeSeconds = [Math]::Max($PreflightDurationSeconds, 3)

    Write-Host ""
    Write-Host "== Fake stream loopback preflight"
    Write-Host "UDP target: 127.0.0.1:$preflightPort"
    Write-Host "Stations: $preflightStationArg"
    Write-Host "Duration: $PreflightDurationSeconds seconds"

    $sender = Start-Job -ScriptBlock {
        param($Root, $TargetPort, $TargetStations, $ActiveSeconds, $Template)

        Set-Location $Root
        dotnet run --project .\tools\DsccFakeSender --no-build -- 127.0.0.1 $TargetPort $TargetStations $ActiveSeconds $Template
    } -ArgumentList $repoRoot, $preflightPort, $preflightStationArg, $activeSeconds, $Template

    Start-Sleep -Seconds 1
    try {
        dotnet run --project .\tools\DsccUdpProbe --no-build -- $preflightPort $PreflightDurationSeconds --field-strict --max-active-body-count 1 --expect-device-type FakeSender --expect-stations $preflightStationArg --expect-serials $expectedSerials --min-player-ratio 0.5 --min-active-ratio 0.5 --min-active-confidence 0.8 --min-active-joint-confidence-ratio 0.9 --required-active-joints Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight --min-required-active-joint-confidence-ratio 0.8 --min-active-joint-motion-m 0.05 --motion-joints Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight --max-player-gap-ms 3000 --max-active-gap-ms 3000
        if ($LASTEXITCODE -ne 0) {
            throw "Fake stream loopback preflight failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Stop-Job $sender -ErrorAction SilentlyContinue
        Write-Host ""
        Write-Host "== Fake preflight sender sample"
        Receive-Job $sender -ErrorAction SilentlyContinue | Select-Object -First 20
        Remove-Job $sender -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path -LiteralPath $AppRoot)) {
    throw "Tauri app root not found: $AppRoot"
}

$resolvedAppRoot = (Resolve-Path -LiteralPath $AppRoot).Path
$resolvedAppConfigPath = Resolve-PathFromBase $resolvedAppRoot $AppConfigPath
$appConfig = Read-AppConfig $resolvedAppConfigPath

if ($Port -le 0) {
    $Port = Get-ReceiverPort $appConfig
}

if ($ViaProbe -and $ProbePort -le 0) {
    $ProbePort = Get-FreeUdpPort
}

if ($ViaProbe -and $ProbePort -eq $Port) {
    throw "ViaProbe requires a probe input port different from the Tauri receiver port $Port."
}

if ($DurationSeconds -le 0) {
    throw "DurationSeconds must be greater than zero"
}

if ($ReceiverSocketTimeoutSeconds -lt 0) {
    throw "ReceiverSocketTimeoutSeconds must be greater than or equal to zero"
}

if (-not $NoBuild) {
    Invoke-ExternalStep "Build fake sender" {
        dotnet build .\tools\DsccFakeSender\DsccFakeSender.csproj --no-restore -p:UseSharedCompilation=false
    }

    if ($ViaProbe -or -not $SkipFakeStreamPreflight) {
        Invoke-ExternalStep "Build UDP probe" {
            dotnet build .\tools\DsccUdpProbe\DsccUdpProbe.csproj --no-restore -p:UseSharedCompilation=false
        }
    }
}

$stationIdList = @(Get-StationIdList $StationIds)

if (-not $SkipFakeStreamPreflight) {
    Invoke-FakeStreamPreflight $stationIdList $SerialTemplate
}

Write-Host ""
Write-Host "== Tauri app receiver target"
Write-Host "App root: $resolvedAppRoot"
Write-Host "App config: $resolvedAppConfigPath"
Write-Host "UDP target: $HostName`:$Port"
if ($ViaProbe) {
    Write-Host "Probe tee: 127.0.0.1:$ProbePort -> $HostName`:$Port"
}
Write-Host "Stations: $StationIds"
Write-Host "Duration: $DurationSeconds seconds"

if (-not (Get-ReceiverAutoStart $appConfig)) {
    Write-Host "[warn] receiver.autoStart is false; start the receiver in the Tauri app before running this smoke."
}

$appProcesses = @(Find-TauriAppProcess)
if ($appProcesses.Count -eq 0) {
    $message = "No DSCC Tauri app process was detected. Start the Tauri app first if this smoke is meant to exercise the real UI."
    if ($RequireAppProcess) {
        throw $message
    }

    Write-Host "[warn] $message"
}
else {
    foreach ($process in $appProcesses) {
        Write-Host "[app] pid=$($process.Id) name=$($process.ProcessName) title=$($process.MainWindowTitle)"
    }
}

$appProcessIds = @($appProcesses | ForEach-Object { [int]$_.Id })
if ($RequireAppProcess -and -not $AllowMissingReceiverSocket -and $appProcessIds.Count -gt 0) {
    Write-Host "[app] waiting up to $ReceiverSocketTimeoutSeconds seconds for UDP receiver socket on port $Port"
    $receiverEndpoints = @(Wait-UdpReceiverEndpoint $Port $appProcessIds $ReceiverSocketTimeoutSeconds)
}
else {
    $receiverEndpoints = @(Find-UdpReceiverEndpoint $Port $appProcessIds)
}

if ($receiverEndpoints.Count -eq 0) {
    $message = if ($appProcessIds.Count -gt 0) {
        "No UDP receiver socket on port $Port was detected for the DSCC Tauri app process. The app may be open but its receiver has not started."
    }
    else {
        "No UDP receiver socket on port $Port was detected."
    }

    if ($RequireAppProcess -and -not $AllowMissingReceiverSocket) {
        throw $message
    }

    Write-Host "[warn] $message"
}
else {
    foreach ($endpoint in $receiverEndpoints) {
        Write-Host "[udp] local=$($endpoint.LocalAddress):$($endpoint.LocalPort) pid=$($endpoint.OwningProcess)"
    }
}

Write-Host ""
if ($ViaProbe) {
    Write-Host "== Send fake four-station skeleton stream through UDP probe tee"
    Write-Host "In the Tauri app, press 'd' and expect stations 1-4 to show FakeSender frames and moving avatars."
}
else {
    Write-Host "== Send fake four-station skeleton stream"
    Write-Host "In the Tauri app, press 'd' and expect stations 1-4 to show FakeSender frames and moving avatars."
}

$stationArg = ($stationIdList -join ",")
$expectedSerials = Format-SerialMapping $stationIdList $SerialTemplate
$probe = $null
$probeDurationSeconds = [Math]::Max($DurationSeconds, 3)
$sendHost = if ($ViaProbe) { "127.0.0.1" } else { $HostName }
$sendPort = if ($ViaProbe) { $ProbePort } else { $Port }
$senderActiveSeconds = if ($ViaProbe) { $DurationSeconds + 2 } else { $DurationSeconds }
$sender = Start-Job -ScriptBlock {
    param($Root, $TargetHost, $TargetPort, $TargetStations, $ActiveSeconds, $Template)

    Set-Location $Root
    dotnet run --project .\tools\DsccFakeSender --no-build -- $TargetHost $TargetPort $TargetStations $ActiveSeconds $Template
} -ArgumentList $repoRoot, $sendHost, $sendPort, $stationArg, $senderActiveSeconds, $SerialTemplate

if ($ViaProbe) {
    Start-Sleep -Seconds 1
    $probe = Start-Job -ScriptBlock {
        param($Root, $ListenPort, $ProbeDuration, $TargetHost, $TargetPort, $TargetStations, $ExpectedSerials)

        Set-Location $Root
        dotnet run --project .\tools\DsccUdpProbe --no-build -- $ListenPort $ProbeDuration --field-strict --max-active-body-count 1 --expect-device-type FakeSender --expect-stations $TargetStations --expect-serials $ExpectedSerials --min-player-ratio 0.5 --min-active-ratio 0.5 --min-active-confidence 0.8 --min-active-joint-confidence-ratio 0.9 --required-active-joints Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight --min-required-active-joint-confidence-ratio 0.8 --min-active-joint-motion-m 0.05 --motion-joints Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight --max-player-gap-ms 3000 --max-active-gap-ms 3000 --forward-to "$TargetHost`:$TargetPort"
        "EXITCODE=$LASTEXITCODE"
    } -ArgumentList $repoRoot, $ProbePort, $probeDurationSeconds, $HostName, $Port, $stationArg, $expectedSerials
}

try {
    Start-Sleep -Seconds $DurationSeconds
}
finally {
    Stop-Job $sender -ErrorAction SilentlyContinue
    Write-Host ""
    Write-Host "== Fake sender sample"
    Receive-Job $sender -ErrorAction SilentlyContinue | Select-Object -First 30
    Remove-Job $sender -Force -ErrorAction SilentlyContinue

    if ($null -ne $probe) {
        Wait-Job $probe -Timeout ([Math]::Max(2, $probeDurationSeconds + 2)) | Out-Null
        Stop-Job $probe -ErrorAction SilentlyContinue
        Write-Host ""
        Write-Host "== UDP probe tee summary"
        $probeOutput = @(Receive-Job $probe -ErrorAction SilentlyContinue)
        $probeOutput | Select-String -Pattern '^\[done\]|^\[forward\]|^\[acceptance|^\[pass\]|^\[fail\]|EXITCODE='
        Remove-Job $probe -Force -ErrorAction SilentlyContinue

        if (($probeOutput -join "`n") -notmatch '\[pass\] validation passed') {
            throw "UDP probe tee validation did not pass."
        }

        if (($probeOutput -join "`n") -notmatch '\[forward\] forwarded \d+ packets .*errors 0') {
            throw "UDP probe tee did not report clean forwarding."
        }
    }
}

if ($RequireAppProcess) {
    Write-Host ""
    Write-Host "== Tauri app receiver post-send check"
    Assert-TauriReceiverAlive $Port (-not $AllowMissingReceiverSocket) "fake stream"
}

Write-Host ""
Write-Host "[pass] Tauri app smoke stream completed"
