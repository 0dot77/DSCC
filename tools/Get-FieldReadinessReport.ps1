[CmdletBinding()]
param(
    [string] $ConfigPath = "config\wall-a.local.json",

    [string] $AppRoot = "C:\Users\o77do\Developer\theme-toon-dance",

    [string] $AppConfigPath = "config\show.local.json",

    [int] $TeePort = 55130,

    [switch] $CheckDevices,

    [switch] $RequireAppProcess,

    [switch] $Strict
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

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "JSON file not found: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-JsonProperty {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        $DefaultValue = $null
    )

    if ($null -eq $Object -or $null -eq $Object.PSObject.Properties[$Name]) {
        return $DefaultValue
    }

    return $Object.$Name
}

function Get-NestedString {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $ObjectName,
        [Parameter(Mandatory = $true)][string] $PropertyName
    )

    $nested = Get-JsonProperty $Object $ObjectName
    if ($null -eq $nested) {
        return ""
    }

    $value = Get-JsonProperty $nested $PropertyName ""
    if ($null -eq $value) {
        return ""
    }

    return [string]$value
}

function Get-AppSkeletonPortFromConfig {
    param([Parameter(Mandatory = $true)] $Config)

    $receiver = Get-JsonProperty $Config "receiver"
    if ($null -ne $receiver) {
        $receiverPort = Get-JsonProperty $receiver "port"
        if ($null -ne $receiverPort) {
            return [int]$receiverPort
        }
    }

    $tracker = Get-JsonProperty $Config "tracker"
    if ($null -ne $tracker) {
        $trackerPort = Get-JsonProperty $tracker "port"
        if ($null -ne $trackerPort) {
            return [int]$trackerPort
        }
    }

    return 55010
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

function Add-Failure {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]] $Failures,
        [Parameter(Mandatory = $true)][string] $Message
    )

    $Failures.Add($Message)
    Write-Host "[fail] $Message"
}

function Write-Pass {
    param([Parameter(Mandatory = $true)][string] $Message)

    Write-Host "[ok] $Message"
}

function Write-Warn {
    param([Parameter(Mandatory = $true)][string] $Message)

    Write-Host "[warn] $Message"
}

function Invoke-ReportCommand {
    param(
        [Parameter(Mandatory = $true)][string] $Label,
        [Parameter(Mandatory = $true)][scriptblock] $Command,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]] $Failures
    )

    Write-Host ""
    Write-Host "== $Label"
    $global:LASTEXITCODE = 0
    $output = & $Command 2>&1
    foreach ($line in $output) {
        Write-Host $line
    }

    if ($LASTEXITCODE -ne 0) {
        Add-Failure $Failures "$Label failed with exit code $LASTEXITCODE."
        return
    }

    Write-Pass "$Label passed"
}

function Get-EnabledStationIds {
    param([Parameter(Mandatory = $true)] $Config)

    $stations = @(Get-JsonProperty $Config "stations" @())
    return @($stations |
        Where-Object { [bool](Get-JsonProperty $_ "enabled" $true) } |
        ForEach-Object { [int](Get-JsonProperty $_ "stationId" 0) } |
        Sort-Object)
}

function Get-AppStationIds {
    param([Parameter(Mandatory = $true)] $Config)

    $stations = @(Get-JsonProperty $Config "stations" @())
    return @($stations |
        ForEach-Object { [int](Get-JsonProperty $_ "stationId" 0) } |
        Sort-Object)
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
        while ($stream.Position -lt $stream.Length) {
            $length = [int]$reader.ReadUInt32()
            $type = $reader.ReadUInt32()
            $bytes = $reader.ReadBytes($length)
            if ($type -ne 0x4E4F534A) {
                continue
            }

            $json = [System.Text.Encoding]::UTF8.GetString($bytes).Trim([char]0, ' ', "`r", "`n", "`t")
            $document = $json | ConvertFrom-Json
            return @($document.nodes |
                Where-Object { $null -ne $_.name -and -not [string]::IsNullOrWhiteSpace([string]$_.name) } |
                ForEach-Object { [string]$_.name })
        }

        throw "GLB JSON chunk not found: $Path"
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
        Add-Failure $Failures "Tauri StageScene retargeter source not found: $stageScenePath"
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
        Add-Failure $Failures "Tauri StageScene retargeter is missing: $($missing -join ', ')."
    }
    else {
        Write-Pass "Tauri StageScene retargeter contract matched head/nose, hand, and foot segments"
    }
}

function Add-FieldAcceptanceWrapperFailures {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]] $Failures
    )

    $acceptancePath = Join-Path $repoRoot "tools\Run-FieldAcceptance.ps1"
    if (-not (Test-Path -LiteralPath $acceptancePath)) {
        Add-Failure $Failures "Field acceptance wrapper not found: $acceptancePath"
        return
    }

    $text = Get-Content -LiteralPath $acceptancePath -Raw
    $requiredPatterns = @(
        [pscustomobject]@{ Label = "live app-fed motion drill step"; Pattern = 'Live app-fed motion drill' },
        [pscustomobject]@{ Label = "live mode"; Pattern = '"-Mode",\s*"Live"' },
        [pscustomobject]@{ Label = "forward to app"; Pattern = '"-ForwardToApp"' },
        [pscustomobject]@{ Label = "required motion drill"; Pattern = '"-RequireMotionDrill"' },
        [pscustomobject]@{ Label = "motion threshold argument"; Pattern = '"-MotionThresholdMeters"' },
        [pscustomobject]@{ Label = "success marker after live drill"; Pattern = '\[pass\] field acceptance completed' },
        [pscustomobject]@{ Label = "direct route restore after success"; Pattern = 'Restore direct DSCC-to-Tauri routing after success' },
        [pscustomobject]@{ Label = "direct route restore after failure option"; Pattern = 'RestoreDirectOnFailure' },
        [pscustomobject]@{ Label = "keep tee after failure explicit override"; Pattern = 'KeepTeeAfterFailure' },
        [pscustomobject]@{ Label = "direct route restore after failure default"; Pattern = 'Should-RestoreDirectAfterFailure' },
        [pscustomobject]@{ Label = "direct restore mode"; Pattern = '"-Mode",\s*"Direct"' }
    )

    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $requiredPatterns) {
        if ($text -notmatch $item.Pattern) {
            $missing.Add([string]$item.Label)
        }
    }

    if ($missing.Count -gt 0) {
        Add-Failure $Failures "Run-FieldAcceptance.ps1 is missing final handoff contract piece(s): $($missing -join ', ')."
    }
    else {
        Write-Pass "Run-FieldAcceptance final handoff requires app-fed live motion drill and direct-route restore"
    }
}

function Add-DsccLiveStartupPinningFailures {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]] $Failures
    )

    $viewModelPath = Join-Path $repoRoot "src\DSCC.App.Wpf\ViewModels\MainWindowViewModel.cs"
    if (-not (Test-Path -LiteralPath $viewModelPath)) {
        Add-Failure $Failures "DSCC WPF live startup source not found: $viewModelPath"
        return
    }

    $text = Get-Content -LiteralPath $viewModelPath -Raw
    $requiredPatterns = @(
        [pscustomobject]@{ Label = "live station pin validation policy"; Pattern = 'OrbbecLiveStationPinPolicy\.ValidateRequiredPins' },
        [pscustomobject]@{ Label = "live start validates pins"; Pattern = 'ValidateLiveStationPins\(\)' },
        [pscustomobject]@{ Label = "auto assign is opt-in"; Pattern = '_config\.AutoAssignDevicesOnStart' },
        [pscustomobject]@{ Label = "body tracker uses pinned serial"; Pattern = 'CameraSerial\s*=\s*runtime\.Station\.AssignedCameraSerial' },
        [pscustomobject]@{ Label = "K4A body tracker factory"; Pattern = 'K4aBodyTrackingSkeletonSourceFactory\.Create' }
    )

    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $requiredPatterns) {
        if ($text -notmatch $item.Pattern) {
            $missing.Add([string]$item.Label)
        }
    }

    $pinValidationIndex = $text.IndexOf("ValidateLiveStationPins()", [System.StringComparison]::Ordinal)
    $trackerCreateIndex = $text.IndexOf("K4aBodyTrackingSkeletonSourceFactory.Create", [System.StringComparison]::Ordinal)
    if ($pinValidationIndex -lt 0 -or $trackerCreateIndex -lt 0 -or $pinValidationIndex -gt $trackerCreateIndex) {
        $missing.Add("pin validation before K4A tracker creation")
    }

    if ($missing.Count -gt 0) {
        Add-Failure $Failures "DSCC WPF live startup is missing camera pinning contract piece(s): $($missing -join ', ')."
    }
    else {
        Write-Pass "DSCC WPF live startup validates pinned station serials before creating K4A trackers"
    }
}

function Add-CoreAvatarRigFailures {
    param(
        [Parameter(Mandatory = $true)][string] $AvatarId,
        [Parameter(Mandatory = $true)][string] $ModelPath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]] $Failures
    )

    $extension = [System.IO.Path]::GetExtension($ModelPath)
    if (-not $extension.Equals(".glb", [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Warn "avatar $AvatarId core rig check skipped for non-GLB model: $ModelPath"
        return
    }

    $aliases = @{
        Pelvis = @("hips", "pelvis")
        Head = @("head")
        WristLeft = @("lefthand", "handl", "lhand", "wristleft")
        HandLeft = @("lefthandend", "handlend", "handfklend", "handtweaklend", "defpalm01l", "defpalm02l", "defpalm03l", "defpalm04l")
        WristRight = @("righthand", "handr", "rhand", "wristright")
        HandRight = @("righthandend", "handrend", "handfkrend", "handtweakrend", "defpalm01r", "defpalm02r", "defpalm03r", "defpalm04r")
        KneeLeft = @("leftlowerleg", "leftleg", "leftcalf", "lowerlegl", "llowerleg", "shinl", "calfl", "kneeleft")
        KneeRight = @("rightlowerleg", "rightleg", "rightcalf", "lowerlegr", "rlowerleg", "shinr", "calfr", "kneeright")
        AnkleLeft = @("leftfoot", "footl", "lfoot", "ankleleft")
        AnkleRight = @("rightfoot", "footr", "rfoot", "ankleright")
        FootLeft = @("deftoel", "toefkl", "toeikl", "orgtoel", "lefttoes", "toesl", "ltoes", "footleft")
        FootRight = @("deftoer", "toefkr", "toeikr", "orgtoer", "righttoes", "toesr", "rtoes", "footright")
    }
    $required = @("Pelvis", "Head", "WristLeft", "HandLeft", "WristRight", "HandRight", "KneeLeft", "KneeRight", "AnkleLeft", "AnkleRight", "FootLeft", "FootRight")

    try {
        $boneNames = @(Read-GlbNodeNames $ModelPath)
    }
    catch {
        Add-Failure $Failures "avatar $AvatarId core rig check failed: $_"
        return
    }

    if ($boneNames.Count -eq 0) {
        Add-Failure $Failures "avatar $AvatarId contains no named GLB nodes: $ModelPath"
        return
    }

    $missing = [System.Collections.Generic.List[string]]::new()
    $matches = [System.Collections.Generic.List[string]]::new()
    foreach ($jointName in $required) {
        $match = Find-BestBoneMatch $boneNames ([string[]]$aliases[$jointName])
        if ($null -eq $match) {
            $missing.Add($jointName)
        }
        else {
            $matches.Add("$jointName=$($match.Name)")
        }
    }

    if ($missing.Count -gt 0) {
        Add-Failure $Failures "avatar $AvatarId is missing core retarget bones: $($missing -join ',')."
        return
    }

    Write-Pass "avatar $AvatarId core retarget bones: $($matches -join '; ')"
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

$dsccConfig = Read-JsonFile $resolvedConfigPath
$appConfig = Read-JsonFile $resolvedAppConfigPath
$failures = [System.Collections.Generic.List[string]]::new()
$essentialMotionJoints = "Head,Nose,HandLeft,HandRight,KneeLeft,KneeRight,AnkleLeft,AnkleRight,FootLeft,FootRight"
$motionThresholdMeters = "0.05"
$appProcessRequired = $RequireAppProcess -or $Strict

Write-Host "== DSCC Field Readiness Report"
Write-Host "DSCC config: $resolvedConfigPath"
Write-Host "Tauri config: $resolvedAppConfigPath"
Write-Host ""

$unity = Get-JsonProperty $dsccConfig "unity" ([pscustomobject]@{})
$dsccPort = [int](Get-JsonProperty $unity "skeletonPort" 55010)
$appPort = Get-AppSkeletonPortFromConfig $appConfig
$portMode = if ($dsccPort -eq $appPort) {
    "Direct"
}
elseif ($dsccPort -eq $TeePort) {
    "Tee"
}
else {
    "Custom"
}

Write-Host "== UDP routing"
Write-Host "[config] DSCC unity.skeletonPort=$dsccPort; Tauri receiver.port=$appPort; teePort=$TeePort; mode=$portMode"
if ($portMode -eq "Custom") {
    Write-Warn "DSCC and Tauri ports differ, but DSCC port does not match the expected tee port. Pass -TeePort $dsccPort or run Set-FieldSkeletonPort."
}
elseif ($portMode -eq "Direct") {
    Write-Pass "Direct DSCC-to-Tauri UDP routing is configured."
}
else {
    Write-Pass "Tee routing is configured for probe input port $TeePort."
}

Write-Host ""
Write-Host "== Field stations"
$autoAssign = [bool](Get-JsonProperty $dsccConfig "autoAssignDevicesOnStart" $true)
if ($autoAssign) {
    Add-Failure $failures "autoAssignDevicesOnStart must be false for field serial pinning."
}
else {
    Write-Pass "autoAssignDevicesOnStart=false"
}

$enabledStationIds = @(Get-EnabledStationIds $dsccConfig)
if (($enabledStationIds -join ",") -ne "1,2,3,4") {
    Add-Failure $failures "Enabled DSCC station ids must be exactly 1,2,3,4; found $($enabledStationIds -join ',')."
}
else {
    Write-Pass "enabled stations=1,2,3,4"
}

$stations = @(Get-JsonProperty $dsccConfig "stations" @())
$missingSerialStations = [System.Collections.Generic.List[int]]::new()
$syncRoleFailures = [System.Collections.Generic.List[int]]::new()
foreach ($station in $stations) {
    $enabled = [bool](Get-JsonProperty $station "enabled" $true)
    if (-not $enabled) {
        continue
    }

    $stationId = [int](Get-JsonProperty $station "stationId" 0)
    $device = Get-JsonProperty $station "device" ([pscustomobject]@{})
    $calibration = Get-JsonProperty $station "calibration" ([pscustomobject]@{})
    $deviceType = [string](Get-JsonProperty $device "deviceType" "")
    $serial = Get-NestedString $station "device" "serial"
    $calibrationSerial = Get-NestedString $station "calibration" "cameraSerial"
    $effectiveSerial = if (-not [string]::IsNullOrWhiteSpace($serial)) { $serial } else { $calibrationSerial }
    $depthMode = [string](Get-JsonProperty $device "depthMode" "")
    $fps = [int](Get-JsonProperty $device "fps" 0)
    $syncRole = [string](Get-JsonProperty $device "syncRole" "")

    if ([string]::IsNullOrWhiteSpace($effectiveSerial)) {
        $missingSerialStations.Add($stationId)
    }

    $serialText = if ([string]::IsNullOrWhiteSpace($effectiveSerial)) { "<missing>" } else { $effectiveSerial }
    Write-Host "[station $stationId] type=$deviceType serial=$serialText sync=$syncRole depth=$depthMode fps=$fps"

    if ($deviceType -ne "FemtoMega") {
        Add-Failure $failures "station $stationId deviceType must be FemtoMega."
    }
    $expectedSyncRole = if ($stationId -eq 1) { "Primary" } else { "Secondary" }
    if (-not $syncRole.Equals($expectedSyncRole, [System.StringComparison]::OrdinalIgnoreCase)) {
        $syncRoleFailures.Add($stationId)
        Add-Failure $failures "station $stationId syncRole must be $expectedSyncRole for the four-Mega field rig."
    }
    if ($depthMode -ne "NFOV_UNBINNED") {
        Add-Failure $failures "station $stationId depthMode should be NFOV_UNBINNED for field body tracking."
    }
    if ($fps -gt 15 -or $fps -lt 1) {
        Add-Failure $failures "station $stationId fps must be between 1 and 15 for the current field profile."
    }
}

if ($missingSerialStations.Count -gt 0) {
    Add-Failure $failures "station serials are missing for station(s): $($missingSerialStations -join ',')."
}
else {
    Write-Pass "all enabled stations have serial pins"
}

if ($syncRoleFailures.Count -eq 0 -and ($enabledStationIds -join ",") -eq "1,2,3,4") {
    Write-Pass "field sync roles: station 1 Primary; stations 2-4 Secondary"
}

Write-Host ""
Write-Host "== DSCC live startup contract"
Add-DsccLiveStartupPinningFailures $failures

Write-Host ""
Write-Host "== Body tracking runtime config"
$bodyTracking = Get-JsonProperty $dsccConfig "bodyTracking" ([pscustomobject]@{})
$processingModes = @((Get-JsonProperty $bodyTracking "processingModes" @()) | ForEach-Object { [string]$_ })
$useLiteModel = [bool](Get-JsonProperty $bodyTracking "useLiteModel" $false)
$maxFps = [int](Get-JsonProperty $bodyTracking "maxFps" 0)
Write-Host "[config] processingModes=$($processingModes -join ',') useLiteModel=$useLiteModel maxFps=$maxFps"
if ($processingModes.Count -ne 1 -or $processingModes[0] -ne "Cuda") {
    Add-Failure $failures "bodyTracking.processingModes must be exactly Cuda."
}
else {
    Write-Pass "CUDA-only body tracking mode"
}
if (-not $useLiteModel) {
    Add-Failure $failures "bodyTracking.useLiteModel should be true for field stability."
}
else {
    Write-Pass "useLiteModel=true"
}
if ($maxFps -gt 15 -or $maxFps -lt 1) {
    Add-Failure $failures "bodyTracking.maxFps must be between 1 and 15."
}
else {
    Write-Pass "bodyTracking.maxFps=$maxFps"
}

Write-Host ""
Write-Host "== Core skeleton acceptance"
Write-Host "[motion] required app-driving joints: $essentialMotionJoints"
Write-Host "[motion] final drill threshold: >= ${motionThresholdMeters}m pelvis-relative motion per joint"
Write-Host "[body] live gate requires one Active body per station by default and zero selected-body id switches"
Write-Pass "Run-FieldSmoke -RequireMotionDrill gates head, hands, knees, ankles, and feet before field acceptance."

Write-Host ""
Write-Host "== Field handoff wrapper"
Add-FieldAcceptanceWrapperFailures $failures

Write-Host ""
Write-Host "== Tauri app config"
$appStationIds = @(Get-AppStationIds $appConfig)
Add-TauriStageSceneRetargeterFailures $resolvedAppRoot $failures
if (($enabledStationIds -join ",") -ne ($appStationIds -join ",")) {
    Add-Failure $failures "Tauri station ids must match DSCC enabled station ids; app has $($appStationIds -join ',')."
}
else {
    Write-Pass "Tauri station ids match DSCC stations"
}

$avatars = @(Get-JsonProperty $appConfig "avatars" @())
$avatarIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($avatar in $avatars) {
    $id = [string](Get-JsonProperty $avatar "id" "")
    if (-not [string]::IsNullOrWhiteSpace($id)) {
        [void]$avatarIds.Add($id)
    }
}

$publicRoot = Join-Path $resolvedAppRoot "public"
foreach ($avatar in $avatars) {
    $id = [string](Get-JsonProperty $avatar "id" "")
    $model = [string](Get-JsonProperty $avatar "model" "")
    if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($model)) {
        Add-Failure $failures "Tauri avatar has missing id or model path."
        continue
    }

    if ($model.StartsWith("/", [System.StringComparison]::Ordinal) -or $model.StartsWith("\", [System.StringComparison]::Ordinal)) {
        $model = $model.Substring(1)
    }

    $modelPath = Join-Path $publicRoot $model
    if (Test-Path -LiteralPath $modelPath) {
        Write-Pass "avatar $id model exists: $modelPath"
        Add-CoreAvatarRigFailures $id $modelPath $failures
    }
    else {
        Add-Failure $failures "avatar $id model file not found: $modelPath"
    }
}

foreach ($station in @(Get-JsonProperty $appConfig "stations" @())) {
    $stationId = [int](Get-JsonProperty $station "stationId" 0)
    $avatarId = [string](Get-JsonProperty $station "avatarId" "")
    if ([string]::IsNullOrWhiteSpace($avatarId) -or -not $avatarIds.Contains($avatarId)) {
        Add-Failure $failures "Tauri station $stationId references missing avatarId '$avatarId'."
    }
}

Write-Host ""
Write-Host "== Tauri process"
$processes = @(Find-TauriAppProcess)
if ($processes.Count -eq 0) {
    if ($appProcessRequired) {
        Add-Failure $failures "DSCC Tauri app process is not running."
    }
    else {
        Write-Warn "DSCC Tauri app process is not running."
    }
}
else {
    $processIds = @($processes | ForEach-Object { [int]$_.Id })
    foreach ($process in $processes) {
        Write-Host "[app] pid=$($process.Id) name=$($process.ProcessName) title=$($process.MainWindowTitle)"
    }

    $endpoints = @(Find-UdpReceiverEndpoint $appPort $processIds)
    if ($endpoints.Count -eq 0) {
        Add-Failure $failures "Tauri app process is running but does not own UDP receiver port $appPort."
    }
    else {
        foreach ($endpoint in $endpoints) {
            Write-Pass "Tauri receiver socket $($endpoint.LocalAddress):$($endpoint.LocalPort) pid=$($endpoint.OwningProcess)"
        }
    }
}

if ($CheckDevices) {
    Invoke-ReportCommand "K4A body tracking runtime and CUDA files" {
        dotnet run --project .\tools\DsccDeviceList --no-build -- --runtime --require-cuda
    } $failures

    if ($missingSerialStations.Count -gt 0) {
        Invoke-ReportCommand "Connected Femto Mega discovery and pin template" {
            dotnet run --project .\tools\DsccDeviceList --no-build -- --field --print-pin-command $resolvedConfigPath
        } $failures
    }
    else {
        Invoke-ReportCommand "Connected Femto Mega serial pins" {
            dotnet run --project .\tools\DsccDeviceList --no-build -- --field --config $resolvedConfigPath
        } $failures
    }
}

Write-Host ""
Write-Host "== Blocking actions"
$blockingActionCount = 0
if ($missingSerialStations.Count -gt 0) {
    $blockingActionCount++
    Write-Host "[action] Connect and label all four Femto Mega cameras in physical station order: 1=left, 2=mid-left, 3=mid-right, 4=right."
    if ($CheckDevices) {
        Write-Host "[action] Re-run the pin template only after connected device discovery shows count=4; do not pin from a partial device list."
    }
    else {
        Write-Host "[action] Run .\tools\Get-FieldReadinessReport.ps1 -CheckDevices to verify connected device count and print the pin template."
    }
    Write-Host "[action] Then write station serials with .\tools\Set-FieldStationSerials.ps1 -Station1Serial LEFT_SERIAL -Station2Serial MID_LEFT_SERIAL -Station3Serial MID_RIGHT_SERIAL -Station4Serial RIGHT_SERIAL -NoBuild"
}
if ($appProcessRequired -and $processes.Count -eq 0) {
    $blockingActionCount++
    Write-Host "[action] Start the Tauri player before strict/app acceptance so the receiver socket can be verified."
}
if ($blockingActionCount -eq 0) {
    Write-Host "[ok] No operator action inferred before the next validation command."
}

Write-Host ""
Write-Host "== Next commands"
if ($missingSerialStations.Count -gt 0) {
    Write-Host ".\tools\Set-FieldStationSerials.ps1 -PrintTemplate -NoBuild -ConfigPath $ConfigPath"
}
if (-not $CheckDevices) {
    Write-Host ".\tools\Get-FieldReadinessReport.ps1 -CheckDevices"
}
if (-not $appProcessRequired) {
    Write-Host ".\tools\Get-FieldReadinessReport.ps1 -RequireAppProcess -Strict"
}
Write-Host ".\tools\Run-FieldReplaySmoke.ps1 -NoBuild"
if ($portMode -eq "Direct") {
    Write-Host ".\tools\Run-FieldSmoke.ps1 -Mode App -DurationSeconds 15 -NoBuild"
    Write-Host ".\tools\Run-FieldSmoke.ps1 -Mode Live -RequireMotionDrill -DurationSeconds 60 -NoBuild"
    Write-Host ".\tools\Set-FieldSkeletonPort.ps1 -Mode Tee -Port $TeePort"
    Write-Host ".\tools\Run-FieldAcceptance.ps1 -ConfigureTee -PlanOnly -NoBuild"
    Write-Host ".\tools\Run-FieldAcceptance.ps1 -ConfigureTee -NoBuild"
}
else {
    Write-Host ".\tools\Run-FieldSmoke.ps1 -Mode Config -ForwardToApp -Port $dsccPort -NoBuild"
    Write-Host ".\tools\Run-FieldSmoke.ps1 -Mode Live -ForwardToApp -Port $dsccPort -DurationSeconds 60 -NoBuild"
    Write-Host ".\tools\Run-FieldSmoke.ps1 -Mode Live -ForwardToApp -RequireMotionDrill -Port $dsccPort -DurationSeconds 60 -NoBuild"
    Write-Host ".\tools\Run-FieldAcceptance.ps1 -ConfigureTee -PlanOnly -NoBuild"
    Write-Host ".\tools\Run-FieldAcceptance.ps1 -ConfigureTee -NoBuild"
}

Write-Host ""
if ($failures.Count -eq 0) {
    Write-Host "[pass] field readiness report found no blocking config issues"
}
else {
    Write-Host "[fail] field readiness report found $($failures.Count) blocking issue(s)"
    if ($Strict) {
        exit 2
    }
}
