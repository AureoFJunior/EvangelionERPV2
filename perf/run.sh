#!/usr/bin/env bash
# Runs one k6 scenario at one load level and samples container resources alongside it.
#
#   ./perf/run.sh read_light baseline
#   ./perf/run.sh write_mixed load
#   ./perf/run.sh rate_limit_guard smoke      # run this one WITHOUT the rate-limit override
#
# Credentials come from perf/.env.perf (git-ignored). See perf/README.md.

set -euo pipefail

SCENARIO="${1:?usage: run.sh <scenario> <level>}"
LEVEL="${2:-smoke}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
STAMP="$(date +%Y%m%d-%H%M%S)"
OUT="$HERE/results/$STAMP-$SCENARIO-$LEVEL"

[[ -f "$HERE/scenarios/$SCENARIO.js" ]] || { echo "no such scenario: $SCENARIO"; exit 1; }
# shellcheck disable=SC1091
[[ -f "$HERE/.env.perf" ]] && set -a && source "$HERE/.env.perf" && set +a

BASE_URL="${BASE_URL:-http://localhost:8082/api/v1}"
mkdir -p "$OUT"

# Resource sampler: docker stats every 5s into CSV. Prometheus/Grafana would do this better and is
# a later job — ponytail: CSV + a spreadsheet is enough to spot a saturated container.
CONTAINERS="evangelionerpv2_web evangelionerpv2_nginx evangelionerpv2_sqlserver"
sample_resources() {
    echo "ts,container,cpu_pct,mem_used,mem_pct,net_io,block_io" > "$OUT/resources.csv"
    while true; do
        docker stats --no-stream --format '{{.Name}},{{.CPUPerc}},{{.MemUsage}},{{.MemPerc}},{{.NetIO}},{{.BlockIO}}' $CONTAINERS 2>/dev/null \
            | sed "s|^|$(date +%s),|" | tr -d '%' >> "$OUT/resources.csv"
        sleep 5
    done
}
sample_resources & SAMPLER=$!
trap 'kill "$SAMPLER" 2>/dev/null || true' EXIT

echo "scenario=$SCENARIO level=$LEVEL base=$BASE_URL -> $OUT"

# Cloud mode. Every load run so far came from one IP, so the per-IP rate limiter absorbed the
# traffic before OverloadProtection's concurrency limiter (400 permits in Production) ever saw it -
# N2 in the readiness spec. Grafana Cloud k6 generates from many addresses, which is the only way
# that limit gets exercised.
#
#   K6_CLOUD_TOKEN=... ./perf/run.sh read_heavy load          # local generator, results to cloud
#   K6_CLOUD_TOKEN=... K6_RUN_IN_CLOUD=1 ./perf/run.sh ...    # generated from cloud IPs
#
# BASE_URL must be publicly reachable for K6_RUN_IN_CLOUD - the cloud generators cannot see
# localhost:8082. Point it at the deployed instance.
K6_COMMAND=(run)
if [ -n "${K6_CLOUD_TOKEN:-}" ]; then
    if [ "${K6_RUN_IN_CLOUD:-}" = "1" ]; then
        case "$BASE_URL" in
            *localhost*|*127.0.0.1*)
                echo "K6_RUN_IN_CLOUD=1 needs a publicly reachable BASE_URL; cloud generators cannot reach $BASE_URL." >&2
                exit 1
                ;;
        esac
        K6_COMMAND=(cloud run)
        echo "mode: generated from Grafana Cloud (multiple source IPs)"
    else
        K6_COMMAND=(cloud run --local-execution)
        echo "mode: local generator, results streamed to Grafana Cloud"
    fi
fi

# --network host so localhost:8082 inside the container is the nginx on this machine.
docker run --rm --network host \
    -v "$HERE:/perf:ro" -v "$OUT:/out" \
    -e BASE_URL="$BASE_URL" -e LEVEL="$LEVEL" \
    -e K6_CLOUD_TOKEN -e K6_CLOUD_STACK_ID -e K6_CLOUD_PROJECT_ID \
    -e ADMIN_USER -e ADMIN_PASS \
    -e MANAGER_USER -e MANAGER_PASS \
    -e SUPERVISOR_USER -e SUPERVISOR_PASS \
    -e EMPLOYEE_USER -e EMPLOYEE_PASS \
    -e PDV_USER -e PDV_PASS -e PDV_TERMINAL -e PDV_SESSION_ID \
    -e PERF_PRODUCT_UPDATE -e PERF_PDV_SYNC \
    grafana/k6:latest "${K6_COMMAND[@]}" \
    --summary-export=/out/summary.json \
    "/perf/scenarios/$SCENARIO.js" | tee "$OUT/k6.log"

echo
echo "results: $OUT"
echo "remember: ./scripts/e2e-cleanup.sh --apply after any write scenario"
