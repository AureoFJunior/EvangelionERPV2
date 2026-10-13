# perf — k6 load rig

Executable half of [`docs/performance/SPEC-stress-test.md`](../docs/performance/SPEC-stress-test.md).
Read the spec for what is measured and why; this file is how to run it.

```
lib/common.js         levels, auth, tagging, response-size metric, shared thresholds
scenarios/*.js        one file per spec §6 scenario
run.sh                k6 in docker + docker stats sampler -> results/<stamp>-<scenario>-<level>/
.env.perf.example     credentials template (copy to .env.perf, git-ignored)
```

## Before the first run

```bash
./scripts/local-db-update.sh                 # schema current, or every request is a 500

# raise the per-IP limit on the API (see below), then:
docker compose --env-file .env.local -f docker-compose.yml -f docker-compose.perf.yml up -d evangelionerpv2

./perf/seed.py                               # accounts + fixtures, idempotent
```

`seed.py` creates one account per access level (`perf.admin` … `perf.employee`), proves each one can
log in, writes `perf/.env.perf` with their passwords, and tops the fixtures up to 500 customers,
1000 products and 5000 orders. Re-running it is safe: it reuses what exists and only fills the gap.
`--users-only` stops after the accounts; `--customers/--products/--orders` change the targets.

Everything it can do through the API, it does through the API. The one exception is `perf.admin`
itself — creating a user needs a token, getting a token needs a user, and there is no anonymous
sign-up — so that single row is inserted into the database with a PBKDF2 hash in the format
`SharedFunctions.HashPassword` writes.

The stack must run with `ASPNETCORE_ENVIRONMENT=Development` (reCAPTCHA is `Enabled: false` there;
in Production it is on and fails closed, so no script can log in) and with the per-IP rate limit
raised. All traffic arrives from one address and nginx overwrites `X-Real-IP` with the real peer, so
the 300/min general rule is reached at about 5 RPS. `docker-compose.perf.yml` is that override and
nothing else:

```yaml
IpRateLimiting__GeneralRules__0__Limit: "1000000"
```

Put the container back to its normal limit with `scripts/local-up.ps1`, or the same compose command
without that file. Leave the override **off** for `rate_limit_guard`, which exists to prove the
limiter still works.

## Running

```bash
./perf/run.sh read_light smoke        # always smoke first — a broken scenario at 300 VUs is noise
./perf/run.sh read_light baseline
./perf/run.sh read_heavy load
./perf/run.sh write_mixed baseline
./perf/run.sh rbac_denied baseline
./perf/run.sh rate_limit_guard smoke
./scripts/e2e-cleanup.sh --apply      # after any write scenario
```

Levels: `smoke` `baseline` `load` `stress` `spike` `soak`.

Each run writes `summary.json` (k6 metrics, per-route), `k6.log` and `resources.csv` (CPU/mem per
container, 5 s samples). Turn those into a report per spec §10.

## What the scenarios assume

- **Data already exists.** `setup()` reads real product and customer ids off the API with the admin
  token and uses whatever the tenant holds — `seed.py` is what puts it there. An empty tenant fails
  fast with "seed the database first".
- **Roles are pinned per VU** (`__VU % roles.length`), so one run spreads across access levels and
  every sample carries the level that produced it.
- **Writes are prefixed `E2E `** so `scripts/e2e-cleanup.sh` already knows how to remove them —
  including the fixtures `seed.py` created, which carry the same prefix. Running the cleanup empties
  the tenant; re-run `./perf/seed.py` to put it back.
- **Only active rows are sampled.** The list endpoints return soft-deleted products and customers
  while order creation refuses them, so `setup()` keeps `isActive === true` only.
- **Seeded products carry no images**, so `read_heavy`'s picture payload is small (~33 KB at
  pageSize 200). The 2 MB budget only means something once real images exist.
- **Order lines use quantity 1.** Creating an order draws down `StorageQuantity`; a long write run
  against thin stock starts returning 400 and then measures the reject path, not the write path.
- **Two write paths are opt-in.** `PERF_PRODUCT_UPDATE=1` rewrites real product rows with their own
  values; `PERF_PDV_SYNC=1` needs an enrolled terminal *and* an open cash session (`PDV_SESSION_ID`),
  which nothing here opens for you. Without the flag, `pdv_sync` is catalog + session reads only.

## Not here on purpose

Scraping `/metrics`. That is the Prometheus/Grafana stack's job (`docs/observability/`), which reads
container port **9101** — the same registry, unauthenticated, and never published to a host port. The
rig does not scrape it. Not because it could not: k6 runs with `--network host`, and on Linux the host
routes to the bridge subnet, so the container's own address (`docker inspect -f
'{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' evangelionerpv2_web`) plus `:9101` works —
only the DNS name `evangelionerpv2` is out of reach. It does not scrape because `resources.csv` answers
which container saturates, and anything finer is correlated by timestamp against Prometheus.

Two things not to do: do not scrape `/metrics` through nginx on :8082 (it returns 404 by design), and
do not put a Supervisor token in `.env.perf` to reach the authenticated door — that credential can
read every business endpoint in the system.

Note the running `evangelionerpv2_web` container may predate port 9101. Rebuilding it picks that up,
and the rebuild must name `docker-compose.perf.yml` again or the rate-limit override is lost.

## Found while building this

Two things the smoke runs turned up, both in the API rather than the rig:

1. **`Product/GetProducts` and `Customer/GetCustomers` return soft-deleted rows.** Neither filters
   `IsActive`, but `OrderService.ValidateOrderProductsAsync` requires `IsActive == true`. Ordering a
   product straight off the list can therefore fail with a sanitized 400, and the caller has no way
   to tell which line was the problem. 17 of 1019 products here are inactive, which was enough to
   fail ~10-25% of write iterations until the rig filtered them out.
2. **Concurrent order creation deadlocks.** Seeding 5000 orders at 8 parallel writers produced 169
   failures, all SQL Server error 1205 (deadlock victim) wrapped as `InsertDatabaseException` and
   returned to the client as a 400 "An internal error occurred". Retrying recovered 164 of them, so
   the work is transient — but a deadlock is reported as a bad request, and a client cannot tell it
   from a payload it should not retry. `write_mixed` at higher levels is where to measure how bad
   this gets.
