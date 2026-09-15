param(
    [string]$EnvFile = ".env.local",
    [switch]$WithWorkers,
    [switch]$WithVoice,
    [switch]$WithObservability,
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

# Prometheus and Grafana live in their own compose file, not a profile, so the profile list below
# cannot protect them from the orphan sweep - compose only knows a service exists if its file is
# named. Without this, every local-up would delete a running monitoring stack, which is the same
# trap voice hit and the reason the comment below exists.
$files = @()
if ($WithObservability) {
    $files = @("-f", "docker-compose.yml", "-f", "docker-compose.observability.yml")
}

$profiles = @("--profile", "proxy", "--profile", "localdb")
if ($WithWorkers -or $WithOrderWorker) {
    $profiles += @("--profile", "workers")
}
# The profile list also drives the "down --remove-orphans" below. Leaving voice out of it meant
# every local-up deleted a running whisper container as an orphan.
if ($WithVoice) {
    $profiles += @("--profile", "voice")
}

Write-Host "Stopping previous containers..."
docker compose @files --env-file $envPath @profiles down --remove-orphans

$upArgs = $files + @("--env-file", $envPath) + $profiles + @("up", "-d")
if (-not $NoBuild) {
    $upArgs += "--build"
}

if ($WithWorkers) {
    $upArgs += @("sqlserver", "evangelionerpv2", "nginx", "worker_order", "worker_email", "worker_notification")
}
elseif ($WithOrderWorker) {
    $upArgs += @("sqlserver", "evangelionerpv2", "nginx", "worker_order")
}
else {
    $upArgs += @("sqlserver", "evangelionerpv2", "nginx")
}

# Naming services means every service has to be named. -WithVoice used to add the profile and stop
# there, which kept the orphan sweep above from deleting whisper and piper but never actually
# started them — so the shortcut brought up an assistant that could neither hear nor speak.
if ($WithVoice) {
    $upArgs += @("whisper", "piper")
}

# Same reasoning as voice: naming services means every service has to be named, or the stack is
# protected from the orphan sweep and then never started. Needs GRAFANA_ADMIN_PASSWORD in the env
# file - compose refuses outright without it, by design.
if ($WithObservability) {
    $upArgs += @("prometheus", "grafana")
}

Write-Host "Starting containers..."
docker compose @upArgs

Write-Host "Done."
