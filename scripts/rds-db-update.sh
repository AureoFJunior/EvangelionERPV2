#!/usr/bin/env bash
# Applies pending EF migrations to the RDS database.
#
# The remote twin of local-db-update.sh, and the step most likely to be forgotten at cutover.
# Nothing in the app calls Database.Migrate(), so a freshly created RDS instance is an empty
# database: the deploy succeeds, the container starts, health goes green, and every request
# touching a table answers 500 with "Invalid column name". It looks like a broken deploy rather
# than a missing step, which is why this exists as a script instead of a line in a runbook.
#
# Differences from the local one, all of them consequences of there being no container:
#   * sqlcmd runs here, in a container of its own, against a remote host - there is nothing to
#     `docker exec` into.
#   * No BACKUP DATABASE. RDS does not let you write a .bak to local disk; the equivalent is a
#     snapshot, taken below through the AWS CLI.
#   * Credentials come from Secrets Manager rather than .env.local, because `manage_master_user_password`
#     has RDS generate the master password and keep it there.
#
# Two traps carried over unchanged, because they bite the same way remotely:
#   1. Generating an idempotent script needs no database connection at all, so it happens before
#      anything is touched.
#   2. sqlcmd runs with QUOTED_IDENTIFIER OFF by default and every filtered index in this schema
#      then fails with Msg 1934. `-I` is not optional.
#
# WHERE THIS RUNS MATTERS. Terraform sets publicly_accessible = false, so the database is only
# reachable from inside the VPC - from the application instance. Run this there, over SSM:
#
#   aws ssm start-session --target <instance-id>
#   cd /opt/evangelion && ./scripts/rds-db-update.sh
#
# From a laptop it will resolve the endpoint from the AWS API quite happily and then hang on the
# connection, which is a confusing way to learn about network topology.
#
# Usage:
#   ./scripts/rds-db-update.sh                         # resolve everything from AWS
#   ./scripts/rds-db-update.sh --no-snapshot           # skip the pre-flight snapshot
#   DB_HOST=... DB_USER=... DB_PASSWORD=... ./scripts/rds-db-update.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REGION="${AWS_REGION:-us-east-1}"
DB_IDENTIFIER="${DB_IDENTIFIER:-evangelionerpv2}"
DATABASE="${DATABASE:-EvangelionERPV2}"
# mssql-tools, not mssql-tools18: the 18 binaries ship only inside the mssql/server image, and
# there is no standalone mcr.microsoft.com/mssql-tools18 to pull. This one is small and has the
# flags that matter.
SQLCMD_IMAGE="${SQLCMD_IMAGE:-mcr.microsoft.com/mssql-tools}"
SQLCMD_BIN="${SQLCMD_BIN:-/opt/mssql-tools/bin/sqlcmd}"

# -N encrypts; -C trusts the presented certificate. RDS serves a certificate signed by the Amazon
# RDS CA, which is not in this image's trust store, and the alternative to -C is baking that CA
# bundle in. Connecting unencrypted is not the alternative: without -N, sqlcmd 17 does not encrypt
# at all, and the master password would cross the VPC in the clear.
SQLCMD_TLS=(-N -C)
TAKE_SNAPSHOT=1

for arg in "$@"; do
  case "$arg" in
    --no-snapshot) TAKE_SNAPSHOT=0 ;;
    *) echo "unknown argument: $arg" >&2; exit 1 ;;
  esac
done

# --- Resolve the target ----------------------------------------------------------------------

if [[ -z "${DB_HOST:-}" ]]; then
  echo "==> Resolving $DB_IDENTIFIER in $REGION"
  DB_HOST="$(aws rds describe-db-instances --region "$REGION" \
    --db-instance-identifier "$DB_IDENTIFIER" \
    --query 'DBInstances[0].Endpoint.Address' --output text 2>/dev/null || true)"

  if [[ -z "$DB_HOST" || "$DB_HOST" == "None" ]]; then
    echo "No RDS instance '$DB_IDENTIFIER' in $REGION. Create it first (terraform), or set DB_HOST." >&2
    exit 1
  fi
fi

if [[ -z "${DB_USER:-}" || -z "${DB_PASSWORD:-}" ]]; then
  echo "==> Reading the master credentials from Secrets Manager"
  SECRET_ARN="$(aws rds describe-db-instances --region "$REGION" \
    --db-instance-identifier "$DB_IDENTIFIER" \
    --query 'DBInstances[0].MasterUserSecret.SecretArn' --output text 2>/dev/null || true)"

  if [[ -z "$SECRET_ARN" || "$SECRET_ARN" == "None" ]]; then
    echo "The instance has no managed master password. Set DB_USER and DB_PASSWORD explicitly." >&2
    exit 1
  fi

  SECRET_JSON="$(aws secretsmanager get-secret-value --region "$REGION" \
    --secret-id "$SECRET_ARN" --query SecretString --output text)"
  DB_USER="$(printf '%s' "$SECRET_JSON" | python3 -c 'import json,sys; print(json.load(sys.stdin)["username"])')"
  DB_PASSWORD="$(printf '%s' "$SECRET_JSON" | python3 -c 'import json,sys; print(json.load(sys.stdin)["password"])')"
fi

echo "==> Target: $DB_HOST / $DATABASE as $DB_USER"

# --- Snapshot --------------------------------------------------------------------------------

# Cheap insurance against a migration that half-applies. Skippable for a database that is empty,
# which is the usual case the first time this runs.
if [[ "$TAKE_SNAPSHOT" == "1" ]]; then
  SNAPSHOT_ID="${DB_IDENTIFIER}-premigration-$(date +%Y%m%d%H%M%S)"
  echo "==> Snapshot $SNAPSHOT_ID"
  aws rds create-db-snapshot --region "$REGION" \
    --db-instance-identifier "$DB_IDENTIFIER" \
    --db-snapshot-identifier "$SNAPSHOT_ID" >/dev/null
  aws rds wait db-snapshot-available --region "$REGION" --db-snapshot-identifier "$SNAPSHOT_ID"
  echo "    done"
fi

# --- Generate ---------------------------------------------------------------------------------

SCRIPT_PATH="$(mktemp -t evangelion-migrations-XXXXXX.sql)"
trap 'rm -f "$SCRIPT_PATH"' EXIT

echo "==> Generating the idempotent migration script"
dotnet ef migrations script --idempotent \
  --project "$ROOT/EvangelionERPV2.Shared/EvangelionERPV2.Shared.csproj" \
  --startup-project "$ROOT/EvangelionERPV2.Web/EvangelionERPV2.Web.csproj" \
  -o "$SCRIPT_PATH"

# --- Apply ------------------------------------------------------------------------------------

echo "==> Applying"
chmod 644 "$SCRIPT_PATH"

# The password goes in through the environment, not the command line, so it does not appear in
# `docker inspect` or in this machine's process listing while the migration runs.
# -I: quoted identifiers on, or every filtered index fails with Msg 1934.
# -b: stop on the first error rather than continuing and recording the migration as applied.
docker run --rm -i \
  -e SQLCMDPASSWORD="$DB_PASSWORD" \
  -v "$SCRIPT_PATH:/tmp/migrations.sql:ro" \
  "$SQLCMD_IMAGE" \
  "$SQLCMD_BIN" -S "$DB_HOST" -U "$DB_USER" -d "$DATABASE" "${SQLCMD_TLS[@]}" -I -b -i /tmp/migrations.sql

echo "==> Applied migrations"
docker run --rm -i \
  -e SQLCMDPASSWORD="$DB_PASSWORD" \
  "$SQLCMD_IMAGE" \
  "$SQLCMD_BIN" -S "$DB_HOST" -U "$DB_USER" -d "$DATABASE" "${SQLCMD_TLS[@]}" -h -1 -W \
  -Q "SET NOCOUNT ON; SELECT TOP 5 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;"
