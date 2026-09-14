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

Main modules currently present:

- Bills
- Boleto
- Customer
- Email
- Enterprise
- NFe
- Order
- PayablesReceivables
- Product
- User

## Tech stack

- .NET 10 / ASP.NET Core Web API
- Entity Framework Core 10 (SQL Server)
- Serilog
- Prometheus metrics (`/metrics`)
- SignalR (`/orderHub`)
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
- `/health` is public for liveness probes
- `/metrics` requires an authenticated user with the metrics read permission
- Swagger UI is available only in `Development`

## Running with Docker Compose (single file)

This project now uses a single compose file: `docker-compose.yml`.

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

Every profile, every service — API, nginx, SQL Server, all three workers, and both voice services.
Naming no services starts everything the enabled profiles contain:

```bash
docker compose --env-file .env.local \
  --profile localdb --profile proxy --profile workers --profile voice \
  up -d --build
```

PowerShell shortcut for the same thing:

```powershell
.\scripts\local-up.ps1 -WithWorkers -WithVoice
```

That brings up eight containers:

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

On the very first voice run, download the pt-BR model into the volume (once per machine — the image
ships English voices only):

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
- available host ports (default: `8080`, `8082` and `14333`, depending on profiles)
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
- `worker_notification` runs the notification sweep: every 15 minutes it logs in with `SelfAPIAuth`
  and calls `POST Notification/SweepAlerts`, which recomputes each tenant's daily-panel alerts and
  pushes `NotificationsChanged` on `/orderHub` only when the set of firing alerts changed. It talks
  to the API over HTTP and to Redis for its lease — no database, no broker — so it is the one worker
  that needs neither RabbitMQ nor SQL Server. Only one replica runs a pass at a time; the lease key
  is `eva:lease:notification-sweep`.

6. Optional: start only `nginx` + `worker_order` (without `worker_email`):

```powershell
docker compose --env-file .env.local --profile localdb --profile proxy --profile workers up -d --build sqlserver evangelionerpv2 nginx worker_order
```

If images are already built locally:

```powershell
docker compose --env-file .env.local --profile localdb --profile proxy --profile workers up -d --no-build sqlserver evangelionerpv2 nginx worker_order
```

7. Optional: the assistant's voice mode (`whisper` + `piper`):

```powershell
docker compose --env-file .env.local --profile localdb --profile voice up -d --build sqlserver evangelionerpv2 whisper piper
```

`whisper` is the speech-to-text service the agent uses to hear a question, and `piper` is the
text-to-speech service it answers with. Like the workers they are profile-based and do not start
unless you pass `--profile voice` (or `-WithVoice` to the shortcut scripts). They also need
`EVA_AI_SPEECH_ENABLED=true` and `EVA_AI_VOICE_ENABLED=true` in `.env.local`; without those the API
answers `503` on `POST /api/v1/ai/transcribe` and `POST /api/v1/ai/speak` instead of hanging.

`piper` exists because the device's own voice is a different voice on every machine — and on a
Linux desktop without `speech-dispatcher`, no voice at all: `speechSynthesis` accepts the utterance,
says nothing, and never fires `onDone`. With `piper` running and `EVA_AI_VOICE_ENABLED=true`, the
client asks `POST /api/v1/ai/speak` for each sentence and every platform hears the same voice; with
it off, or if the container is down, the client falls back to the device for the rest of the
session. The rendered audio is streamed through the API and never written to disk or cached.

The pt-BR voice is not in the image, which ships English voices only. `docker/piper/voice_to_speaker.yaml`
maps the name in `EVA_AI_VOICE_NAME` to a model file, and the model is downloaded into the
`piper_voices` volume on first use:

```bash
docker exec evangelionerpv2_piper python3 -c "
from piper.download import ensure_voice_exists, get_voices
ensure_voice_exists('pt_BR-faber-medium', ['/app/voices'], '/app/voices', get_voices('/app/voices'))"
```

Without the model file Piper answers `200` with an empty MP3 rather than an error; the API detects
that and answers 503 so the client falls back instead of playing silence.

Both `whisper` and `piper` carry `restart: unless-stopped`. A synthesis or transcription process
that dies is a transient fault, not a reason to take voice out of the product until somebody runs
`docker start` by hand — and with the default policy nothing brings them back. Watch `RestartCount`
on those containers if you want to know whether they are dying quietly.

**Why `medium` at `int8`.** Measured 2026-09-14 against sixteen synthesised pt-BR sentences of this
system's own vocabulary, scored by word error rate, with noise mixed in to stand in for a real
microphone:

| Config | Noisy WER | Exact | Per clip |
|---|---|---|---|
| `small`, no prompt (the old default) | 22.1% | 5/16 | 1.4 s |
| `small` + VAD + vocabulary prompt | 20.8% | 4/16 | 1.5 s |
| `medium` float + VAD + prompt | 14.7% | 7/16 | 4.4 s |
| **`medium` int8 + VAD + prompt** | **14.0%** | **7/16** | **2.3 s** |

`int8` is the whole reason `medium` is affordable here: it keeps the accuracy and gives back half
the latency. The GPU is not an option — it is holding `qwen3:14b`, and Docker on this machine has no
GPU access without the NVIDIA container toolkit, which this setup deliberately avoids.

Two request-level settings come with it, both measured rather than assumed: `AiSettings__SpeechPrompt`
is a list of this ERP's own terms (worth 13.5% → 10.9% on clean speech — without it "estoque crítico
hoje" came back as "toque, que de cor"), and `AiSettings__SpeechVadFilter` drops silence before
Whisper sees it. The VAD filter buys no accuracy on speech; it is there because Whisper answers a
*silent* clip with one phrase repeated until it runs out of room, which is how a 667-character
question nobody asked reached the conversation history.

Audio is transcribed in flight and never stored — the container has no volume for it, the API
streams the upload straight through, and only the transcript is kept.

Useful knobs (all optional, defaults in `docker-compose.yml`):

| Variable | Default | What it does |
|---|---|---|
| `EVA_AI_SPEECH_ENABLED` | `false` | Turns the voice endpoint on |
| `EVA_AI_SPEECH_MODEL` | `Systran/faster-whisper-medium` | Whisper model. Drives both the server and the `model` the API sends with every clip — they must agree, or the per-request value silently wins |
| `EVA_AI_SPEECH_COMPUTE_TYPE` | `int8` | Quantisation. `int8` is what makes `medium` affordable on CPU |
| `EVA_AI_SPEECH_DEVICE` | `cpu` | `cuda` to put it on the GPU |
| `WHISPER_HOST_PORT` | `9000` | Host port for the transcription service |
| `EVA_AI_VOICE_ENABLED` | `false` | Turns `POST /api/v1/ai/speak` on; off keeps the device's voice |
| `EVA_AI_VOICE_NAME` | `pt_BR-faber-medium` | Which Piper voice speaks. Must exist in `docker/piper/voice_to_speaker.yaml` |
| `PIPER_HOST_PORT` | `9100` | Host port for the synthesis service |
| `EVA_AI_ENABLED` | `false` | Turns the agent itself on; without it every `/api/v1/ai/*` route answers 503 |
| `EVA_AI_ENDPOINT` | `http://host.docker.internal:11434` | Where the API looks for Ollama |
| `EVA_AI_MODEL` | `qwen3:14b` | The model the agent thinks with |

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
# Keep the model resident. Ollama unloads after five minutes idle by default, so the first question
# after any break paid a cold load of ~9.6 GB onto the GPU — and a client that gave up while that was
# happening aborted the load itself, leaving the user with "O assistente está indisponível" and
# nothing wrong in the API logs. Ollama logs that case as `499` with "client connection closed
# before llama-server finished loading".
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
- `-NoBuild` skips image build
- `-SkipIdentityCheck` skips the AWS STS check
- `-EnvFile <path>` uses a different env file

Stop shortcut:

```powershell
.\scripts\local-down.ps1
.\scripts\local-down.ps1 -WithWorkers
.\scripts\local-down.ps1 -WithOrderWorker
```

### EC2 environment

1. Create EC2 env file:

```powershell
Copy-Item .env.ec2.example .env.ec2
```

2. Pull published images:

```powershell
docker compose --env-file .env.ec2 pull
```

3. Start API + nginx (no local build):

```powershell
docker compose --env-file .env.ec2 --profile proxy up -d --no-build evangelionerpv2 nginx
```

4. Optional: include workers:

```powershell
docker compose --env-file .env.ec2 --profile workers up -d --no-build
```

### If you run commands from `EvangelionERPV2.Web`

Use parent paths:

```powershell
docker compose --env-file ..\.env.local -f ..\docker-compose.yml --profile localdb up -d --build sqlserver evangelionerpv2
```

## Frontend base URLs

Use one of the following according to your runtime mode:

- API direct (local): `http://localhost:8080/api/v1`
- Through nginx (`--profile proxy`): `http://localhost:8082/api/v1`
- EC2 without nginx: `http://<EC2_PUBLIC_IP>:5000/api/v1`
- Frontend running inside the same Docker network: `http://evangelionerpv2:8080/api/v1`

## Grafana Cloud metrics (Option A: direct scrape)

Use Grafana Cloud to scrape the API endpoint directly:

1. Expose `https://<your-domain-or-alb>/metrics`.
2. In Grafana Cloud scrape job, configure the target URL `/metrics`.
3. Configure the scrape job to send an authorization token for an account with metrics read access.
4. Keep access restricted at network level (security group / WAF / allowlist) when possible.

## RabbitMQ note

`docker-compose.yml` does not start a RabbitMQ container.

When `--profile workers` is enabled, workers expect a reachable broker configured through AWS Secrets Manager references or environment values.
For local runs, `.env.local` should use the `evangelion/dev/rabbitmq-*` AWS secret references shown in `.env.local.example`.

## Stop and cleanup

Stop default services from project root:

```powershell
docker compose --env-file .env.local --profile localdb down
```

or:

```powershell
.\scripts\local-down.ps1
```

Stop everything including profile services (`workers` and `proxy`) and remove orphans:

```powershell
docker compose --env-file .env.local --profile localdb --profile workers --profile proxy down --remove-orphans
```

If you run commands from `EvangelionERPV2.Web`:

```powershell
docker compose --env-file ..\.env.local -f ..\docker-compose.yml --profile localdb --profile workers --profile proxy down --remove-orphans
```

Check what is still running:

```powershell
docker ps
docker compose --env-file .env.local ps
```

If a worker container still appears in Docker Desktop after `down`, force remove it:

```powershell
docker rm -f evangelionerpv2_worker_order evangelionerpv2_worker_email
docker ps
```

## Troubleshooting workers

### Error: `Failed to resolve AWS credentials`

Cause:

- services are trying to resolve `evangelion/dev/*` secrets, but container has no usable AWS identity.

Fix:

- confirm `.env.local` has a valid `AWS_CREDENTIALS_DIR` path
- confirm the profile exists in `<AWS_CREDENTIALS_DIR>/credentials` (default `profile = default`)
- compose mounts that folder at `/home/app/.aws` and sets `AWS_SHARED_CREDENTIALS_FILE` / `AWS_CONFIG_FILE` for non-root containers
- check AWS access from host: `aws sts get-caller-identity`

### Error on login API: `Failed to resolve AWS credentials`

Cause:

- container started without AWS profile/credentials-file mapping.

Fix:

1. Confirm `.env.local` includes:
   - `EVA_CONN_STR=plain:Server=sqlserver,1433;...;TrustServerCertificate=True;Encrypt=False`
   - `EVA_AWS_SECRET_NAME=AWSCredentials`
   - `AWS_CREDENTIALS_DIR=.../.aws`
   - `AWS_PROFILE=default`
2. Rebuild web + nginx:

```powershell
docker compose --env-file .env.local --profile localdb --profile proxy up -d --build sqlserver evangelionerpv2 nginx
```

3. If old containers are still cached, recreate them:

```powershell
docker compose --env-file .env.local --profile localdb --profile proxy down --remove-orphans
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

## Tests

Run all tests in the solution:

```powershell
dotnet test EvangelionERPV2.sln
```

## Important notes

- Sensitive settings are resolved via AWS Secrets Manager in local and non-local environments.
- `plain:` is still supported by `AWSKMSKeyProvider` for fallback/debug scenarios.
- If Swagger is not available, confirm `ASPNETCORE_ENVIRONMENT=Development`.
