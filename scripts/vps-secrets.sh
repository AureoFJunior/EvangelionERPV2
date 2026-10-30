#!/usr/bin/env bash
# Creates or updates the three Secrets Manager entries that only the VPS uses:
#
#   evangelion/vps/database   {"DefaultConnection": "Server=sqlserver,1433;...;User Id=evangelion_app;Password=..."}
#   evangelion/vps/redis      {"ConnectionString": "redis:6379,password=...,abortConnect=false"}
#   evangelion/vps/rabbitmq   {"hostname", "username", "password", "vhost", "port", "uri"}
#
# Run it from a machine with admin AWS access, not on the VPS: the VPS key can only read secrets.
# The Redis and RabbitMQ passwords come from the VPS's .env (copy it, or run this over SSH with
# admin credentials in the environment). The database password is generated the first time and
# kept afterwards; --rotate-db-password replaces it, then run vps-db-init.sh and restart the app.
#
# Usage: vps-secrets.sh <path-to-vps-.env> [--rotate-db-password] [--prefix evangelion/vps]
set -euo pipefail

ENV_FILE="${1:?Usage: vps-secrets.sh <path-to-vps-.env> [--rotate-db-password] [--prefix evangelion/vps]}"
shift
ROTATE=0
PREFIX="evangelion/vps"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --rotate-db-password) ROTATE=1; shift ;;
    --prefix) PREFIX="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done

[[ -f "$ENV_FILE" ]] || { echo "No such file: $ENV_FILE" >&2; exit 1; }
REGION="${AWS_REGION:-us-east-1}"
DATABASE="${DATABASE:-EvangelionERPV2}"
APP_USER="${APP_USER:-evangelion_app}"

env_value() {
  { grep -E "^$1=" "$ENV_FILE" || true; } | tail -n 1 | cut -d= -f2-
}

REDIS_PASSWORD="$(env_value REDIS_PASSWORD)"
RABBITMQ_USER="$(env_value RABBITMQ_USER)"
RABBITMQ_USER="${RABBITMQ_USER:-evangelion}"
RABBITMQ_PASSWORD="$(env_value RABBITMQ_PASSWORD)"
for name in REDIS_PASSWORD RABBITMQ_PASSWORD; do
  value="${!name}"
  [[ -n "$value" && "$value" != REPLACE* ]] || { echo "$name is not set in $ENV_FILE." >&2; exit 1; }
done

read_secret() {
  aws secretsmanager get-secret-value --region "$REGION" --secret-id "$1" \
    --query SecretString --output text 2>/dev/null || true
}

write_secret() {
  local id="$1" value="$2"
  if aws secretsmanager describe-secret --region "$REGION" --secret-id "$id" >/dev/null 2>&1; then
    aws secretsmanager put-secret-value --region "$REGION" --secret-id "$id" --secret-string "$value" >/dev/null
    echo "    updated $id"
  else
    aws secretsmanager create-secret --region "$REGION" --name "$id" --secret-string "$value" \
      --description "Evangelion ERP on the VPS; written by scripts/vps-secrets.sh" >/dev/null
    echo "    created $id"
  fi
}

echo "==> Writing $PREFIX/* in $REGION as $(aws sts get-caller-identity --query Arn --output text)"

# Kept across runs unless asked: rotating it here alone would lock the app out until
# vps-db-init.sh syncs the login.
DB_PASSWORD=""
if [[ "$ROTATE" == "0" ]]; then
  DB_PASSWORD="$(read_secret "$PREFIX/database" | python3 -c '
import json, sys
raw = sys.stdin.read().strip()
if raw:
    for part in json.loads(raw).get("DefaultConnection", "").split(";"):
        key, _, value = part.partition("=")
        if key.strip().lower() in ("password", "pwd"):
            print(value.strip())
' || true)"
fi
if [[ -z "$DB_PASSWORD" ]]; then
  # SQL Server's policy wants three of upper, lower, digit, symbol; the suffix guarantees them, and
  # leaving out ; = ' keeps the value safe inside a connection string and a T-SQL literal.
  DB_PASSWORD="$(openssl rand -base64 36 | tr -d '/+=\n' | cut -c1-32)Aa1!"
fi

# mktemp: 0600, because this file holds every password until the trap removes it.
SECRETS_JSON="$(mktemp -t evangelion-vps-secrets-XXXXXX.json)"
trap 'rm -f "$SECRETS_JSON"' EXIT
python3 - "$DATABASE" "$APP_USER" "$DB_PASSWORD" "$REDIS_PASSWORD" "$RABBITMQ_USER" "$RABBITMQ_PASSWORD" > "$SECRETS_JSON" <<'PY'
import json, sys, urllib.parse
database, app_user, db_password, redis_password, rabbit_user, rabbit_password = sys.argv[1:]
quote = lambda s: urllib.parse.quote(s, safe="")
print(json.dumps({
    "database": {"DefaultConnection":
        f"Server=sqlserver,1433;Database={database};User Id={app_user};Password={db_password};"
        "Encrypt=True;TrustServerCertificate=True"},
    "redis": {"ConnectionString": f"redis:6379,password={redis_password},abortConnect=false"},
    "rabbitmq": {
        "hostname": "rabbitmq", "username": rabbit_user, "password": rabbit_password,
        "vhost": "/", "port": "5672",
        "uri": f"amqp://{quote(rabbit_user)}:{quote(rabbit_password)}@rabbitmq:5672/"},
}))
PY

for name in database redis rabbitmq; do
  write_secret "$PREFIX/$name" "$(python3 -c 'import json,sys; print(json.dumps(json.load(open(sys.argv[1]))[sys.argv[2]]))' "$SECRETS_JSON" "$name")"
done

echo "==> Done. On the VPS: scripts/vps-db-init.sh (or the next deploy) syncs the $APP_USER login."
