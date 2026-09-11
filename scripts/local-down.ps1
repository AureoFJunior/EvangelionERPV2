param(
    [string]$EnvFile = ".env.local",
    [switch]$WithWorkers,
    [switch]$WithVoice,
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

$profiles = @("--profile", "proxy", "--profile", "localdb")
if ($WithWorkers -or $WithOrderWorker) {
    $profiles += @("--profile", "workers")
}
# The profile list also drives the "down --remove-orphans" below. Leaving voice out of it meant
# every local-up deleted a running whisper container as an orphan.
if ($WithVoice) {
    $profiles += @("--profile", "voice")
}

docker compose --env-file $envPath @profiles down --remove-orphans
Write-Host "Stopped."
