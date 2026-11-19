# Sourced by the vps-*.sh scripts. Everything that differs between "on the VPS" and "somewhere
# else" is decided here, once.
#
# Layout on the host (vps-bootstrap.sh creates it):
#   /opt/evangelion/.env               the only configuration; .env.hostgator.example documents it
#   /opt/evangelion/.docker/aws/       credentials + config for the evangelion IAM user
#   /opt/evangelion/backups/           SQL Server writes .bak files here (uid 10001)
#   /opt/evangelion/releases/<sha>/    one directory per deploy: compose files, Caddyfile, scripts,
#                                      migrations.sql
#   /opt/evangelion/current            symlink to the release that is running

APP_DIR="${APP_DIR:-/opt/evangelion}"
ENV_FILE="${ENV_FILE:-$APP_DIR/.env}"
DATABASE="${DATABASE:-EvangelionERPV2}"
SQL_CONTAINER="${SQL_CONTAINER:-evangelionerpv2_sqlserver}"
SQLCMD_BIN="/opt/mssql-tools18/bin/sqlcmd"

# A fixed project name. Compose otherwise names the project after the directory of the first file,
# which is releases/<sha> here: every deploy would get fresh, empty volumes for RabbitMQ and the
# Chromium cache, and leave the old containers running beside the new ones.
COMPOSE_PROJECT="${COMPOSE_PROJECT:-evangelionerpv2}"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Missing $ENV_FILE. Copy .env.hostgator.example there and fill it in." >&2
  exit 1
fi

# A placeholder password would start the stack with a guessable one. The *_IMAGE lines are exempt:
# vps-deploy.sh rewrites them itself on the first deploy.
PLACEHOLDER_KEYS="$({ grep -E '^[A-Za-z_][A-Za-z0-9_]*=REPLACE' "$ENV_FILE" || true; } |
  cut -d= -f1 | { grep -vE '_IMAGE$' || true; } | paste -sd, -)"
if [[ -n "$PLACEHOLDER_KEYS" ]]; then
  echo "$ENV_FILE still has placeholder values (REPLACE...) for: $PLACEHOLDER_KEYS" >&2
  exit 1
fi

# Reads one KEY=value line without sourcing the file, so a value with spaces or $ is taken as is.
# A missing key is an empty value, not a failure: under pipefail grep's "no match" would end the
# caller.
env_value() {
  local key="$1"
  { grep -E "^$key=" "$ENV_FILE" || true; } | tail -n 1 | cut -d= -f2-
}

# The host's own aws CLI uses the same key as the containers.
AWS_CREDENTIALS_DIR="$(env_value AWS_CREDENTIALS_DIR)"
export AWS_SHARED_CREDENTIALS_FILE="${AWS_CREDENTIALS_DIR:-$APP_DIR/.docker/aws}/credentials"
export AWS_CONFIG_FILE="${AWS_CREDENTIALS_DIR:-$APP_DIR/.docker/aws}/config"
export AWS_PROFILE="${AWS_PROFILE:-$(env_value AWS_PROFILE)}"
export AWS_REGION="${AWS_REGION:-$(env_value AWS_REGION)}"
export AWS_EC2_METADATA_DISABLED=true

# The host paths above must not reach Compose: shell variables win over --env-file in
# interpolation, and the containers read their credentials from /home/app/.aws.
compose() {
  local release_dir="${RELEASE_DIR:-$APP_DIR/current}"
  env -u AWS_SHARED_CREDENTIALS_FILE -u AWS_CONFIG_FILE \
    docker compose -p "$COMPOSE_PROJECT" --env-file "$ENV_FILE" \
    -f "$release_dir/docker-compose.yml" \
    -f "$release_dir/docker-compose.prod.yml" \
    -f "$release_dir/docker-compose.hostgator.yml" \
    --profile workers "$@"
}

# sqlcmd as sa inside the SQL Server container. The password goes through the environment, not the
# command line, so it is not in this host's process list or in `docker inspect`.
#   -C: trust the container's self-signed certificate (the connection never leaves the container).
#   -I: quoted identifiers on, or every filtered index in the schema fails with Msg 1934.
sa_sqlcmd() {
  SQLCMDPASSWORD="$(env_value SQLSERVER_SA_PASSWORD)" \
    docker exec -i -e SQLCMDPASSWORD "$SQL_CONTAINER" \
    "$SQLCMD_BIN" -S localhost -U sa -C -I -b "$@"
}

# False only when SQL Server answers that the database is absent. A failed lookup ends the script:
# callers run it inside `if`, where set -e does not apply.
database_exists() {
  local result
  result="$(sa_sqlcmd -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$DATABASE') IS NULL THEN 0 ELSE 1 END;")" ||
    { echo "Could not ask SQL Server whether $DATABASE exists." >&2; exit 1; }
  [[ "$result" == "1" ]]
}
