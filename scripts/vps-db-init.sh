#!/usr/bin/env bash
# Creates (or re-syncs) the login the API and workers use, from the connection string they read.
#
# The app never connects as sa. Its login gets db_datareader and db_datawriter only: nothing in the
# app changes the schema at runtime (no Database.Migrate(), no EnsureCreated()), and the number
# allocators are UPDATE ... OUTPUT against tables, not sequences. Migrations run as sa, from
# vps-db-update.sh.
#
# The user name and password come from the Secrets Manager entry the app itself reads
# (EVA_CONN_STR in .env), so there is one copy of the password. Re-run this after rotating it.
# Safe to run any number of times. vps-deploy.sh runs it after every migration.
#
# Usage: vps-db-init.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/vps-common.sh
source "$SCRIPT_DIR/lib/vps-common.sh"

REFERENCE="$(env_value EVA_CONN_STR)"
SECRET_ID="${REFERENCE%:*}"
SECRET_KEY="${REFERENCE##*:}"
[[ -n "$SECRET_ID" && "$SECRET_ID" != "$REFERENCE" ]] ||
  { echo "EVA_CONN_STR must look like <secret-id>:<json-key>." >&2; exit 1; }

CONNECTION_STRING="$(aws secretsmanager get-secret-value --secret-id "$SECRET_ID" \
  --query SecretString --output text |
  python3 -c 'import json,sys; print(json.load(sys.stdin)[sys.argv[1]])' "$SECRET_KEY")"

# Pulls one keyword out of an ADO.NET connection string, case- and space-insensitively.
connection_value() {
  python3 -c '
import sys
wanted = {k.strip().lower() for k in sys.argv[2].split("|")}
for part in sys.argv[1].split(";"):
    key, _, value = part.partition("=")
    if key.strip().lower() in wanted:
        print(value.strip())
        break
' "$CONNECTION_STRING" "$1"
}

APP_USER="$(connection_value 'user id|uid|user')"
APP_PASSWORD="$(connection_value 'password|pwd')"
[[ -n "$APP_USER" && -n "$APP_PASSWORD" ]] ||
  { echo "The connection string in $SECRET_ID has no User Id or Password." >&2; exit 1; }
[[ "$APP_USER" != "sa" ]] || { echo "The app must not connect as sa." >&2; exit 1; }
[[ "$APP_USER" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] ||
  { echo "Login name '$APP_USER' must be a plain identifier." >&2; exit 1; }

database_exists || { echo "No database $DATABASE yet. Run vps-db-update.sh first." >&2; exit 1; }

# The password reaches sqlcmd through the environment (sqlcmd reads environment variables as
# scripting variables), never the command line. Doubled quotes keep a ' in it from ending the
# literal it is substituted into.
ESCAPED_PASSWORD="${APP_PASSWORD//\'/\'\'}"

echo "==> Ensuring login $APP_USER on $DATABASE"
SQLCMDPASSWORD="$(env_value SQLSERVER_SA_PASSWORD)" APP_PASSWORD_SQL="$ESCAPED_PASSWORD" \
  docker exec -i -e SQLCMDPASSWORD -e APP_PASSWORD_SQL "$SQL_CONTAINER" \
  "$SQLCMD_BIN" -S localhost -U sa -C -I -b -v AppUser="$APP_USER" Database="$DATABASE" <<'SQL'
SET NOCOUNT ON;
DECLARE @password nvarchar(128) = N'$(APP_PASSWORD_SQL)';
-- QUOTENAME with a quote character escapes the password again for the dynamic statement.
DECLARE @sql nvarchar(max) =
    CASE WHEN SUSER_ID(N'$(AppUser)') IS NULL
         THEN N'CREATE LOGIN ' + QUOTENAME(N'$(AppUser)') + N' WITH PASSWORD = ' + QUOTENAME(@password, '''')
              + N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = ' + QUOTENAME(N'$(Database)')
         ELSE N'ALTER LOGIN ' + QUOTENAME(N'$(AppUser)') + N' WITH PASSWORD = ' + QUOTENAME(@password, '''')
    END;
EXEC (@sql);
GO
USE [$(Database)];
IF DATABASE_PRINCIPAL_ID(N'$(AppUser)') IS NULL
    CREATE USER [$(AppUser)] FOR LOGIN [$(AppUser)];
ALTER ROLE db_datareader ADD MEMBER [$(AppUser)];
ALTER ROLE db_datawriter ADD MEMBER [$(AppUser)];
GO
SQL
echo "    done"
