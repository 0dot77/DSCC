param(
    [string]$ConfigPath = ".\config\wall-a.local.json",
    [string]$Station1Serial = "",
    [string]$Station2Serial = "",
    [string]$Station3Serial = "",
    [string]$Station4Serial = "",
    [string]$Mapping = "",
    [switch]$PrintTemplate,
    [switch]$DryRun,
    [string]$DryRunDirectory = "",
    [switch]$AllowUnconnected,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Resolve-ConfigPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Resolve-OutputPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Invoke-Step {
    param(
        [string]$Title,
        [scriptblock]$Block
    )

    Write-Host ""
    Write-Host "== $Title"
    & $Block
}

function Assert-ExitCode {
    param([string]$Step)

    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE"
    }
}

function Get-SerialMapping {
    if (-not [string]::IsNullOrWhiteSpace($Mapping)) {
        return $Mapping.Trim()
    }

    $serials = @($Station1Serial, $Station2Serial, $Station3Serial, $Station4Serial)
    if ($serials | Where-Object { [string]::IsNullOrWhiteSpace($_) }) {
        return ""
    }

    return "1=$($serials[0].Trim()),2=$($serials[1].Trim()),3=$($serials[2].Trim()),4=$($serials[3].Trim())"
}

if (-not $DryRun -and -not [string]::IsNullOrWhiteSpace($DryRunDirectory)) {
    throw "-DryRunDirectory is only valid with -DryRun."
}

$configCandidatePath = Resolve-ConfigPath $ConfigPath
if (-not (Test-Path -LiteralPath $configCandidatePath)) {
    throw "Config not found: $configCandidatePath"
}
$resolvedConfigPath = (Resolve-Path -LiteralPath $configCandidatePath).Path

if (-not $NoBuild) {
    Invoke-Step "Build field serial tools" {
        dotnet build .\tools\DsccDeviceList\DsccDeviceList.csproj --no-restore -p:UseSharedCompilation=false
        Assert-ExitCode "DsccDeviceList build"
        dotnet build .\tools\DsccUdpProbe\DsccUdpProbe.csproj --no-restore -p:UseSharedCompilation=false
        Assert-ExitCode "DsccUdpProbe build"
    }
}

if ($PrintTemplate -or [string]::IsNullOrWhiteSpace((Get-SerialMapping))) {
    Invoke-Step "Print connected device pinning template" {
        dotnet run --project .\tools\DsccDeviceList --no-build -- --field --print-pin-command $resolvedConfigPath
        Assert-ExitCode "pin template"
    }
    return
}

$serialMapping = Get-SerialMapping
$targetConfigPath = $resolvedConfigPath
$dryRunWorkingDirectory = ""
if ($DryRun) {
    $dryRunRoot = if ([string]::IsNullOrWhiteSpace($DryRunDirectory)) {
        $env:TEMP
    }
    else {
        Resolve-OutputPath $DryRunDirectory
    }
    $dryRunWorkingDirectory = Join-Path $dryRunRoot ("dscc-serial-pin-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $dryRunWorkingDirectory | Out-Null
    $targetConfigPath = Join-Path $dryRunWorkingDirectory ([System.IO.Path]::GetFileName($resolvedConfigPath))
    Copy-Item -LiteralPath $resolvedConfigPath -Destination $targetConfigPath
    Write-Host "[dry-run] using temporary config: $targetConfigPath"
}

$pinArgs = @("--pin-config", $targetConfigPath, "--pin-serials", $serialMapping)
if ($AllowUnconnected) {
    Write-Host "[pin] AllowUnconnected is enabled; connected device discovery is not used to verify these serials."
    $pinArgs = @("--backend", "placeholder") + $pinArgs + @("--allow-unconnected-pin")
}
else {
    $pinArgs = @("--field") + $pinArgs
}

Invoke-Step "Pin station serials" {
    dotnet run --project .\tools\DsccDeviceList --no-build -- @pinArgs
    Assert-ExitCode "serial pinning"
}

Invoke-Step "Validate pinned field config" {
    dotnet run --project .\tools\DsccUdpProbe --no-build -- --check-field-config $targetConfigPath
    Assert-ExitCode "field config validation"
}

if (-not $AllowUnconnected) {
    Invoke-Step "Verify connected devices match pinned config" {
        dotnet run --project .\tools\DsccDeviceList --no-build -- --field --config $targetConfigPath
        Assert-ExitCode "connected device validation"
    }
}

if ($DryRun) {
    Write-Host ""
    Write-Host "[dry-run] real config was not modified: $resolvedConfigPath"
    Write-Host "[dry-run] validated temporary config: $targetConfigPath"
    Write-Host "[dry-run] output directory: $dryRunWorkingDirectory"
}
else {
    Write-Host ""
    Write-Host "[pass] station serials pinned in $resolvedConfigPath"
}
