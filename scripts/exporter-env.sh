#!/usr/bin/env bash
# Prints the exporter variables for .env.local, read from AWS Secrets Manager.
#
# The dependency exporters connect to Redis and RabbitMQ directly, so they need real credentials
# rather than the `evangelion/dev/*` references the application resolves at runtime. This reads
# those secrets with your AWS profile and prints the lines to append:
#
#   ./scripts/exporter-env.sh >> .env.local
#
# Then bring them up:
#
#   docker compose --env-file .env.local \
#     -f docker-compose.yml -f docker-compose.observability.yml \
#     --profile exporters up -d
#
# Nothing here is written to disk by this script, and nothing is echoed to a terminal you did not
# redirect. .env.local is git-ignored.
set -euo pipefail

REGION="${AWS_REGION:-us-east-1}"

secret() {
  aws secretsmanager get-secret-value --region "$REGION" --secret-id "$1" --query SecretString --output text
}

redis_raw="$(secret evangelion/dev/redis)"
redis_host="$(grep -oE '[A-Za-z0-9.-]+\.upstash\.io:[0-9]+' <<<"$redis_raw" | head -1)"
redis_pass="$(sed -n 's/.*password=\([^,}]*\).*/\1/p' <<<"$redis_raw")"

rabbit_uri="$(grep -oE 'amqps://[^"}]+' <<<"$(secret evangelion/dev/rabbitmq)")"
rabbit_cred="${rabbit_uri#amqps://}"; rabbit_cred="${rabbit_cred%%@*}"
rabbit_hostpath="${rabbit_uri#*@}"
rabbit_host="${rabbit_hostpath%%/*}"
rabbit_vhost="${rabbit_hostpath##*/}"

if [[ -z "$redis_host" || -z "$rabbit_host" ]]; then
  echo "Could not parse a secret. Check the AWS profile can read evangelion/dev/*." >&2
  exit 1
fi

cat <<EOF

# --- Dependency exporters (scripts/exporter-env.sh) ---
# Redis is Upstash and RabbitMQ is CloudAMQP, so the exporters connect over the internet.
# CloudAMQP does not expose the prometheus plugin on :15692, so the RabbitMQ exporter reads the
# management API over HTTPS instead.
REDIS_EXPORTER_ADDR=rediss://${redis_host}
REDIS_EXPORTER_PASSWORD=${redis_pass}
RABBITMQ_EXPORTER_URL=https://${rabbit_host}
RABBITMQ_EXPORTER_USER=${rabbit_cred%%:*}
RABBITMQ_EXPORTER_PASSWORD=${rabbit_cred#*:}
RABBITMQ_EXPORTER_VHOST=${rabbit_vhost}
EOF
