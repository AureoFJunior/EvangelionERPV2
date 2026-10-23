#!/usr/bin/env bash
# Starts isolated E2E infrastructure and applies the current schema to its disposable database.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="evangelionerpv2_e2e_sql"
DATABASE="ERP_E2E"
STACK_ENV="$ROOT/.env.e2e.stack"
MIGRATIONS="$(mktemp -t evangelion-e2e-migrations-XXXXXX.sql)"
trap 'rm -f "$MIGRATIONS"' EXIT
COMPOSE=(docker compose -p evangelionerpv2-e2e -f "$ROOT/docker-compose.e2e.yml")

if [[ ! -f "$STACK_ENV" ]]; then
  (umask 077; printf 'E2E_SQLSERVER_SA_PASSWORD=E2e!%s\n' "$(openssl rand -hex 32)") > "$STACK_ENV"
fi
chmod 600 "$STACK_ENV"
# This generated local file contains only the stack's SQL password.
source "$STACK_ENV"
: "${E2E_SQLSERVER_SA_PASSWORD:?Invalid E2E stack credentials in $STACK_ENV}"
export E2E_SQLSERVER_SA_PASSWORD

if docker network inspect evangelionerpv2_e2e_net >/dev/null 2>&1; then
  [[ "$(docker network inspect evangelionerpv2_e2e_net --format '{{.Internal}}')" == true ]] ||
    { echo "Refusing non-internal Docker network evangelionerpv2_e2e_net." >&2; exit 1; }
else
  docker network create --internal evangelionerpv2_e2e_net >/dev/null
fi

if docker inspect "$CONTAINER" >/dev/null 2>&1 &&
   [[ "$(docker inspect "$CONTAINER" --format '{{index .Config.Labels "com.docker.compose.project"}}')" != "evangelionerpv2-e2e" ]]; then
  image="$(docker inspect "$CONTAINER" --format '{{.Config.Image}}')"
  volume="$(docker inspect "$CONTAINER" --format '{{range .Mounts}}{{if eq .Destination "/var/opt/mssql"}}{{.Name}}{{end}}{{end}}')"
  if [[ "$image" != mcr.microsoft.com/mssql/server:2022-latest ||
        "$volume" != evangelionerpv2_e2e_sql_data_v2 ]]; then
    echo "Refusing unexpected existing E2E SQL container or data volume." >&2
    exit 1
  fi
  echo "The E2E SQL container is not Compose-managed. Remove only that container (the named data volume is preserved), then rerun this script." >&2
  exit 1
fi

"${COMPOSE[@]}" up -d --wait

for attempt in {1..60}; do
  if docker exec "$CONTAINER" sh -lc \
      'exec /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "SELECT 1"' \
      >/dev/null 2>&1; then
    break
  fi
  if [[ "$attempt" == 60 ]]; then
    echo "The isolated E2E SQL Server did not become ready." >&2
    exit 1
  fi
  sleep 2
done

printf "IF DB_ID(N'%s') IS NULL CREATE DATABASE [%s];\n" "$DATABASE" "$DATABASE" |
  docker exec -i "$CONTAINER" sh -lc \
    'exec /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -b -I -h -1 -W -i /dev/stdin'

dotnet ef migrations script --idempotent \
  --project "$ROOT/EvangelionERPV2.Shared/EvangelionERPV2.Shared.csproj" \
  --startup-project "$ROOT/EvangelionERPV2.Web/EvangelionERPV2.Web.csproj" \
  -o "$MIGRATIONS"

chmod 644 "$MIGRATIONS"
docker cp "$MIGRATIONS" "$CONTAINER:/tmp/e2e-migrations.sql"
docker exec "$CONTAINER" sh -lc \
  "exec /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P \"\$MSSQL_SA_PASSWORD\" -b -I -d $DATABASE -i /tmp/e2e-migrations.sql"

echo "Isolated SQL and RabbitMQ are ready on evangelionerpv2_e2e_net; schema is current."
echo "Run python3 scripts/e2e-seed-accounts.py --apply to create Cypress fixture accounts."
