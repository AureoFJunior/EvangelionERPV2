#!/usr/bin/env bash
# Checks the merged VPS compose configuration for the mistakes that turn into an outage or an open
# port, without starting anything. Run by runTests.yml on every PR, and safe to run locally.
#
#   1. Only Caddy publishes host ports. Docker bypasses ufw for published ports, so a stray
#      `ports:` on SQL Server, Redis or RabbitMQ is an internet-facing service.
#   2. SQL Server runs as Express, the edition that is licensed for production at no cost.
#   3. Every long-running service restarts on its own after a crash or a reboot.
#   4. The overlay refuses to start without each of its passwords, instead of falling back to a
#      default that is committed to git.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# The example with every placeholder filled, so interpolation succeeds.
sed -e 's/=REPLACE$/=Placeholder1/' -e 's/REPLACE_[A-Z_]*/placeholder/g' \
  "$ROOT/.env.hostgator.example" > "$WORK/.env"

merged() {
  docker compose --project-directory "$ROOT" --env-file "$1" \
    -f "$ROOT/docker-compose.yml" -f "$ROOT/docker-compose.prod.yml" -f "$ROOT/docker-compose.hostgator.yml" \
    --profile workers config --format json
}

merged "$WORK/.env" > "$WORK/config.json"

python3 - "$WORK/config.json" <<'PY'
import json, sys

services = json.load(open(sys.argv[1]))["services"]
failures = []

for name, service in services.items():
    published = [p.get("published") for p in service.get("ports", []) if p.get("published")]
    if published and name != "caddy":
        failures.append(f"{name} publishes host ports {published}; only caddy may.")
    if name != "volume_init" and service.get("restart") not in ("unless-stopped", "always"):
        failures.append(f"{name} has restart={service.get('restart')!r}; it would stay down after a reboot.")

edition = services.get("sqlserver", {}).get("environment", {}).get("MSSQL_PID")
if edition != "Express":
    failures.append(f"sqlserver runs as {edition!r}, not Express.")

for expected in ("sqlserver", "redis", "rabbitmq", "evangelionerpv2", "caddy",
                 "worker_order", "worker_email", "worker_notification"):
    if expected not in services:
        failures.append(f"{expected} is missing from the merged configuration.")

if failures:
    print("\n".join(failures))
    sys.exit(1)
print(f"OK: {len(services)} services, ports only on caddy, SQL Server Express, restart policies set.")
PY

for secret in SQLSERVER_SA_PASSWORD REDIS_PASSWORD RABBITMQ_PASSWORD; do
  grep -v "^$secret=" "$WORK/.env" > "$WORK/.env.missing"
  if merged "$WORK/.env.missing" >/dev/null 2>&1; then
    echo "The configuration starts without $secret; it must refuse to." >&2
    exit 1
  fi
done
echo "OK: refuses to start without SQLSERVER_SA_PASSWORD, REDIS_PASSWORD or RABBITMQ_PASSWORD."
