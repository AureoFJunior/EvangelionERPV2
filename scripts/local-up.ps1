param(
    [string]$EnvFile = ".env.local",
    [switch]$WithWorkers,
    [switch]$WithOrderWorker,
    [switch]$NoBuild,
    [switch]$SkipIdentityCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-EnvValue {
    param(
        [string]$Path,
        [string]$Key
    )

    if (-not (Test-Path $Path)) {
        return $null
    }

    $line = Get-Content $Path | Where-Object { $_ -match "^\s*$Key\s*=" } | Select-Object -First 1
    if (-not $line) {
        return $null
    }

    return ($line -replace "^\s*$Key\s*=\s*", "").Trim()
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$envPath = if ([System.IO.Path]::IsPathRooted($EnvFile)) { $EnvFile } else { Join-Path $repoRoot $EnvFile }

if (-not (Test-Path $envPath)) {
    throw "Env file not found: $envPath"
}

Set-Location $repoRoot

if (-not $SkipIdentityCheck) {
    $awsProfile = Get-EnvValue -Path $envPath -Key "AWS_PROFILE"
    $awsRegion = Get-EnvValue -Path $envPath -Key "AWS_REGION"

    $awsArgs = @("sts", "get-caller-identity")
    if ($awsProfile) { $awsArgs += @("--profile", $awsProfile) }
    if ($awsRegion) { $awsArgs += @("--region", $awsRegion) }

    Write-Host "Checking AWS identity..."
    aws @awsArgs | Out-Null
}

$profiles = @("--profile", "proxy", "--profile", "localdb")
if ($WithWorkers -or $WithOrderWorker) {
    $profiles += @("--profile", "workers")
}

Write-Host "Stopping previous containers..."
docker compose --env-file $envPath @profiles down --remove-orphans

$upArgs = @("--env-file", $envPath) + $profiles + @("up", "-d")
if (-not $NoBuild) {
    $upArgs += "--build"
}

if ($WithWorkers) {
    $upArgs += @("sqlserver", "evangelionerpv2", "nginx", "worker_order", "worker_email")
}
elseif ($WithOrderWorker) {
    $upArgs += @("sqlserver", "evangelionerpv2", "nginx", "worker_order")
}
else {
    $upArgs += @("sqlserver", "evangelionerpv2", "nginx")
}

Write-Host "Starting containers..."
docker compose @upArgs

Write-Host "Done."
