#!/usr/bin/env bash
# Applies pending EF migrations to the local SQL Server container.
#
# Nothing in the app calls Database.Migrate() — there is no automatic path — so a migration that
# is written and committed is not a migration that has run. On 2026-08-20 the local database was
# nine migrations behind, and every request touching Customer, Supplier, StockMovement,
# EmployeeAbsence, the bank statement tables or the vacation accrual answered 500 with
# "Invalid column name".
#
# Two traps this script exists to carry:
#   1. `dotnet ef database update` would need the app's own connection string, which resolves
#      through AWS Secrets Manager. Generating an idempotent script needs no database at all.
#   2. sqlcmd runs with QUOTED_IDENTIFIER OFF by default, and every filtered index in this schema
#      then fails with Msg 1934. `-I` is not optional.
#
# Usage: ./scripts/local-db-update.sh [container] [database]

set -euo pipefail

CONTAINER="${1:-evangelionerpv2_sqlserver}"
DATABASE="${2:-EvangelionERPV2}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCRIPT_PATH="$(mktemp -t evangelion-migrations-XXXXXX.sql)"

if [[ ! -f "$ROOT/.env.local" ]]; then
  echo "Missing .env.local — copy .env.local.example first." >&2
  exit 1
fi

SA_PASSWORD="$(grep -E '^SQLSERVER_SA_PASSWORD=' "$ROOT/.env.local" | cut -d= -f2-)"
if [[ -z "$SA_PASSWORD" ]]; then
  echo "SQLSERVER_SA_PASSWORD is not set in .env.local." >&2
  exit 1
fi

echo "==> Generating the idempotent migration script"
dotnet ef migrations script --idempotent \
  --project "$ROOT/EvangelionERPV2.Shared/EvangelionERPV2.Shared.csproj" \
  --startup-project "$ROOT/EvangelionERPV2.Web/EvangelionERPV2.Web.csproj" \
  -o "$SCRIPT_PATH"

echo "==> Backing up $DATABASE"
docker exec "$CONTAINER" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C \
  -Q "BACKUP DATABASE [$DATABASE] TO DISK='/var/opt/mssql/data/${DATABASE}_before_update.bak' WITH INIT, COMPRESSION;"

echo "==> Applying"
# mktemp makes the file 0600 and sqlcmd runs as the mssql user inside the container, which then
# cannot read it (error code 0x80070005).
chmod 644 "$SCRIPT_PATH"
docker cp "$SCRIPT_PATH" "$CONTAINER:/tmp/migrations.sql"
# -I: quoted identifiers on, or every filtered index in the schema fails with Msg 1934.
# -b: stop on the first error instead of continuing and recording the migration as applied.
docker exec "$CONTAINER" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C -I -b \
  -d "$DATABASE" -i /tmp/migrations.sql

echo "==> Applied migrations"
docker exec "$CONTAINER" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C \
  -d "$DATABASE" -h -1 -W -Q "SET NOCOUNT ON; SELECT TOP 5 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;"

rm -f "$SCRIPT_PATH"
