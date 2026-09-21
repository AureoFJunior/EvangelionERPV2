# Evangelion ERP V2

<p align="center">
  <img src="https://static.wikia.nocookie.net/evangelion/images/d/db/Neon_Genesis_Evangelion_Logo_transparent.png/revision/latest/scale-to-width-down/1000?cb=20200521033858" alt="Evangelion ERP logo" width="680"/>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white" alt="C#" />
  <img src="https://img.shields.io/badge/.NET-5C2D91?style=for-the-badge&logo=.net&logoColor=white" alt=".NET" />
  <img src="https://img.shields.io/badge/JavaScript-F7DF1E?style=for-the-badge&logo=javascript&logoColor=black" alt="JavaScript" />
  <img src="https://img.shields.io/badge/HTML5-E34F26?style=for-the-badge&logo=html5&logoColor=white" alt="HTML5" />
  <img src="https://img.shields.io/badge/CSS3-1572B6?style=for-the-badge&logo=css3&logoColor=white" alt="CSS3" />
  <img src="https://img.shields.io/badge/React_Native-20232A?style=for-the-badge&logo=react&logoColor=61DAFB" alt="React Native" />
  <img src="https://img.shields.io/badge/Expo-000020?style=for-the-badge&logo=expo&logoColor=white" alt="Expo" />
</p>

<p align="center">
  <img src="https://img.shields.io/github/downloads/AureoFJunior/EvangelionERP-V1/total?style=for-the-badge" alt="Downloads" />
</p>

Evangelion ERP V2 is the backend for a modular ERP platform focused on small and mid-size business operations.
It centralizes users, customers, products, orders, billing, payable flows, NFe documents, and email routines in one API with clear module boundaries.

## Overview

This repository is organized as a modular monolith:

- `EvangelionERPV2.Web` hosts the API layer
- each business context has its own `Application`, `Domain`, and `Infra` projects
- `EvangelionERPV2.Shared` contains cross-cutting concerns and shared entities
- background jobs run in `EvangelionERPV2.Worker.Email`, `EvangelionERPV2.Worker.Order` and
  `EvangelionERPV2.Worker.Notification`

Main modules currently present (one `Application` project each, most with `Domain` and `Infra`
alongside):

- Ai — the assistant: tools, the write-confirmation flow, speech in and out
- Audit
- Bills — receivables, boleto, dunning, the daily panel
- Customer
- Email
- Employee — payroll, absences, vacations
- Enterprise
- NFe
- Notification — the bell: admin broadcasts plus alerts projected from the daily panel
- OpportunityRadar
- Order — quotes and orders
- Pdv — the till
- Product
- Reports
- Supplier
- User

Surfaces without a module of their own live in the Web layer over one of the above: the dashboard,
cash-flow forecast, bank reconciliation, payable bills and sales performance.

**Running it?** [Running with Docker Compose](#running-with-docker-compose). That is the only
supported setup today — nothing runs in AWS, and production is deferred by choice.

**Deploying it later?** [Deploying to production](#deploying-to-production) is the runbook, written
for the day that happens. The full plan, the ordered checklist and the measurements behind it live
in the working specs under `docs/`, which are kept out of the repo — ask the maintainer for them.

## Tech stack

- .NET 10 / ASP.NET Core Web API
- Entity Framework Core 10 (SQL Server)
- Serilog
- Prometheus metrics (`/metrics`)
- SignalR — `/orderHub` (orders, and the notification bell) and `/aiHub` (the assistant's progress ticker)
- JWT + Refresh Token + Google login
- Redis cache
- RabbitMQ integration (workers consume from broker)
- AI assistant: a local model through Ollama (host-native), with optional `whisper` (hearing) and
  `piper` (speaking) services for voice mode (`--profile voice`)
- AWS Secrets Manager (with `plain:` fallback for local runs)
- Docker + Docker Compose

## Architecture and request flow

### Layers per module

Each module follows a consistent layering model:

- `*.Domain`: entities, interfaces, and business rules
- `*.Application`: use cases, services, DI wiring
- `*.Infra`: repositories and data access implementations

### Shared infrastructure

`EvangelionERPV2.Shared` includes:

- `AppDbContext`
- shared entities and DTO helpers
- encryption/hash helpers
- AWS secret resolution utilities
- common abstractions used across modules

### End-to-end request flow

1. Request reaches a versioned route (`api/v{version}/{controller}/{action}`)
2. middleware logs request data and captures exceptions
3. auth, rate limiting, and validation are applied
4. controller delegates to application services
5. services use repositories + EF Core context
6. response is returned and timing/status are logged

## Security and observability

- Request logging middleware tracks: endpoint, caller, body, response time, status code
- JWT bearer authentication is enabled globally
- IP rate limiting via `AspNetCoreRateLimit`; public traffic should enter through nginx or another trusted proxy that sets `X-Real-IP`
- `/health` is public for liveness probes, and its results are also published as
  `evangelion_health_check_status` so a dependency going down can raise an alert
- `/metrics` on 8080 requires an authenticated user with the metrics read permission. nginx answers
  404 for it: scraping happens on the internal network, so leaving the public route open would mean
  one relaxed RBAC policy is the whole distance to a public metrics endpoint
- The API and all three workers also serve the same registry on **port 9101, unauthenticated and
  never published** — no host port mapping, no security-group rule. Prometheus scrapes that. The
  alternative was a never-expiring Supervisor token in a scraper's config file
- Metrics carry an `enterprise_id` label on the opportunity-radar gauges, so treat them as
  confidential. Never label a metric with a user id, email, CPF/CNPJ or document number: a
  time-series database cannot honour a per-subject deletion request
- Swagger UI is available only in `Development`

## Running with Docker Compose

`docker-compose.yml` holds every service. Two optional files sit beside it, each used only when you
name it with `-f`:

- `docker-compose.gpu.yml` — moves Whisper onto the GPU (see "Whisper on the GPU").
- `docker-compose.observability.yml` — Prometheus and Grafana (see "Metrics, dashboards and alerts").

The observability stack is a separate file rather than a compose profile on purpose. Its
`GRAFANA_ADMIN_PASSWORD` uses `${VAR:?}` so compose refuses to run without it, and compose
interpolates the whole file before profiles select anything — inside `docker-compose.yml` that would
break every plain `docker compose` command for anyone who has never heard of Grafana.

### First run on a new machine (after cloning)

From a clean machine, follow this order:

1. Install prerequisites:
   - Docker Desktop (or Docker Engine + Compose)
   - AWS CLI v2
2. Configure AWS credentials locally (must be able to read `evangelion/dev/*` secrets):

```powershell
aws configure --profile default
aws sts get-caller-identity --profile default --region us-east-1
```

3. Clone the repository and open a terminal in the repository root (`EvangelionERPV2`).
4. Create local env file:

```powershell
Copy-Item .env.local.example .env.local
```

5. Edit `.env.local` and confirm at least:
   - `AWS_CREDENTIALS_DIR=C:/Users/<your-user>/.aws` (absolute path)
   - `AWS_PROFILE=default` (or your profile)
   - `AWS_REGION=us-east-1`
6. Start API + nginx:

```powershell
.\scripts\local-up.ps1
```

7. Validate containers and health:

```powershell
docker compose --env-file .env.local ps
Invoke-WebRequest http://localhost:8082/health
```

8. Use this API base URL from frontend/mobile:
   - `http://localhost:8082/api/v1` (through nginx)

To stop:

```powershell
.\scripts\local-down.ps1
```

If you started workers too:

```powershell
.\scripts\local-down.ps1 -WithWorkers
```

To include the assistant's voice mode (the `whisper` and `piper` services), pass `-WithVoice` to
both scripts — see "the assistant's voice mode" below for what else it needs:

```powershell
.\scripts\local-up.ps1 -WithVoice
.\scripts\local-down.ps1 -WithVoice
```

Pass the same switches to `local-down.ps1` that you passed to `local-up.ps1`. The scripts run
`docker compose down --remove-orphans` against exactly the profiles named, so a service started
under a profile you leave out is removed as an orphan.

### Everything at once

Every profile, every service — API, nginx, SQL Server, all three workers, both voice services, and
the metrics stack. Naming no services starts everything the enabled profiles contain:

```bash
docker compose --env-file .env.local \
  --profile localdb --profile proxy --profile workers --profile voice --profile exporters \
  up -d --build
```

**No `-f` flags, deliberately.** `COMPOSE_FILE` in `.env.local` names every file this project uses:

```
COMPOSE_FILE=docker-compose.yml:docker-compose.gpu.yml:docker-compose.observability.yml
```

Set it once and every command sees the same files, which is what makes the `down` below match this
`up`. Passing `-f` **overrides** `COMPOSE_FILE` rather than adding to it, so a command with two `-f`
flags silently drops the third file — the GPU one, in this project, which leaves Whisper running on
the CPU with no error to tell you.

`.env.local` must set `GRAFANA_ADMIN_PASSWORD` or compose refuses to start at all; that is
deliberate, and there is no default.

PowerShell shortcut, which handles the file wiring for you:

```powershell
.\scripts\local-up.ps1 -WithWorkers -WithVoice -WithObservability
```

That brings up thirteen containers:

| Service | Profile | Port (host) | Needs |
|---|---|---|---|
| `evangelionerpv2` | *(always)* | `8080` | SQL Server; Ollama on the host for the agent |
| `nginx` | `proxy` | `8082` | the API |
| `sqlserver` | `localdb` | `14333` | — |
| `worker_order` | `workers` | — | the API, RabbitMQ |
| `worker_email` | `workers` | — | the API, RabbitMQ |
| `worker_notification` | `workers` | — | the API, Redis. No broker, no database |
| `whisper` | `voice` | `9000` | — |
| `piper` | `voice` | `9100` | its voice model in the `piper_voices` volume |
| `prometheus` | *(own file)* | `9090`, **loopback only** | nothing; scrapes the API and workers on `:9101` |
| `grafana` | *(own file)* | `3000` | Prometheus; `GRAFANA_ADMIN_PASSWORD` |
| `mssql_exporter` | `exporters` | — | SQL Server; `MSSQL_EXPORTER_*` |
| `redis_exporter` | `exporters` | — | Redis; `REDIS_EXPORTER_ADDR` |
| `rabbitmq_exporter` | `exporters` | — | RabbitMQ management API; `RABBITMQ_EXPORTER_*` |

The three exporters publish no host port: Prometheus scrapes them over the compose network, and each
one sits idle rather than failing when its `*_EXPORTER_*` variables are unset. Redis and RabbitMQ
themselves are not compose services here — they run outside, which is why the exporters need an
address rather than a `depends_on`.

Three things are **not** started by that command and the assistant is degraded or silent without
them:

1. **Ollama runs on the host**, not in Compose — see the section below. `systemctl --user status ollama`.
2. **The feature flags are off by default.** In `.env.local`:

```ini
EVA_AI_ENABLED=true          # POST /api/v1/ai/chat, otherwise 503
EVA_AI_SPEECH_ENABLED=true   # POST /api/v1/ai/transcribe (hearing), otherwise 503
EVA_AI_VOICE_ENABLED=true    # POST /api/v1/ai/speak (speaking), otherwise 503
EVA_AI_VOICE_NAME=pt_BR-faber-medium
```

3. **Migrations never apply themselves.** Nothing in the app calls `Database.Migrate()`, so a
   freshly built image against an older database answers 500 with `Invalid column name`:

```bash
./scripts/local-db-update.sh
```

First voice run on a machine downloads two models, once each, and they are cached in volumes
afterwards. Whisper pulls its own on the first transcription — **1.5 GB** for `medium`, into
`whisper_models` — so the first spoken question is slow and the ones after it are not. Piper's pt-BR
voice is not in its image at all (it ships English only) and has to be fetched by hand:

```bash
docker exec evangelionerpv2_piper python3 -c "
from piper.download import ensure_voice_exists, get_voices
ensure_voice_exists('pt_BR-faber-medium', ['/app/voices'], '/app/voices', get_voices('/app/voices'))"
```

Check the whole thing is actually up:

```bash
docker compose --env-file .env.local --profile localdb --profile proxy --profile workers --profile voice ps
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:8082/health      # nginx -> API
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:9000/v1/models   # whisper
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:11434/api/tags   # ollama, on the host
ollama ps                                                                   # model resident?
```

And stop everything, including the profile-gated services:

```bash
docker compose --env-file .env.local \
  --profile localdb --profile proxy --profile workers --profile voice \
  down --remove-orphans
```

**Pass the same profiles to every command.** `down --remove-orphans` deletes containers belonging to
profiles you did not name, so a `down` without `--profile voice` silently removes `piper` and
`whisper`, and voice then fails with `Name or service not known (piper:8000)` until they are started
again. A restart policy does not help: those containers were removed, not stopped.

### Prerequisites

- Docker Desktop (or Docker Engine + Compose)
- available host ports, depending on the profiles you start: `8080` (API), `8082` (nginx, `proxy`),
  `14333` (SQL Server, `localdb`), `9000` (whisper) and `9100` (piper), both `voice`. Ollama is on
  the host at `11434`
- AWS CLI configured locally (`aws configure`) with a profile that can read the `evangelion/dev/*` secrets

### Local environment

1. Create local env file:

```powershell
Copy-Item .env.local.example .env.local
```

2. Adjust `.env.local`:
   - `AWS_CREDENTIALS_DIR` to your local AWS folder (for Windows, usually `C:/Users/<your-user>/.aws`)
   - `SQLSERVER_SA_PASSWORD` to a strong local password
   - keep `EVA_WORKER_SELF_API_AUTH`, `EVA_RABBITMQ_*`, `EVA_TOKEN_KEY`, and `EVA_ENCRYPTION_KEY` pointed at the `evangelion/dev/*` AWS Secrets Manager references

3. Start local SQL Server + API (build locally):

```powershell
docker compose --env-file .env.local --profile localdb up -d --build sqlserver evangelionerpv2
```

4. Optional: start API + nginx reverse proxy:

```powershell
docker compose --env-file .env.local --profile localdb --profile proxy up -d --build sqlserver evangelionerpv2 nginx
```

5. Optional: include workers:

```powershell
docker compose --env-file .env.local --profile localdb --profile workers up -d --build sqlserver evangelionerpv2 worker_order worker_email worker_notification
```

Workers are profile-based. They do not start unless you pass `--profile workers`.

- `worker_order` and `worker_email` consume RabbitMQ queues and send the scheduled broadcasts.
- `worker_notification` calls `POST Notification/SweepAlerts` every 15 minutes, which recomputes each
  tenant's alerts and pushes `NotificationsChanged` on `/orderHub` only when the firing set changed.
  HTTP to the API plus Redis for its lease — no broker, no database.

6. Any subset works — name the services you want, and add `--no-build` to skip the build.

7. Optional: the assistant's voice mode (`whisper` hears, `piper` speaks):

```bash
docker compose --env-file .env.local --profile localdb --profile voice up -d --build sqlserver evangelionerpv2 whisper piper
```

Both are profile-gated (`--profile voice`, or `-WithVoice` on the scripts) and both need flags in
`.env.local`: `EVA_AI_SPEECH_ENABLED=true` and `EVA_AI_VOICE_ENABLED=true`. Without them the API
answers 503 on `/ai/transcribe` and `/ai/speak`.

`piper` exists because the device's own voice differs on every machine, and on a Linux desktop
without `speech-dispatcher` there is none at all. With it running, every platform hears the same
voice; if it is down the client falls back to the device. Audio is streamed both ways and never
written to disk.

The pt-BR voice is not in the image (it ships English only). Once per machine:

```bash
docker exec evangelionerpv2_piper python3 -c "
from piper.download import ensure_voice_exists, get_voices
ensure_voice_exists('pt_BR-faber-medium', ['/app/voices'], '/app/voices', get_voices('/app/voices'))"
```

Without the model Piper answers 200 with an empty MP3; the API detects that and answers 503 so the
client falls back rather than playing silence.

#### Whisper on the GPU

Worth doing — it is 6× faster and more accurate. Measured against sixteen synthesised pt-BR
sentences of this system's vocabulary, with noise mixed in to stand in for a microphone:

| Config | WER | Exact | Per clip | 9 s question |
|---|---|---|---|---|
| `small` on CPU (the original default) | 22.1% | 5/16 | 1.4 s | — |
| `medium` int8 on CPU | 14.0% | 7/16 | 1.9 s | 2.9 s |
| **`large-v3` int8_float16 on GPU** | **11.5%** | **8/16** | **0.29 s** | **0.45 s** |

One-time host setup (needs root):

```bash
sudo pacman -S nvidia-container-toolkit
sudo nvidia-ctk runtime configure --runtime=docker
sudo systemctl restart docker
docker run --rm --gpus all nvidia/cuda:12.4.0-base-ubuntu22.04 nvidia-smi -L   # should list the card
```

Then add this to `.env.local` so every compose command uses the GPU override without `-f`:

```ini
COMPOSE_FILE=docker-compose.yml:docker-compose.gpu.yml
```

`.env.local` is gitignored, so this stays on the machines that have a card.

**Do not apply the override without the toolkit.** The container starts anyway (`deploy` is advisory
outside swarm) with `cuda` set and no card behind it, then dies mid-transcription with
`Could not load library libcudnn_ops_infer.so.8`. The restart policy brings it back, so it looks
like a slow "entendendo" rather than a crash — `docker inspect -f '{{.RestartCount}}' evangelionerpv2_whisper`
is the tell.

Ollama and Whisper share the one card: `qwen3:14b` holds ~9.6 GB and `large-v3` at `int8_float16`
about 1.3 GB, leaving ~2.3 GB free. If that gets tight, `EVA_AI_SPEECH_MODEL=Systran/faster-whisper-medium`
is the lever.

Useful knobs (all optional, defaults in `docker-compose.yml`):

| Variable | Default | What it does |
|---|---|---|
| `EVA_AI_ENABLED` | `false` | The agent itself; without it every `/api/v1/ai/*` route answers 503 |
| `EVA_AI_ENDPOINT` | `http://host.docker.internal:11434` | Where the API looks for Ollama |
| `EVA_AI_MODEL` | `qwen3:14b` | The model the agent thinks with |
| `EVA_AI_SPEECH_ENABLED` | `false` | Hearing: `POST /ai/transcribe` |
| `EVA_AI_SPEECH_MODEL` | `Systran/faster-whisper-medium` | Drives the server *and* the `model` the API sends per clip — they must agree, or the per-request value wins |
| `EVA_AI_SPEECH_COMPUTE_TYPE` | `int8` | Quantisation. What makes `medium` affordable on CPU |
| `EVA_AI_SPEECH_CPU_THREADS` | `8` | Physical cores, not threads: 4 → 2.33 s, 8 → 1.86 s, 16 → 2.28 s on an 8-core 7800X3D |
| `EVA_AI_SPEECH_TTL` | `-1` | Never unload. The default 300 s made the first question after a pause pay a 1.8 s load |
| `WHISPER_HOST_PORT` | `9000` | Host port |
| `EVA_AI_VOICE_ENABLED` | `false` | Speaking: `POST /ai/speak` |
| `EVA_AI_VOICE_NAME` | `pt_BR-faber-medium` | Must exist in `docker/piper/voice_to_speaker.yaml` |
| `PIPER_HOST_PORT` | `9100` | Host port |

Two settings live in `AiSettings` rather than compose: `SpeechPrompt`, a list of this ERP's terms
sent with every clip (13.5% → 10.9% WER on clean speech; without it "estoque crítico hoje" came back
as "toque, que de cor"), and `SpeechVadFilter`, which drops silence before Whisper sees it — not for
accuracy, but because Whisper answers a *silent* clip with one phrase repeated until it runs out of
room.

Audio is transcribed in flight and never stored: no volume for it, the upload is streamed straight
through, and only the transcript is kept.

Both containers carry `restart: unless-stopped` and a healthcheck, so `docker ps` shows `(healthy)`
once they are serving. The healthcheck catches a container that is up but not answering; it does
**not** catch a process that crashes and is restarted fast enough to answer the next probe — for
that, `RestartCount` is still the tell.

The agent's *answers* come from Ollama, which runs **natively on the host** rather than in Compose
(the GPU is the point, and this avoids the NVIDIA container toolkit). It must be reachable from the
API container, which means binding beyond loopback:

```bash
OLLAMA_HOST=0.0.0.0:11434 OLLAMA_MODELS=/path/to/models ollama serve
```

Started by hand it dies with the terminal, the session, or a reboot — and the first symptom is the
assistant answering "está indisponível" with nothing wrong in the API logs. A user unit keeps it up
and restarts it if it falls over:

```ini
# ~/.config/systemd/user/ollama.service
[Unit]
Description=Ollama — local model server for the ERP agent
After=network-online.target

[Service]
# Both matter: without OLLAMA_HOST it binds loopback and the container cannot reach it; without
# OLLAMA_MODELS it starts with an empty model store and answers "model not found".
Environment="OLLAMA_HOST=0.0.0.0:11434"
Environment="OLLAMA_MODELS=/path/to/models"
# Never unload. The default is five minutes, so the first question after a break paid a ~9.6 GB cold
# load — and a client that gave up meanwhile aborted the load itself, which Ollama logs as a 499.
Environment="OLLAMA_KEEP_ALIVE=-1"
ExecStart=/path/to/ollama serve
Restart=always
RestartSec=3

[Install]
WantedBy=default.target
```

```bash
systemctl --user daemon-reload
systemctl --user enable --now ollama.service
```

It starts on login. For a machine that should serve without anyone logged in, add
`sudo loginctl enable-linger $USER`.

If `/api/v1/ai/chat` times out, check reachability before suspecting the model:

```bash
docker exec evangelionerpv2_web bash -c 'exec 3<>/dev/tcp/host.docker.internal/11434'
```

A hang there is the host firewall (allow the docker bridge to `11434`); *connection refused* means
Ollama is not running.

### Shortcut scripts (PowerShell)

From repository root:

```powershell
.\scripts\local-up.ps1
```

What it does:

1. validates AWS identity via `aws sts get-caller-identity`
2. runs `docker compose ... down --remove-orphans`
3. runs `docker compose ... up -d --build` with `localdb` and `proxy` profiles

Optional flags:

- `-WithWorkers` includes `worker_order`, `worker_email` and `worker_notification`
- `-WithOrderWorker` includes only `worker_order` (with `sqlserver + evangelionerpv2 + nginx`)
- `-WithVoice` includes `whisper` and `piper`
- `-WithObservability` includes `prometheus` and `grafana` — this one also adds
  `docker-compose.observability.yml` to the `down` in step 2, which is what stops that `down` from
  deleting a running metrics stack as an orphan. Needs `GRAFANA_ADMIN_PASSWORD` in the env file.
- `-NoBuild` skips image build
- `-SkipIdentityCheck` skips the AWS STS check
- `-EnvFile <path>` uses a different env file

**Pass the same flags to both scripts.** Step 2 above is a `down --remove-orphans`, and compose
deletes containers it was not told about — so a `local-up.ps1` without `-WithVoice` removes a running
whisper, and without `-WithObservability` removes Prometheus and Grafana along with the metrics
they had stored.

Stop shortcut:

```powershell
.\scripts\local-down.ps1
.\scripts\local-down.ps1 -WithWorkers
.\scripts\local-down.ps1 -WithOrderWorker
.\scripts\local-down.ps1 -WithWorkers -WithVoice -WithObservability   # everything
```

### EC2 environment

The production target is **one EC2 instance running the same compose file**, with RDS private in the
same VPC — not ECS. ECS is Phase 2, kept in terraform behind `var.enable_ecs` and triggered by
growth rather than by a date. Full reasoning, costs and the Phase 2 triggers are in the deploy spec
under `docs/`, kept out of the repo.

```powershell
Copy-Item .env.ec2.example .env.ec2
docker compose --env-file .env.ec2 `
  -f docker-compose.yml -f docker-compose.prod.yml `
  --profile workers --profile voice up -d --no-build
```

`docker-compose.prod.yml` adds Caddy on 80/443 and removes the API's published host port. Do **not**
add `--profile proxy` there: nginx is the local proxy and would fight Caddy for port 80. Do not add
`--profile localdb` either — the database is RDS.

## Deploying to production

**Not done yet, and not in progress** — production is deferred and dev runs on the compose stack
above. This section is the runbook for the day it changes; it is written down now so the ordering
is not re-derived under pressure.

The target is **one EC2 instance running this compose file**, with RDS private in the same VPC.
Terraform describes all of it and is validated but never applied. ECS is Phase 2, parked behind
`var.enable_ecs`.

Status and ownership live in the deploy spec under `docs/`, kept out of the repo.

### First deployment, in order

The ordering matters more than any individual step.

**1. Terraform state.** The bucket must exist before the first `init`:

```bash
aws s3api create-bucket --bucket evangelion-tfstate --region us-east-1
aws s3api put-bucket-versioning --bucket evangelion-tfstate \
  --versioning-configuration Status=Enabled

cd EvangelionERPV2.Web/terraform
terraform init
```

Import what already exists rather than letting terraform make a second copy:

```bash
terraform import aws_ecr_repository.evangelionerpv2 evangelionerpv2-repository   # if declared
```

**2. Create the infrastructure.**

```bash
terraform plan      # read it; this is the only review the instance and database get
terraform apply
```

Outputs give you the Elastic IP, the database endpoint, and the ARN of the secret RDS generated for
the master password.

**3. Create the prd secrets.** Seven are missing. Exact names and keys are in the spec (B4). Two
rules: the encryption keys must be **freshly generated, not copied from dev**, and key names must
match the configuration exactly — a multi-key secret returns an **empty string** for a key it cannot
find rather than falling back to the single value.

**4. Prepare the instance.**

```bash
aws ssm start-session --target <instance-id>

sudo apt update && sudo apt install -y docker.io docker-compose-v2 git
sudo usermod -aG docker ubuntu        # log out and back in
uname -m                              # aarch64 - the instance is Graviton
aws sts get-caller-identity           # must show the instance role, not a user

sudo git clone <repo> /opt/evangelion
cd /opt/evangelion && cp .env.ec2.example .env   # then fill CADDY_DOMAIN and CADDY_EMAIL
```

Leave `AWS_PROFILE` empty — the instance role supplies credentials from metadata.

**5. Apply migrations.** Nothing calls `Database.Migrate()`, so a new RDS instance is an empty
database and the app will answer 500 with `Invalid column name` until this runs:

```bash
cd /opt/evangelion && ./scripts/rds-db-update.sh
```

**6. Start it.**

```bash
docker compose --env-file .env \
  -f docker-compose.yml -f docker-compose.prod.yml \
  --profile workers --profile voice up -d

curl -f http://localhost/health      # before DNS, before TLS
```

**7. Point DNS** at the Elastic IP. Caddy issues the certificate on the first HTTPS request, which
needs ports 80 and 443 open and the name resolving.

**8. Wire up automatic deploys.** Add three repository secrets — `DEPLOY_INSTANCE_ID`,
`DEPLOY_APP_DIR` (defaults to `/opt/evangelion`), `DEPLOY_HEALTH_URL` — and a push to `release`
builds, pushes and restarts over SSM.

### Routine deploys

Automatic on push to `release`. By hand:

```bash
aws ssm start-session --target <instance-id>
cd /opt/evangelion
docker compose --env-file .env -f docker-compose.yml -f docker-compose.prod.yml \
  --profile workers --profile voice pull
docker compose --env-file .env -f docker-compose.yml -f docker-compose.prod.yml \
  --profile workers --profile voice up -d
```

Run `./scripts/rds-db-update.sh` first whenever the release contains a migration.

### Things that will bite

| Symptom | Cause |
|---|---|
| `Invalid column name` on every request | Migrations not applied. Step 5 |
| The migration script hangs | Run from a laptop. RDS is `publicly_accessible = false`; run it on the instance |
| Caddy never gets a certificate | 80/443 not open, or DNS not pointing at the Elastic IP yet |
| Deploy is green but nothing changed | Look at the SSM command output — the workflow prints stdout and fails on a non-`Success` status |
| A secret resolves to an empty string | Key name mismatch in a multi-key secret. There is no fallback once a secret has more than one property |
| `terraform destroy` refuses | Working as intended — `prevent_destroy` on the database |

### Tearing down

`terraformDestroy.yml` needs a typed `destroy-production` confirmation and a reviewer on the
`destroy` environment. It will still refuse to delete the database until `prevent_destroy` is
removed by hand, and a forced destroy leaves a final snapshot.

## Frontend base URLs

Use one of the following according to your runtime mode:

- API direct (local): `http://localhost:8080/api/v1`
- Through nginx (`--profile proxy`): `http://localhost:8082/api/v1`
- EC2 without nginx: `http://<EC2_PUBLIC_IP>:5000/api/v1`
- Frontend running inside the same Docker network: `http://evangelionerpv2:8080/api/v1`

## Metrics, dashboards and alerts

Prometheus and Grafana run beside the API from `docker-compose.observability.yml`. Everything is
provisioned from files in `docker/` — datasource, dashboards and alert rules — so a fresh clone gets
the same dashboards with no clicking. Edits made in the Grafana UI are overwritten on restart, which
is the point: a dashboard that exists only in somebody's browser is not a dashboard.

```bash
# .env.local must set GRAFANA_ADMIN_PASSWORD — there is no default, deliberately, and it is read
# from that file only because --env-file names it
docker compose --env-file .env.local up -d prometheus grafana
```

| | URL | Auth |
|---|---|---|
| Grafana | http://localhost:3000 | admin / `GRAFANA_ADMIN_PASSWORD` |
| Prometheus | http://127.0.0.1:9090 | none — and that is why it is bound to loopback |

Verified on Grafana 12.0.0: the datasource, all three dashboards and all ten alert rules provision
from these files with no manual step, and every rule evaluates `health=ok`.

### Where alerts go

`docker/grafana/provisioning/alerting/contactpoints.yml` declares one contact point holding an email
and a webhook receiver. Both are inert until configured, so the stack runs with neither. Set either
in `.env.local` and restart Grafana:

```bash
# email
ALERT_SMTP_ENABLED=true
ALERT_SMTP_HOST=smtp.gmail.com:587      # host:port - the host alone fails with "missing port in address"
ALERT_SMTP_USER=you@example.com
ALERT_SMTP_PASSWORD=xxxxxxxxxxxxxxxx    # Gmail shows App Passwords spaced; paste without spaces
ALERT_SMTP_FROM=you@example.com
ALERT_EMAIL_TO=alerts@example.com

# or a webhook, which needs no credentials at all
ALERT_WEBHOOK_URL=https://discord.com/api/webhooks/...
```

Test delivery without waiting for something to break:

```bash
curl -u admin:$GRAFANA_ADMIN_PASSWORD -X POST \
  http://localhost:3000/api/alertmanager/grafana/config/api/v1/receivers/test \
  -H 'Content-Type: application/json' \
  -d '{"receivers":[{"name":"t","grafana_managed_receiver_configs":[{"name":"t","type":"email","settings":{"addresses":"you@example.com"}}]}]}'
```

The contact point YAML uses plain `${VAR}`, never `${VAR:-default}` — Grafana does not expand
shell-style defaults, and an unexpanded value **fails provisioning and stops Grafana from starting**.
Defaults belong in `docker-compose.observability.yml`, where that syntax works.

Prometheus has no login of any kind, so it is published on `127.0.0.1` only, unlike every other
service in this project. `--web.enable-lifecycle` is deliberately off: it exposes an unauthenticated
`POST /-/reload`. The config lives in git; reloading it is a `compose restart`.

Dashboards, in `docker/grafana/dashboards/`:

- **API overview** — rate, errors and duration by controller, plus the two facts that explain most
  incidents here: a dependency that is down, and a database behind the branch.
- **.NET runtime** — CPU, working set next to managed heap, GC, thread-pool queue.
- **Business and workers** — bills emailed without a PDF, queue throughput and failures, radar rates.

Alerts are Grafana's own (no Alertmanager): ApiDown, DependencyUnhealthy, PendingMigrations,
HighErrorRate, LatencyP95, WorkerFailing. They fire and appear under Alerting, but **nothing is sent
anywhere until you configure a contact point** — Grafana's default needs SMTP credentials this repo
does not carry. That is a deliberate stopping point.

### Dependency exporters

SQL Server, Redis and RabbitMQ metrics, in the `exporters` profile. Redis is Upstash and RabbitMQ is
CloudAMQP, so these connect out over the internet rather than sitting beside the service — and they
need real credentials, not the `evangelion/dev/*` references the app resolves at runtime:

```bash
./scripts/exporter-env.sh >> .env.local      # reads the secrets with your AWS profile

docker compose --env-file .env.local --profile exporters up -d
```

| Exporter | Source | Gives you |
|---|---|---|
| `mssql_exporter` | the SQL Server container, or RDS | connections, deadlocks, batch rates, buffer cache |
| `redis_exporter` | Upstash over TLS | `redis_up`, clients, commands, hit ratio, memory |
| `rabbitmq_exporter` | CloudAMQP **management API** | per-queue depth, ready/unacked, publish and deliver rates |

Two things worth knowing. CloudAMQP does not expose the `rabbitmq_prometheus` plugin on `:15692`
(verified 2026-09-17), which is why the exporter reads the management API instead. And its shared
plans deny `/api/nodes`, so the `node` collector is dropped — leaving it in logs a 401 on every
scrape. There is consequently no `rabbitmq_up`; "can the app reach the broker" comes from the health
check and `evangelion_queue_depth`, which the application publishes itself.

Every variable in `docker-compose.observability.yml` uses an empty default rather than `${VAR:?}`.
That is deliberate: compose interpolates the whole file before profiles select anything, so one
`:?` would break every plain `docker compose` command for anyone without exporter credentials.

### TLS in production

Local development proxies through nginx on 8082 with no TLS. Production has no load balancer, so
the instance terminates TLS itself — **Caddy**, in `docker-compose.prod.yml`:

```bash
docker compose --env-file .env \
  -f docker-compose.yml -f docker-compose.prod.yml \
  --profile workers --profile voice up -d
```

Set `CADDY_DOMAIN` and `CADDY_EMAIL` in `.env`; certificates are obtained and renewed with no cron
and no second container. Note which profiles are absent: `localdb` (the database is RDS) and
`proxy` (nginx and Caddy would both want port 80).

`docker-compose.prod.yml` also removes the API's published host port, so 8080 does not listen on the
instance at all — the security group is not the only thing between the internet and an unencrypted
endpoint. `/metrics` and `/swagger` are refused at the edge, as they are in `nginx.conf`.

### Grafana Cloud (production)

Grafana Cloud's free tier stores **10 000 active series, keeps 14 days, and allows 3 users**. This
API alone does not fit: `http_request_duration_seconds` costs 19 series per label combination
(16 buckets, `+Inf`, `_sum`, `_count`) across 184 controller actions, which is 14 000–24 000 series
before anything else is counted.

So do not point Grafana Cloud at the raw endpoint. Keep full detail in the local Prometheus, which is
free and unlimited, and ship a filtered stream: `docker/prometheus/prometheus.yml` carries a
commented `remote_write` block with the keep-list, paired with the recording rule in
`docker/prometheus/rules/` that pre-aggregates latency by controller instead of by action. That lands
near 2 000 series. Per-action detail stays local, which is where anyone debugging is sitting anyway.

Use a **write-only (MetricsPublisher) token** from AWS Secrets Manager. A leaked write token poisons
data; a leaked read token exfiltrates every tenant's numbers.

`Program.cs` filters the prometheus-net Meter adapter so ASP.NET Core's own
`http_server_request_duration` — a second request histogram, labelled `http_route`, measuring what
`UseHttpMetrics` already measures — is not published. Left on, it costs roughly 19 series per route
and grows with the codebase. Count active series against a process that has been serving for a
minute, not one that just booted: these adapters connect lazily and a scrape taken seconds after the
first request understates the total by more than half.

The older approach — Grafana Cloud scraping `https://<alb>/metrics` with a bearer token — still works
and needs no infra change, but it puts `/metrics` on the public ALB and needs a long-lived Supervisor
token. Prefer the agent.

### Not built yet

- The ECS path: a Prometheus agent sidecar with `remote_write`. Deferred — the planned first
  deployment is a single EC2 instance, where the agent is just another compose service.

### Outbound mail

The application and all three workers send through one configured account rather than each tenant's
own. `EmailSettings__*` is injected from `EMAIL_SMTP_*` in `.env.local`:

```bash
EMAIL_SMTP_HOST=smtp.gmail.com     # host and port separately here, unlike ALERT_SMTP_HOST
EMAIL_SMTP_PORT=587
EMAIL_SMTP_USER=you@example.com
EMAIL_SMTP_PASSWORD=xxxxxxxxxxxxxxxx
```

Per-enterprise rows in the `Email` table are still read, but only as a fallback when the configured
account is absent — so a tenant sends from its own mailbox only when this is left empty. Note the
consequence: with it set, every tenant sends from the same address.

Passwords stored in that table are encrypted at rest; `EMAIL_SMTP_PASSWORD` is plaintext
configuration. Both work, because the send path tries each form.

## RabbitMQ note

`docker-compose.yml` does not start a RabbitMQ container.

`worker_order` and `worker_email` expect a reachable broker, configured through the
`evangelion/dev/rabbitmq-*` secret references in `.env.local.example`. `worker_notification` does
not — it speaks only HTTP to the API and Redis for its lease.

**Queue arguments are immutable.** The work queue is declared with `x-max-length` 100,000 and
`x-overflow reject-publish`; the dead-letter queue with a **30-day `x-message-ttl`**, the same
100,000 cap, and `x-overflow drop-head`. Changing any of them means draining and deleting the queue
on the broker and restarting — it is recreated on the next `SetupChannel`. A mismatch shows up as a
`406 PRECONDITION_FAILED` naming the offending argument, so the log tells you which one.

**Dead-lettered messages are recoverable, and now expire.** Nothing consumes `<queue>.dlq`, so
`POST /api/v1/Messaging/RedriveDeadLettered?queue=order|email&maxMessages=N` moves them back onto the
work queue with the retry count reset — Admin only, capped at 500 a batch. After 30 days the broker
discards them, so `evangelion_dlq_depth` and the `DeadLetterQueueNotEmpty` alert are the difference
between noticing and losing them.

## Stop and cleanup

Tear down everything the "Everything at once" command started — same files, same profiles:

```bash
docker compose --env-file .env.local \
  --profile localdb --profile proxy --profile workers --profile voice --profile exporters \
  down --remove-orphans
```

The same profiles as the `up`, and the same absence of `-f`. Volumes are kept — SQL Server data,
Prometheus history and Grafana's stored config all survive. Add `-v` only when you mean to lose them.

**Name every profile you started.** `down --remove-orphans` deletes containers belonging to profiles
you leave out, so a `down` without `--profile voice` removes `piper` and `whisper` — and voice then
fails with `Name or service not known (piper:8000)` until they are started again. A restart policy
does not help: they were removed, not stopped.

**A profile you leave out can also block the teardown rather than break something later.** Omitting
`--profile exporters` leaves the three exporters attached to the shared network, and compose then
refuses to remove it:

```
Network evangelionerpv2_custom_network  Resource is still in use
```

Nothing is broken when that happens; the command simply did not cover everything. Re-run it with the
missing profile, or list what is still attached:

```bash
docker network inspect evangelionerpv2_custom_network \
  --format '{{range .Containers}}{{.Name}}{{"\n"}}{{end}}'
```

Check what is left:

```bash
docker compose --env-file .env.local ps
docker ps
```

## Troubleshooting workers

### `Failed to resolve AWS credentials`

The container is resolving `evangelion/dev/*` secrets with no usable AWS identity. Same error
whether it surfaces at startup or on the login endpoint.

1. Check the host can reach AWS at all: `aws sts get-caller-identity`.
2. Confirm `.env.local` has `AWS_CREDENTIALS_DIR` pointing at a real `.aws` folder, `AWS_PROFILE`
   naming a profile that exists in its `credentials` file, and `EVA_AWS_SECRET_NAME=AWSCredentials`.
   Compose mounts that folder at `/home/app/.aws` and sets `AWS_SHARED_CREDENTIALS_FILE` /
   `AWS_CONFIG_FILE` for the non-root container.
3. Recreate rather than restart — a cached container keeps the old environment:

```bash
docker compose --env-file .env.local --profile localdb --profile proxy up -d --build --force-recreate sqlserver evangelionerpv2 nginx
```

### Password reset e-mail fails with Google OAuth logs

Cause:

- Docker containers cannot complete the interactive Google OAuth browser flow used by the old fallback path.
- Password reset e-mail delivery must use the configured SMTP sender credentials in Docker.

Fix:

1. Configure the sender with a valid SMTP password or Gmail app password.
2. Keep `EVA_ENABLE_INTERACTIVE_EMAIL_OAUTH=false` for Docker and EC2.
3. Rebuild the API after changing `.env.local` or sender settings:

```powershell
docker compose --env-file .env.local --profile localdb --profile workers up -d --build sqlserver evangelionerpv2 worker_order worker_email
docker compose --env-file .env.local logs --tail=200 evangelionerpv2
```

### Error: `None of the specified endpoints were reachable`

Cause:

- workers started, but RabbitMQ is not reachable at the configured endpoint.

Fix options:

1. Point `EVA_RABBITMQ_URI` / `EVA_RABBITMQ_*` to a reachable broker. `EVA_RABBITMQ_URI` is optional, but when set it must resolve to a full `amqp://...` or `amqps://...` URI; otherwise the app falls back to host/port settings.
2. Restart workers after fixing the RabbitMQ secret values:

```powershell
docker compose --env-file .env.local --profile workers up -d --build
docker compose --env-file .env.local logs --tail=200 worker_order worker_email
```

### Worker order retries after `User/LogInto` returns `401`

Cause:

- `worker_order` could reach the API, but `SelfAPIAuth` did not resolve to credentials for exactly one active user in an active enterprise.
- Local runs use the AWS dev self-auth secret; a local SQL database without that matching user will return `401`.
- Common AWS Secrets Manager formats:
  - secret value `{"log":"worker-user:worker-password"}` with `EVA_WORKER_SELF_API_AUTH=evangelion/prd/selfapilogin:log`
  - secret value `{"username":"worker-user","password":"worker-password"}` with `EVA_WORKER_SELF_API_AUTH=evangelion/prd/selfapilogin`

Fix:

1. Confirm the worker credential user exists, is active, and belongs to an active enterprise.
2. Confirm the user has the RBAC permission needed by the worker, currently `orders.create`.
3. Restart `worker_order` after changing the secret or `.env` value:

```powershell
docker compose --env-file .env.local --profile workers up -d --build worker_order
docker compose --env-file .env.local logs --tail=200 worker_order evangelionerpv2
```

## Database migrations

### Package Manager Console (Visual Studio)

```powershell
Set-DefaultProject EvangelionERPV2.Shared
Add-Migration MigrationName -Context AppDbContext -Project EvangelionERPV2.Shared -StartupProject EvangelionERPV2.Web -OutputDir Migrations
Update-Database -Context AppDbContext -Project EvangelionERPV2.Shared -StartupProject EvangelionERPV2.Web
```

### dotnet ef (CLI)

```powershell
dotnet ef migrations add MigrationName --project EvangelionERPV2.Shared --startup-project EvangelionERPV2.Web --context AppDbContext --output-dir Migrations
dotnet ef database update --project EvangelionERPV2.Shared --startup-project EvangelionERPV2.Web --context AppDbContext
```


### A fresh RDS database

Nothing calls `Database.Migrate()`, so a newly created RDS instance is an **empty database**: the
deploy succeeds, the container starts, health goes green, and every request touching a table answers
500 with `Invalid column name`. It looks like a broken deploy rather than a missing step.

```bash
# on the application instance, because RDS is not publicly reachable
aws ssm start-session --target <instance-id>
cd /opt/evangelion && ./scripts/rds-db-update.sh
```

It resolves the endpoint and the master credentials from AWS (RDS generates the password into
Secrets Manager), takes a pre-flight snapshot, generates the idempotent script and applies it with
`-I` — quoted identifiers on, without which every filtered index fails with Msg 1934.

Run it from a laptop and it will resolve the endpoint happily and then hang on the connection:
`publicly_accessible = false` means only the VPC can reach it.

## Tests

Seventeen test projects — one per module, plus `EvangelionERPV2.Test` for the cross-cutting rules
(RBAC policy coverage, tenant query filters, workers, health checks).

Run all tests in the solution:

```powershell
dotnet test EvangelionERPV2.sln
```

One module, which is what you want while working on it:

```bash
dotnet test EvangelionERPV2.AiModule.Test/EvangelionERPV2.AiModule.Test.csproj
```

`EvangelionERPV2.Test` is the one to run before merging anything that touches a controller or a
permission: `RbacControllerPolicyCoverageTests` fails when a new action has no policy row, and
`TenantQueryFilterTests` fails when tenant isolation changes shape.

## Important notes

- Sensitive settings are resolved via AWS Secrets Manager in local and non-local environments.
- `plain:` is still supported by `AWSKMSKeyProvider` for fallback/debug scenarios.
- If Swagger is not available, confirm `ASPNETCORE_ENVIRONMENT=Development`.

## Load testing with k6

The rig lives in `perf/` and drives the API through nginx. It is untracked work in progress — see
`perf/README.md` for the scenarios and what each measures.

### 1. Bring the stack up with the rate limit raised

Every request in a run arrives from one address, and nginx overwrites `X-Real-IP` with the real peer,
so the general 300/min rule is hit at roughly 5 requests a second — the limiter, not the API, becomes
what you measure. `docker-compose.perf.yml` raises it for the API container only:

```bash
docker compose --env-file .env.local -f docker-compose.yml -f docker-compose.perf.yml \
  up -d evangelionerpv2
```

`-f` here rather than `COMPOSE_FILE`, unlike everywhere else above: the override is wanted for this
one container and nothing else, and because the command names a single service and never sweeps
orphans, the files it drops cost nothing — no other container is touched.

**Do not use that override for the `rate_limit_guard` scenario** — that one exists to prove the
limiter still works, so it needs the normal limit.

### 2. Seed the data

```bash
python3 perf/seed.py                 # 500 customers, 1000 products, 5000 orders
python3 perf/seed.py --users-only    # just the four perf.* accounts, one per access level
```

Credentials are written to `perf/.env.perf`, which is git-ignored. `run.sh` sources it and passes
`ADMIN_*`, `MANAGER_*`, `SUPERVISOR_*`, `EMPLOYEE_*` and `PDV_*` into the container.

**For volume, not breadth**, use the bulk script instead. `seed.py` creates orders through the API,
one HTTP call each — right for 5,000, useless for 200,000:

```bash
docker exec -i evangelionerpv2_sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$SQLSERVER_SA_PASSWORD" -C -I -d EvangelionERPV2 \
  -i perf/seed-volume.sql
```

200,000 orders and boletos in one month, for exercising the monthly aggregates. Edit `@OrderCount`,
`@PeriodStart` or `@Cleanup` at the top of the file; re-running tops up rather than duplicating, and
`@Cleanup = 1` removes the fixture. **`-I` is required** — `Boleto` has a filtered index and the
insert fails without `QUOTED_IDENTIFIER ON`.

This volume is what exposed an `OutOfMemoryException` in `SalesPerformance/GetPerformance` and
787 MiB of growth in `Dashboard/Today`, neither of which was visible at 5,000 orders. Both now
aggregate in SQL.

### 3. Run a scenario

```bash
./perf/run.sh <scenario> <level>

./perf/run.sh read_light smoke
./perf/run.sh write_mixed load
./perf/run.sh rate_limit_guard smoke     # WITHOUT the perf override, see above
```

| Scenarios | Levels (VUs × duration) |
|---|---|
| `read_light`, `read_heavy`, `write_mixed`, `rbac_denied`, `pdv_sync`, `rate_limit_guard` | `smoke` 1 VU/60s · `baseline` 10 VUs/6m · `load` 50 VUs/12m · `stress` 300 VUs/15m · `soak` 30 VUs/1h |

Results land in `perf/results/<timestamp>-<scenario>-<level>/` as `k6.log` plus `resources.csv`
(`docker stats` sampled every 5s). Every scenario shares a floor of no 5xx and under 1% failures.

### Running against Grafana Cloud k6

Every load run so far came from one IP, so the per-IP rate limiter absorbed the traffic before
`OverloadProtection`'s concurrency limiter (400 permits in Production) ever saw it. Cloud generators
come from many addresses, which is the only way that limit gets exercised.

Get a token: **Grafana Cloud → Testing & synthetics → Performance → Settings → Access** →
**Personal token**. (A Stack token also works but additionally needs a `projectID` in the script's
cloud options, so personal is the simpler one.)

`K6_CLOUD_STACK` is the stack **slug or URL**, not a token — for `https://acme.grafana.net` it is
`acme`.

Put both in `perf/.env.cloud`, which is git-ignored and picked up automatically:

```bash
cp perf/.env.cloud.example perf/.env.cloud   # then paste the token and slug
./perf/run.sh read_heavy load                # results stream to the cloud
```

**Not `perf/.env.perf`** — `seed.py` rewrites that file from a fixed key list, so anything extra in
it disappears on the next seed.

For the run N2 actually needs, generated from cloud addresses rather than this machine, set
`K6_RUN_IN_CLOUD=1` and a publicly reachable `BASE_URL` (both are in the example file), or pass them
inline:

```bash
K6_RUN_IN_CLOUD=1 BASE_URL=https://app.evangelionerp.com/api/v1 ./perf/run.sh read_heavy load
```

`k6 cloud run` has no `--stack` flag and the runner uses a throwaway container, so it logs in and
runs in the same container — a `k6 cloud login` on your host would write a config file the container
never sees. The token is passed through the environment, so it stays out of `docker inspect` and
process listings.

Two things about the free tier worth knowing before you hit them:

- **One concurrent test.** A run interrupted with Ctrl-C can stay registered as active and block the
  next one with `Max concurrency of 1 reached`. Stop it from the run's page, or:
  `curl -X POST https://api.k6.io/loadtests/v2/runs/<id>/stop -H "Authorization: Bearer $K6_CLOUD_TOKEN"`
- **500 VUh a month.** A `stress` run at 300 VUs for 15 minutes is 75 VUh, so the budget is roughly
  six of those. Smoke and baseline cost almost nothing.

`K6_RUN_IN_CLOUD=1` refuses a `localhost` BASE_URL rather than failing obscurely halfway through —
cloud generators cannot reach your machine. Without a token nothing changes; runs stay local.

### What the rig does not cover

`write_mixed` drives `Order/InsertOrder`, which persists directly and **never publishes to RabbitMQ**.
The queue path is `Order/AddOrder` → `InsertOrderInQueue` → the broker, and no scenario exercises it —
so the message pipeline is currently unmeasured under load.

A queue scenario now needs **503** in its thresholds, not 500: a broker that will not accept a
publish raises `MessageBrokerUnavailableException`, which the exception middleware maps to
`503 Service Unavailable` with `Retry-After: 5`. That covers the broker being down, a dropped
connection, and the work queue hitting `x-max-length` and rejecting the publish — back-pressure
rather than a fault. The shared `no 5xx` floor would still read it as a failure, so such a scenario
needs its own.

While a run is in flight, the Grafana dashboards at `http://localhost:3000` show the same window from
the API's side — `resources.csv` is the container view, Prometheus is the application view.

