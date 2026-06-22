[CmdletBinding()]
param(
    [ValidateSet("Config", "Offline", "Live", "App")]
    [string] $Mode = "Offline",

    [string] $ConfigPath = "config\wall-a.local.json",

    [string] $AppRoot = "C:\Users\o77do\Developer\theme-toon-dance",

    [string] $AppConfigPath = "config\show.local.json",

    [int] $Port = 0,

    [int] $DurationSeconds = 60,

    [int] $OfflineDurationSeconds = 5,

    [int] $OfflineActiveSeconds = 20,

    [int] $ReceiverSocketTimeoutSeconds = 5,

    [switch] $AllowExtraVisibleBodies,

    [switch] $RequireAppProcess,

    [switch] $AllowMissingAppProcess,

    [switch] $AllowMissingReceiverSocket,

    [switch] $ForwardToApp,

    [switch] $RequireMotionDrill,

    [double] $MotionThresholdMeters = 0.05,

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
    $global:LASTEXITCODE = 0
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

function Invoke-CheckedStep {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][scriptblock] $Action
    )

    Write-Host ""
    Write-Host "== $Name"
    try {
        $global:LASTEXITCODE = 0
        $output = & $Action 2>&1
        foreach ($line in $output) {
            Write-Host $line
        }

        if ($LASTEXITCODE -ne 0) {
            Write-Host "[fail] $Name failed with exit code $LASTEXITCODE"
            return ,$false
        }

        return ,$true
    }
    catch {
        Write-Host "[fail] $Name failed: $_"
        return ,$false
    }
}

function Format-CommandArgument {
    param([Parameter(Mandatory = $true)][string] $Value)

    if ($Value -match '^[A-Za-z0-9_./:\\-]+$') {
        return $Value
    }

    return "'" + $Value.Replace("'", "''") + "'"
}

function Write-StationPinningHint {
    param([Parameter(Mandatory = $true)][string] $Path)

    $configArg = Format-CommandArgument $Path

    Write-Host ""
    Write-Host "== Station serial pinning"
    Write-Host "Field mode requires fixed Femto Mega serials for enabled stations 1-4."
    Write-Host "With all four cameras connected, run:"
    Write-Host "  .\tools\Set-FieldStationSerials.ps1 -PrintTemplate -NoBuild -ConfigPath $configArg"
    Write-Host ""
    Write-Host "Then write the station pins in the physical left-to-right order. The script backs up the config and validates it:"
    Write-Host "  .\tools\Set-FieldStationSerials.ps1 -NoBuild -ConfigPath $configArg -Station1Serial SERIAL_LEFT -Station2Serial SERIAL_MID_LEFT -Station3Serial SERIAL_MID_RIGHT -Station4Serial SERIAL_RIGHT"
    Write-Host ""
    Write-Host "If the serials are known but the cameras are not connected yet, stage the config explicitly:"
    Write-Host "  .\tools\Set-FieldStationSerials.ps1 -NoBuild -ConfigPath $configArg -AllowUnconnected -Station1Serial SERIAL_LEFT -Station2Serial SERIAL_MID_LEFT -Station3Serial SERIAL_MID_RIGHT -Station4Serial SERIAL_RIGHT"
    Write-Host ""
    Write-Host "After pinning, re-run:"
    Write-Host "  .\tools\Run-FieldSmoke.ps1 -Mode Config -ConfigPath $configArg -NoBuild"
}

function Invoke-ReadinessPreflight {
    $failures = @()
    $fieldConfigPassed = Invoke-CheckedStep "Field config preflight" {
        dotnet run --project .\tools\DsccUdpProbe --no-build -- --check-field-config $resolvedConfigPath
    }

    if (-not $fieldConfigPassed) {
        $failures += "Field config preflight"
    }

    if (-not (Invoke-CheckedStep "K4A body tracking runtime preflight" {
        dotnet run --project .\tools\DsccDeviceList --no-build -- --runtime --require-cuda
    })) {
        $failures += "K4A body tracking runtime preflight"
    }

    if (-not (Invoke-CheckedStep "DSCC to Tauri UDP port compatibility" {
        if (-not (Test-Path -LiteralPath $resolvedAppConfigPath)) {
            throw "Tauri app config not found: $resolvedAppConfigPath"
        }

        if ($ForwardToApp) {
            Assert-DsccForwardPortCompatibility $resolvedConfigPath $resolvedAppConfigPath $Port
        }
        else {
            Assert-DsccAppPortCompatibility $resolvedConfigPath $resolvedAppConfigPath
        }
    })) {
        $failures += "DSCC to Tauri UDP port compatibility"
    }

    if (-not (Invoke-CheckedStep "Tauri app station and avatar config compatibility" {
        Assert-TauriAppConfigCompatibility $resolvedConfigPath $resolvedAppRoot $resolvedAppConfigPath
    })) {
        $failures += "Tauri app station/avatar config compatibility"
    }

    if ($failures.Count -gt 0) {
        if (-not $fieldConfigPassed) {
            Write-StationPinningHint $ConfigPath
        }

        throw "Readiness preflight failed: $($failures -join ', ')"
    }
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

function Assert-DsccAppPortCompatibility {
    param(
        [Parameter(Mandatory = $true)][string] $DsccConfigPath,
        [Parameter(Mandatory = $true)][string] $AppConfigPath
    )

    $dsccPort = Get-SkeletonPortFromConfig $DsccConfigPath
    $appPort = Get-AppSkeletonPortFromConfig $AppConfigPath
    Write-Host "[config] DSCC unity.skeletonPort=$dsccPort; Tauri receiver.port=$appPort"
    if ($dsccPort -ne $appPort) {
        throw "DSCC config sends skeleton UDP to $dsccPort, but Tauri app listens on $appPort. Align config\wall-a.local.json unity.skeletonPort with theme-toon-dance config receiver.port."
    }
}

function Assert-DsccForwardPortCompatibility {
    param(
        [Parameter(Mandatory = $true)][string] $DsccConfigPath,
        [Parameter(Mandatory = $true)][string] $AppConfigPath,
        [Parameter(Mandatory = $true)][int] $ProbePort
    )

    $dsccPort = Get-SkeletonPortFromConfig $DsccConfigPath
    $appPort = Get-AppSkeletonPortFromConfig $AppConfigPath
    Write-Host "[config] DSCC unity.skeletonPort=$dsccPort; probe.listen=$ProbePort; Tauri receiver.port=$appPort"
    if ($dsccPort -ne $ProbePort) {
        throw "DSCC config sends skeleton UDP to $dsccPort, but the live tee probe is listening on $ProbePort. Set config\wall-a.local.json unity.skeletonPort to the -Port value for -ForwardToApp."
    }

    if ($ProbePort -eq $appPort) {
        throw "Live tee mode requires different ports: DSCC/probe listens on $ProbePort, and Tauri receiver also listens on $appPort. Use a separate tee input port, for example -Port 55130 while Tauri receiver.port stays 55010."
    }
}

function Find-TauriAppProcess {
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -like "*dscc*tauri*dance*" -or
        $_.MainWindowTitle -like "*DSCC Tauri Dance Player*"
    })
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

function Assert-TauriAppReceiverReady {
    param([Parameter(Mandatory = $true)][int] $ReceiverPort)

    $processes = @(Find-TauriAppProcess)
    if ($processes.Count -eq 0) {
        throw "No DSCC Tauri app process was detected. Start the Tauri app before running Live -ForwardToApp."
    }

    foreach ($process in $processes) {
        Write-Host "[app] pid=$($process.Id) name=$($process.ProcessName) title=$($process.MainWindowTitle)"
    }

    $processIds = @($processes | ForEach-Object { [int]$_.Id })
    Write-Host "[app] waiting up to $ReceiverSocketTimeoutSeconds seconds for UDP receiver socket on port $ReceiverPort"
    $endpoints = @(Wait-UdpReceiverEndpoint $ReceiverPort $processIds $ReceiverSocketTimeoutSeconds)
    if ($endpoints.Count -eq 0) {
        throw "No Tauri receiver UDP socket on port $ReceiverPort was detected for the DSCC Tauri app process."
    }

    foreach ($endpoint in $endpoints) {
        Write-Host "[udp] local=$($endpoint.LocalAddress):$($endpoint.LocalPort) pid=$($endpoint.OwningProcess)"
    }
}

function Get-EnabledDsccStationIds {
    param([Parameter(Mandatory = $true)][string] $Path)

    $config = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($null -eq $config.stations) {
        throw "DSCC config does not contain stations: $Path"
    }

    return @($config.stations |
        Where-Object { $null -eq $_.enabled -or [bool]$_.enabled } |
        ForEach-Object { [int]$_.stationId } |
        Sort-Object)
}

function Get-AppStationIds {
    param([Parameter(Mandatory = $true)] $AppConfig)

    if ($null -eq $AppConfig.stations) {
        throw "Tauri app config does not contain stations"
    }

    return @($AppConfig.stations |
        ForEach-Object { [int]$_.stationId } |
        Sort-Object)
}

function Add-DuplicateValueFailures {
    param(
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()] $Values,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]] $Failures
    )

    @($Values | Group-Object | Where-Object { $_.Count -gt 1 }) | ForEach-Object {
        $Failures.Add("$Label contains duplicate value $($_.Name)")
    }
}

function Normalize-BoneName {
    param([Parameter(Mandatory = $true)][string] $Value)

    return $Value.ToLowerInvariant() -replace '[^a-z0-9]', ''
}

function Get-BoneMatchScore {
    param(
        [Parameter(Mandatory = $true)][string] $BoneName,
        [Parameter(Mandatory = $true)][string[]] $Aliases
    )

    $score = 0
    foreach ($alias in $Aliases) {
        $normalizedAlias = Normalize-BoneName $alias
        if ([string]::IsNullOrWhiteSpace($normalizedAlias)) {
            continue
        }

        if ($BoneName -eq $normalizedAlias) {
            $score = [Math]::Max($score, 120)
        }
        elseif ($BoneName -eq "def$normalizedAlias") {
            $score = [Math]::Max($score, 115)
        }
        elseif ($BoneName.EndsWith($normalizedAlias, [System.StringComparison]::Ordinal)) {
            $score = [Math]::Max($score, 80)
        }
        elseif ($BoneName.Contains($normalizedAlias)) {
            $score = [Math]::Max($score, 35)
        }
    }

    if ($score -eq 0) {
        return 0
    }

    if ($BoneName.StartsWith("def", [System.StringComparison]::Ordinal)) {
        $score += 40
    }
    elseif ($BoneName.StartsWith("org", [System.StringComparison]::Ordinal)) {
        $score += 10
    }
    elseif ($BoneName.StartsWith("mch", [System.StringComparison]::Ordinal) -or
        $BoneName.StartsWith("vis", [System.StringComparison]::Ordinal)) {
        $score -= 20
    }

    return $score
}

function Find-BestBoneMatch {
    param(
        [Parameter(Mandatory = $true)][string[]] $BoneNames,
        [Parameter(Mandatory = $true)][string[]] $Aliases
    )

    $bestName = $null
    $bestScore = 0
    foreach ($boneName in $BoneNames) {
        $score = Get-BoneMatchScore (Normalize-BoneName $boneName) $Aliases
        if ($score -gt $bestScore) {
            $bestName = $boneName
            $bestScore = $score
        }
    }

    if ($null -eq $bestName) {
        return $null
    }

    return [pscustomobject]@{
        Name = $bestName
        Score = $bestScore
    }
}

function Read-GlbNodeNames {
    param([Parameter(Mandatory = $true)][string] $Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = [System.IO.BinaryReader]::new($stream)
        $magic = $reader.ReadUInt32()
        if ($magic -ne 0x46546C67) {
            throw "File is not a binary glTF/GLB asset: $Path"
        }

        [void]$reader.ReadUInt32()
        [void]$reader.ReadUInt32()
        $json = $null
        while ($stream.Position -lt $stream.Length) {
            $length = [int]$reader.ReadUInt32()
            $type = $reader.ReadUInt32()
            $bytes = $reader.ReadBytes($length)
            if ($type -eq 0x4E4F534A) {
                $json = [System.Text.Encoding]::UTF8.GetString($bytes).Trim([char]0, ' ', "`r", "`n", "`t")
                break
            }
        }

        if ($null -eq $json) {
            throw "GLB JSON chunk not found: $Path"
        }

        $document = $json | ConvertFrom-Json
        return @($document.nodes |
            Where-Object { $null -ne $_.name -and -not [string]::IsNullOrWhiteSpace([string]$_.name) } |
            ForEach-Object { [string]$_.name })
    }
    finally {
        $stream.Dispose()
    }
}

function Add-TauriStageSceneRetargeterFailures {
    param(
        [Parameter(Mandatory = $true)][string] $AppRootPath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]] $Failures
    )

    $stageScenePath = Join-Path $AppRootPath "src\components\StageScene.tsx"
    if (-not (Test-Path -LiteralPath $stageScenePath)) {
        $Failures.Add("Tauri StageScene retargeter source not found: $stageScenePath")
        return
    }

    $text = Get-Content -LiteralPath $stageScenePath -Raw
    $requiredPatterns = @(
        [pscustomobject]@{ Label = "debug segment Head->Nose"; Pattern = '\["Head",\s*"Nose"\]' },
        [pscustomobject]@{ Label = "debug segment WristLeft->HandLeft"; Pattern = '\["WristLeft",\s*"HandLeft"\]' },
        [pscustomobject]@{ Label = "debug segment WristRight->HandRight"; Pattern = '\["WristRight",\s*"HandRight"\]' },
        [pscustomobject]@{ Label = "debug segment AnkleLeft->FootLeft"; Pattern = '\["AnkleLeft",\s*"FootLeft"\]' },
        [pscustomobject]@{ Label = "debug segment AnkleRight->FootRight"; Pattern = '\["AnkleRight",\s*"FootRight"\]' },
        [pscustomobject]@{ Label = "Nose bone alias"; Pattern = 'Nose:\s*\[.*"defnose".*"orgnose".*"nose".*\]' },
        [pscustomobject]@{ Label = "HandLeft target bone aliases"; Pattern = 'HandLeft:\s*\[.*"lefthandend".*"defpalm01l".*\]' },
        [pscustomobject]@{ Label = "HandRight target bone aliases"; Pattern = 'HandRight:\s*\[.*"righthandend".*"defpalm01r".*\]' },
        [pscustomobject]@{ Label = "Head->Nose retarget segment"; Pattern = 'boneJoint:\s*"Head",\s*fromJoint:\s*"Head",\s*toJoint:\s*"Nose"' },
        [pscustomobject]@{ Label = "Head->Nose confidence gate"; Pattern = 'minConfidence:\s*0\.65' }
    )

    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $requiredPatterns) {
        if ($text -notmatch $item.Pattern) {
            $missing.Add([string]$item.Label)
        }
    }

    if ($missing.Count -gt 0) {
        $Failures.Add("Tauri StageScene retargeter is missing: $($missing -join ', ').")
    }
    else {
        Write-Host "[config] Tauri StageScene retargeter contract matched head/nose, hand, and foot segments"
    }
}

function Add-AvatarRigFailures {
    param(
        [Parameter(Mandatory = $true)][string] $AvatarId,
        [Parameter(Mandatory = $true)][string] $ModelPath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]] $Failures
    )

    $extension = [System.IO.Path]::GetExtension($ModelPath)
    if (-not $extension.Equals(".glb", [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "[warn] Tauri avatar $AvatarId rig preflight skipped for non-GLB model: $ModelPath"
        return
    }

    $aliases = @{
        Pelvis = @("hips", "pelvis")
        SpineNavel = @("spine")
        SpineChest = @("defspine003", "defspine004", "spine003", "spine004", "chest", "upperchest", "spine1", "spine2")
        Neck = @("neck")
        Head = @("head")
        ClavicleLeft = @("leftshoulder", "shoulderl", "clavicleleft", "claviclel")
        ShoulderLeft = @("leftupperarm", "upperarml", "lupperarm", "shoulderleft")
        ElbowLeft = @("leftforearm", "leftlowerarm", "forearml", "lowerarml", "llowerarm", "elbowleft")
        WristLeft = @("lefthand", "handl", "lhand", "wristleft")
        HandLeft = @("lefthandend", "handlend", "handfklend", "handtweaklend", "defpalm01l", "defpalm02l", "defpalm03l", "defpalm04l")
        ClavicleRight = @("rightshoulder", "shoulderr", "clavicleright", "clavicler")
        ShoulderRight = @("rightupperarm", "upperarmr", "rupperarm", "shoulderright")
        ElbowRight = @("rightforearm", "rightlowerarm", "forearmr", "lowerarmr", "rlowerarm", "elbowright")
        WristRight = @("righthand", "handr", "rhand", "wristright")
        HandRight = @("righthandend", "handrend", "handfkrend", "handtweakrend", "defpalm01r", "defpalm02r", "defpalm03r", "defpalm04r")
        HipLeft = @("leftupperleg", "leftupleg", "lupperleg", "leftthigh", "thighl", "hipleft")
        KneeLeft = @("leftlowerleg", "leftleg", "leftcalf", "lowerlegl", "llowerleg", "shinl", "calfl", "kneeleft")
        AnkleLeft = @("leftfoot", "footl", "lfoot", "ankleleft")
        FootLeft = @("deftoel", "toefkl", "toeikl", "orgtoel", "lefttoes", "toesl", "ltoes", "toel", "footleft")
        HipRight = @("rightupperleg", "rightupleg", "rupperleg", "rightthigh", "thighr", "hipright")
        KneeRight = @("rightlowerleg", "rightleg", "rightcalf", "lowerlegr", "rlowerleg", "shinr", "calfr", "kneeright")
        AnkleRight = @("rightfoot", "footr", "rfoot", "ankleright")
        FootRight = @("deftoer", "toefkr", "toeikr", "orgtoer", "righttoes", "toesr", "rtoes", "toer", "footright")
    }
    $requiredJoints = @(
        "Pelvis", "SpineNavel", "SpineChest", "Neck", "Head",
        "ClavicleLeft", "ShoulderLeft", "ElbowLeft", "WristLeft", "HandLeft",
        "ClavicleRight", "ShoulderRight", "ElbowRight", "WristRight", "HandRight",
        "HipLeft", "KneeLeft", "AnkleLeft", "FootLeft",
        "HipRight", "KneeRight", "AnkleRight", "FootRight"
    )

    try {
        $boneNames = @(Read-GlbNodeNames $ModelPath)
    }
    catch {
        $Failures.Add("Tauri avatar $AvatarId GLB rig preflight failed: $_")
        return
    }

    if ($boneNames.Count -eq 0) {
        $Failures.Add("Tauri avatar $AvatarId model contains no named GLB nodes: $ModelPath")
        return
    }

    $missing = [System.Collections.Generic.List[string]]::new()
    $matches = [System.Collections.Generic.List[string]]::new()
    foreach ($joint in $requiredJoints) {
        $match = Find-BestBoneMatch $boneNames ([string[]]$aliases[$joint])
        if ($null -eq $match) {
            $missing.Add($joint)
        }
        else {
            $matches.Add("$joint=$($match.Name)")
        }
    }

    if ($missing.Count -gt 0) {
        $Failures.Add(
            "Tauri avatar $AvatarId model is missing retarget bone matches for: " +
            ($missing -join ", "))
    }
    else {
        Write-Host "[config] Tauri avatar $AvatarId retarget rig matched $($matches.Count) joints"
    }
}

function Assert-TauriAppConfigCompatibility {
    param(
        [Parameter(Mandatory = $true)][string] $DsccConfigPath,
        [Parameter(Mandatory = $true)][string] $AppRootPath,
        [Parameter(Mandatory = $true)][string] $AppConfigPath
    )

    $appConfig = Get-Content -LiteralPath $AppConfigPath -Raw | ConvertFrom-Json
    $dsccStationIds = @(Get-EnabledDsccStationIds $DsccConfigPath)
    $appStationIds = @(Get-AppStationIds $appConfig)
    $failures = [System.Collections.Generic.List[string]]::new()

    Add-DuplicateValueFailures "DSCC enabled stations" $dsccStationIds $failures
    Add-DuplicateValueFailures "Tauri app stations" $appStationIds $failures
    Add-TauriStageSceneRetargeterFailures $AppRootPath $failures

    Write-Host "[config] DSCC enabled stations=$($dsccStationIds -join ','); Tauri stations=$($appStationIds -join ',')"
    if (($dsccStationIds -join ",") -ne ($appStationIds -join ",")) {
        $failures.Add("Tauri app station ids must match DSCC enabled station ids")
    }

    if ($null -eq $appConfig.avatars) {
        $failures.Add("Tauri app config does not contain avatars")
    }
    else {
        $avatarIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($avatar in @($appConfig.avatars)) {
            if ([string]::IsNullOrWhiteSpace([string]$avatar.id)) {
                $failures.Add("Tauri app avatar has an empty id")
                continue
            }

            [void]$avatarIds.Add([string]$avatar.id)
        }

        foreach ($station in @($appConfig.stations)) {
            $stationId = [int]$station.stationId
            $avatarId = [string]$station.avatarId
            if ([string]::IsNullOrWhiteSpace($avatarId)) {
                $failures.Add("Tauri station $stationId has no avatarId")
            }
            elseif (-not $avatarIds.Contains($avatarId)) {
                $failures.Add("Tauri station $stationId references missing avatarId $avatarId")
            }
        }

        $publicRoot = Join-Path $AppRootPath "public"
        foreach ($avatar in @($appConfig.avatars)) {
            $avatarId = [string]$avatar.id
            $model = [string]$avatar.model
            if ([string]::IsNullOrWhiteSpace($model)) {
                $failures.Add("Tauri avatar $avatarId has no model path")
                continue
            }

            if ($model.StartsWith("http://", [System.StringComparison]::OrdinalIgnoreCase) -or
                $model.StartsWith("https://", [System.StringComparison]::OrdinalIgnoreCase) -or
                $model.StartsWith("data:", [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $relativeModel = if ($model.StartsWith("/", [System.StringComparison]::Ordinal) -or
                    $model.StartsWith("\", [System.StringComparison]::Ordinal)) {
                $model.Substring(1)
            }
            else {
                $model
            }
            $modelPath = Join-Path $publicRoot $relativeModel
            if (-not (Test-Path -LiteralPath $modelPath)) {
                $failures.Add("Tauri avatar $avatarId model file not found: $modelPath")
            }
            else {
                Add-AvatarRigFailures $avatarId $modelPath $failures
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Tauri app config compatibility failed:`n - $($failures -join "`n - ")"
    }
}

$portWasSpecified = $Port -gt 0

$resolvedConfigPath = Resolve-RepoPath $ConfigPath
if (-not (Test-Path -LiteralPath $resolvedConfigPath)) {
    throw "Config file not found: $resolvedConfigPath"
}

$resolvedAppRoot = if ([System.IO.Path]::IsPathRooted($AppRoot)) { $AppRoot } else { Resolve-RepoPath $AppRoot }
$resolvedAppConfigPath = Resolve-PathFromBase $resolvedAppRoot $AppConfigPath

if (-not $portWasSpecified) {
    $Port = if ($Mode -eq "Offline") { 55128 } elseif ($Mode -eq "App") { 0 } else { Get-SkeletonPortFromConfig $resolvedConfigPath }
}

if ($ReceiverSocketTimeoutSeconds -lt 0) {
    throw "ReceiverSocketTimeoutSeconds must be greater than or equal to zero"
}

if ($MotionThresholdMeters -le 0) {
    throw "MotionThresholdMeters must be greater than zero"
}

if ($ForwardToApp -and $Mode -notin @("Config", "Live", "App")) {
    throw "-ForwardToApp is only valid with -Mode Config, -Mode Live, or -Mode App."
}

$singleBodyArgs = if ($AllowExtraVisibleBodies) { @() } else { @("--max-active-body-count", "1") }
$coreJointConfidenceArgs = @(
    "--required-active-joints",
    "Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight",
    "--min-required-active-joint-confidence-ratio",
    "0.8"
)
$motionDrillArgs = if ($RequireMotionDrill) {
    @("--min-active-joint-motion-m", ([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.###}", $MotionThresholdMeters)), "--motion-joints", "Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight")
}
else {
    @()
}

if (-not $NoBuild) {
    Invoke-ExternalStep "Build UDP probe" {
        dotnet build .\tools\DsccUdpProbe\DsccUdpProbe.csproj --no-restore -p:UseSharedCompilation=false
    }

    if ($Mode -in @("Config", "Live")) {
        Invoke-ExternalStep "Build device list" {
            dotnet build .\tools\DsccDeviceList\DsccDeviceList.csproj --no-restore -p:UseSharedCompilation=false
        }
    }

    if ($Mode -in @("Offline", "App")) {
        Invoke-ExternalStep "Build fake sender" {
            dotnet build .\tools\DsccFakeSender\DsccFakeSender.csproj --no-restore -p:UseSharedCompilation=false
        }
    }
}

switch ($Mode) {
    "Config" {
        Invoke-ReadinessPreflight
    }

    "Live" {
        Invoke-ReadinessPreflight

        Invoke-ExternalStep "Connected Femto Mega serial check" {
            dotnet run --project .\tools\DsccDeviceList --no-build -- --field --config $resolvedConfigPath
        }

        $forwardArgs = @()
        if ($ForwardToApp) {
            $appReceiverPort = Get-AppSkeletonPortFromConfig $resolvedAppConfigPath
            Invoke-ExternalStep "Tauri app receiver target" {
                Assert-TauriAppReceiverReady $appReceiverPort
            }

            $forwardArgs = @("--forward-to", "127.0.0.1:$appReceiverPort")
        }

        if ($RequireMotionDrill) {
            Write-Host "[motion] requiring pelvis-relative motion >= $MotionThresholdMeters m for Head, Nose, hands, knees, ankles, and feet"
        }

        Invoke-ExternalStep "Live four-station UDP gate" {
            dotnet run --project .\tools\DsccUdpProbe --no-build -- $Port $DurationSeconds --field-strict $singleBodyArgs $coreJointConfidenceArgs $motionDrillArgs --expect-stations all --expect-serials-from-config $resolvedConfigPath $forwardArgs
        }
    }

    "App" {
        if (-not (Test-Path -LiteralPath $resolvedAppConfigPath)) {
            throw "Tauri app config not found: $resolvedAppConfigPath"
        }

        if ($RequireAppProcess -and $AllowMissingAppProcess) {
            throw "Use only one of -RequireAppProcess or -AllowMissingAppProcess."
        }
        if ($AllowMissingReceiverSocket -and $AllowMissingAppProcess) {
            Write-Host "[warn] -AllowMissingReceiverSocket is redundant when -AllowMissingAppProcess is supplied."
        }

        $appProcessRequired = $RequireAppProcess -or (-not $AllowMissingAppProcess)
        if (-not $appProcessRequired) {
            Write-Host "[warn] App mode will not require a running Tauri process because -AllowMissingAppProcess was supplied."
        }

        if (-not $portWasSpecified -and -not $ForwardToApp) {
            Invoke-ExternalStep "DSCC to Tauri UDP port compatibility" {
                Assert-DsccAppPortCompatibility $resolvedConfigPath $resolvedAppConfigPath
            }
        }

        Invoke-ExternalStep "Tauri app station and avatar config compatibility" {
            Assert-TauriAppConfigCompatibility $resolvedConfigPath $resolvedAppRoot $resolvedAppConfigPath
        }

        Invoke-ExternalStep "Tauri app four-station fake skeleton smoke" {
            if ($ForwardToApp -and $appProcessRequired -and $Port -gt 0) {
                if ($AllowMissingReceiverSocket) {
                    & .\tools\Run-TauriAppSmoke.ps1 -AppRoot $AppRoot -AppConfigPath $AppConfigPath -DurationSeconds $DurationSeconds -Port $Port -ReceiverSocketTimeoutSeconds $ReceiverSocketTimeoutSeconds -RequireAppProcess -AllowMissingReceiverSocket -ViaProbe -NoBuild
                }
                else {
                    & .\tools\Run-TauriAppSmoke.ps1 -AppRoot $AppRoot -AppConfigPath $AppConfigPath -DurationSeconds $DurationSeconds -Port $Port -ReceiverSocketTimeoutSeconds $ReceiverSocketTimeoutSeconds -RequireAppProcess -ViaProbe -NoBuild
                }
            }
            elseif ($ForwardToApp -and $appProcessRequired) {
                if ($AllowMissingReceiverSocket) {
                    & .\tools\Run-TauriAppSmoke.ps1 -AppRoot $AppRoot -AppConfigPath $AppConfigPath -DurationSeconds $DurationSeconds -ReceiverSocketTimeoutSeconds $ReceiverSocketTimeoutSeconds -RequireAppProcess -AllowMissingReceiverSocket -ViaProbe -NoBuild
                }
                else {
                    & .\tools\Run-TauriAppSmoke.ps1 -AppRoot $AppRoot -AppConfigPath $AppConfigPath -DurationSeconds $DurationSeconds -ReceiverSocketTimeoutSeconds $ReceiverSocketTimeoutSeconds -RequireAppProcess -ViaProbe -NoBuild
                }
            }
            elseif ($ForwardToApp -and $Port -gt 0) {
                & .\tools\Run-TauriAppSmoke.ps1 -AppRoot $AppRoot -AppConfigPath $AppConfigPath -DurationSeconds $DurationSeconds -Port $Port -ViaProbe -NoBuild
            }
            elseif ($ForwardToApp) {
                & .\tools\Run-TauriAppSmoke.ps1 -AppRoot $AppRoot -AppConfigPath $AppConfigPath -DurationSeconds $DurationSeconds -ViaProbe -NoBuild
            }
            elseif ($appProcessRequired -and $Port -gt 0) {
                if ($AllowMissingReceiverSocket) {
                    & .\tools\Run-TauriAppSmoke.ps1 -AppRoot $AppRoot -AppConfigPath $AppConfigPath -DurationSeconds $DurationSeconds -Port $Port -ReceiverSocketTimeoutSeconds $ReceiverSocketTimeoutSeconds -RequireAppProcess -AllowMissingReceiverSocket -NoBuild
                }
                else {
                    & .\tools\Run-TauriAppSmoke.ps1 -AppRoot $AppRoot -AppConfigPath $AppConfigPath -DurationSeconds $DurationSeconds -Port $Port -ReceiverSocketTimeoutSeconds $ReceiverSocketTimeoutSeconds -RequireAppProcess -NoBuild
                }
            }
            elseif ($appProcessRequired) {
                if ($AllowMissingReceiverSocket) {
                    & .\tools\Run-TauriAppSmoke.ps1 -AppRoot $AppRoot -AppConfigPath $AppConfigPath -DurationSeconds $DurationSeconds -ReceiverSocketTimeoutSeconds $ReceiverSocketTimeoutSeconds -RequireAppProcess -AllowMissingReceiverSocket -NoBuild
                }
                else {
                    & .\tools\Run-TauriAppSmoke.ps1 -AppRoot $AppRoot -AppConfigPath $AppConfigPath -DurationSeconds $DurationSeconds -ReceiverSocketTimeoutSeconds $ReceiverSocketTimeoutSeconds -RequireAppProcess -NoBuild
                }
            }
            elseif ($Port -gt 0) {
                & .\tools\Run-TauriAppSmoke.ps1 -AppRoot $AppRoot -AppConfigPath $AppConfigPath -DurationSeconds $DurationSeconds -Port $Port -NoBuild
            }
            else {
                & .\tools\Run-TauriAppSmoke.ps1 -AppRoot $AppRoot -AppConfigPath $AppConfigPath -DurationSeconds $DurationSeconds -NoBuild
            }
        }
    }

    "Offline" {
        $sender = Start-Job -ScriptBlock {
            param($Root, $TargetPort, $ActiveSeconds)

            Set-Location $Root
            dotnet run --project .\tools\DsccFakeSender --no-build -- 127.0.0.1 $TargetPort all $ActiveSeconds 'FAKE-SENDER-{0:000}'
        } -ArgumentList $repoRoot, $Port, $OfflineActiveSeconds

        Start-Sleep -Seconds 1
        try {
            Invoke-ExternalStep "Offline four-station UDP loopback" {
                dotnet run --project .\tools\DsccUdpProbe --no-build -- $Port $OfflineDurationSeconds --field-strict $singleBodyArgs $coreJointConfidenceArgs --expect-device-type FakeSender --expect-stations all --expect-serials 1=FAKE-SENDER-001,2=FAKE-SENDER-002,3=FAKE-SENDER-003,4=FAKE-SENDER-004 --min-player-ratio 0.5 --min-active-ratio 0.5 --min-active-confidence 0.8 --min-active-joint-confidence-ratio 0.9 --min-active-joint-motion-m 0.05 --motion-joints Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight --max-player-gap-ms 3000 --max-active-gap-ms 3000
            }
        }
        finally {
            Stop-Job $sender -ErrorAction SilentlyContinue
            Write-Host ""
            Write-Host "== Fake sender sample"
            Receive-Job $sender -ErrorAction SilentlyContinue | Select-Object -First 20
            Remove-Job $sender -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host ""
Write-Host "[pass] field smoke mode '$Mode' completed"
