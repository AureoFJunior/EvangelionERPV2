param(
    [string]$EnvFile = ".env.local",
    [switch]$WithWorkers,
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

docker compose --env-file $envPath @profiles down --remove-orphans
Write-Host "Stopped."
