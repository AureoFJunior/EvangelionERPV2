#!/usr/bin/env bash
# Deploys one release on the VPS. Called over SSH by buildPushDeploy.yml (deploy_hostgator), and by
# hand for a rollback: the previous releases stay on disk, so rolling back is this script with an
# older sha.
#
# The release directory has to be there already; the pipeline copies it before calling this:
#   /opt/evangelion/releases/<sha>/{docker-compose*.yml, docker/caddy/Caddyfile, scripts/, migrations.sql}
#
# Order, and why:
#   1. pull the release's images first, so a missing image fails the deploy before anything changes;
#   2. start SQL Server alone, back it up and migrate; a failed migration stops here, with the
#      previous release still serving and still named in .env;
#   3. sync the app login from the connection string it will use;
#   4. pin the new image tags in .env (so a reboot restarts this release, not "latest"), switch
#      `current`, start everything, and wait for /health from inside the Docker network.
#
# Usage: vps-deploy.sh <sha> <ecr-repository-uri>
#   e.g. vps-deploy.sh 1a2b3c4 123456789012.dkr.ecr.us-east-1.amazonaws.com/evangelionerpv2
set -euo pipefail

SHA="${1:?Usage: vps-deploy.sh <sha> <ecr-repository-uri>}"
REPOSITORY="${2:?Usage: vps-deploy.sh <sha> <ecr-repository-uri>}"
[[ "$SHA" =~ ^[0-9a-f]{7,40}$ ]] || { echo "Not a commit sha: $SHA" >&2; exit 1; }

APP_DIR="${APP_DIR:-/opt/evangelion}"
RELEASE_DIR="$APP_DIR/releases/$SHA"
export RELEASE_DIR
KEEP_RELEASES="${KEEP_RELEASES:-5}"
HEALTH_TIMEOUT_SECONDS="${HEALTH_TIMEOUT_SECONDS:-180}"

for required in docker-compose.yml docker-compose.prod.yml docker-compose.hostgator.yml \
  docker/caddy/Caddyfile scripts/lib/vps-common.sh migrations.sql; do
  [[ -e "$RELEASE_DIR/$required" ]] || { echo "Release $SHA is missing $required." >&2; exit 1; }
done

# Everything below runs the release's own scripts, so a deploy never mixes versions.
SCRIPTS="$RELEASE_DIR/scripts"
# shellcheck source=lib/vps-common.sh
source "$SCRIPTS/lib/vps-common.sh"

# One deploy at a time: the pipeline and a hand-run rollback must not interleave.
exec 9> "$APP_DIR/.deploy.lock"
flock -n 9 || { echo "Another deploy is running." >&2; exit 1; }

set_env() {
  local key="$1" value="$2"
  if grep -qE "^$key=" "$ENV_FILE"; then
    sed -i "s#^$key=.*#$key=$value#" "$ENV_FILE"
  else
    printf '%s=%s\n' "$key" "$value" >> "$ENV_FILE"
  fi
}

echo "==> Release $SHA"
PREVIOUS="$(readlink "$APP_DIR/current" 2>/dev/null || true)"

# Exported rather than written to .env yet: compose prefers the shell's values over --env-file, so
# the pull uses the new tags while .env still names the running release. It is rewritten only once
# the migrations have gone through; until then a reboot brings back what was running.
declare -A IMAGES=(
  [WEB_IMAGE]="$REPOSITORY:sha-$SHA"
  [WORKER_ORDER_IMAGE]="$REPOSITORY:worker-order-sha-$SHA"
  [WORKER_EMAIL_IMAGE]="$REPOSITORY:worker-email-sha-$SHA"
  [WORKER_NOTIFICATION_IMAGE]="$REPOSITORY:worker-notification-sha-$SHA"
)
for key in "${!IMAGES[@]}"; do export "$key=${IMAGES[$key]}"; done

echo "==> Pulling images"
compose pull --quiet

echo "==> Starting SQL Server"
compose up -d --wait sqlserver

"$SCRIPTS/vps-db-update.sh" "$RELEASE_DIR/migrations.sql"
"$SCRIPTS/vps-db-init.sh"

echo "==> Starting release"
for key in "${!IMAGES[@]}"; do set_env "$key" "${IMAGES[$key]}"; done
ln -sfn "$RELEASE_DIR" "$APP_DIR/current"
compose up -d --remove-orphans

# From inside the network, through the same name Caddy uses, so this does not depend on DNS or the
# certificate yet. Degraded still means "serving"; the pipeline's external check insists on Healthy.
echo "==> Waiting for /health"
deadline=$((SECONDS + HEALTH_TIMEOUT_SECONDS))
status=""
until [[ "$status" == "Healthy" || "$status" == "Degraded" ]]; do
  if (( SECONDS > deadline )); then
    echo "API did not report healthy within ${HEALTH_TIMEOUT_SECONDS}s (last: ${status:-no answer})." >&2
    compose logs --tail 80 evangelionerpv2 >&2 || true
    echo "Previous release: ${PREVIOUS:-none}. Roll back with: $SCRIPTS/vps-deploy.sh <previous-sha> $REPOSITORY" >&2
    exit 1
  fi
  sleep 5
  status="$(docker exec evangelionerpv2_caddy wget -qO- -T 5 http://evangelionerpv2:8080/health 2>/dev/null || true)"
done
echo "    /health: $status"

echo "==> Cleaning up"
docker image prune -f >/dev/null
# Newest first; the running release is never removed even if it is older than the rest.
ls -1dt "$APP_DIR"/releases/*/ 2>/dev/null | tail -n +$((KEEP_RELEASES + 1)) | while read -r old; do
  [[ "$(readlink -f "$old")" == "$(readlink -f "$APP_DIR/current")" ]] || rm -rf "$old"
done

echo "==> Deployed $SHA"
