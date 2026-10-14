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

# No -f anywhere in this script, for the reason local-up.ps1 gives: COMPOSE_FILE names the files,
# and an explicit -f would override it and drop one.
$profiles = @("--profile", "proxy", "--profile", "localdb")
if ($WithWorkers -or $WithOrderWorker) {
    $profiles += @("--profile", "workers")
}
# The profile list also drives the "down --remove-orphans" below. Leaving voice out of it meant
# every local-up deleted a running whisper container as an orphan.
if ($WithVoice) {
    $profiles += @("--profile", "voice")
}
# Same pairing as local-up.ps1. A profile left out here leaves its containers attached to the shared
# network, and the teardown then fails with "Resource is still in use" rather than removing it.
if ($WithObservability) {
    $profiles += @("--profile", "exporters")
}

docker compose --env-file $envPath @profiles down --remove-orphans
Write-Host "Stopped."
