#!/usr/bin/env bash
# Applies a release's migrations to the SQL Server container on the VPS.
#
# The VPS twin of rds-db-update.sh. The script itself is generated in CI (buildPushDeploy.yml), so
# this box needs neither the source nor a .NET SDK image: only the file and the running container.
#
# Order matters and each step stops the deploy on failure, before any new container starts:
#   1. back up (vps-db-backup.sh premigration), the way back from a migration that goes wrong;
#   2. create the database on the very first run;
#   3. apply, with XACT_ABORT so a failed migration is rolled back rather than recorded.
#
# Usage: vps-db-update.sh <migrations.sql>
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/vps-common.sh
source "$SCRIPT_DIR/lib/vps-common.sh"
# shellcheck source=lib/abort-on-error.sh
source "$SCRIPT_DIR/lib/abort-on-error.sh"

MIGRATIONS="${1:?Usage: vps-db-update.sh <migrations.sql>}"
[[ -s "$MIGRATIONS" ]] || { echo "$MIGRATIONS is missing or empty." >&2; exit 1; }

"$SCRIPT_DIR/vps-db-backup.sh" premigration

if ! database_exists; then
  echo "==> Creating $DATABASE"
  sa_sqlcmd -Q "CREATE DATABASE [$DATABASE];"
fi

SCRIPT_PATH="$(mktemp -t evangelion-migrations-XXXXXX.sql)"
trap 'rm -f "$SCRIPT_PATH"' EXIT
cp "$MIGRATIONS" "$SCRIPT_PATH"
prepend_xact_abort "$SCRIPT_PATH"
# mktemp makes the file 0600, and sqlcmd reads it as the mssql user inside the container.
chmod 644 "$SCRIPT_PATH"

echo "==> Applying migrations"
docker cp "$SCRIPT_PATH" "$SQL_CONTAINER:/tmp/migrations.sql"
sa_sqlcmd -d "$DATABASE" -i /tmp/migrations.sql
docker exec -u 0 "$SQL_CONTAINER" rm -f /tmp/migrations.sql

echo "==> Latest applied migrations"
sa_sqlcmd -d "$DATABASE" -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT TOP 5 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;"
