param(
    [string]$EnvFile = ".env.local",
    [switch]$WithWorkers,
    [switch]$WithVoice,
    [switch]$WithObservability,
    [switch]$WithOrderWorker
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$envPath = if ([System.IO.Path]::IsPathRooted($EnvFile)) { $EnvFile } else { Join-Path $repoRoot $EnvFile }

if (-not (Test-Path $envPath)) {
    throw "Env file not found: $envPath"
}

Set-Location $repoRoot

# Prometheus and Grafana are in their own compose file; compose only knows they exist if it is
# named, so without this they survive a down and then get deleted as orphans by the next local-up.
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

docker compose @files --env-file $envPath @profiles down --remove-orphans
Write-Host "Stopped."
