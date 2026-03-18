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
- background jobs run in `EvangelionERPV2.Worker.Email` and `EvangelionERPV2.Worker.Order`

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
- IP rate limiting via `AspNetCoreRateLimit`
- `/metrics` and `/health` are public endpoints
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

### Prerequisites

- Docker Desktop (or Docker Engine + Compose)
- available host ports (default: `5000` and `8082`, depending on profiles)
- AWS CLI configured locally (`aws configure`) with a profile that can read the `evangelion/dev/*` secrets

### Local environment

1. Create local env file:

```powershell
Copy-Item .env.local.example .env.local
```

2. Adjust `AWS_CREDENTIALS_DIR` in `.env.local` to your local AWS folder (for Windows, usually `C:/Users/<your-user>/.aws`).

3. Start API (build locally):

```powershell
docker compose --env-file .env.local up -d --build evangelionerpv2
```

4. Optional: start API + nginx reverse proxy:

```powershell
docker compose --env-file .env.local --profile proxy up -d --build evangelionerpv2 nginx
```

5. Optional: include workers:

```powershell
docker compose --env-file .env.local --profile workers up -d --build
```

Workers are profile-based. They do not start unless you pass `--profile workers`.

6. Optional: start only `nginx` + `worker_order` (without `worker_email`):

```powershell
docker compose --env-file .env.local --profile proxy --profile workers up -d --build nginx worker_order
```

If images are already built locally:

```powershell
docker compose --env-file .env.local --profile proxy --profile workers up -d --no-build nginx worker_order
```

### Shortcut scripts (PowerShell)

From repository root:

```powershell
.\scripts\local-up.ps1
```

What it does:

1. validates AWS identity via `aws sts get-caller-identity`
2. runs `docker compose ... down --remove-orphans`
3. runs `docker compose ... up -d --build` with `proxy` profile

Optional flags:

- `-WithWorkers` includes `worker_order` and `worker_email`
- `-NoBuild` skips image build
- `-SkipIdentityCheck` skips the AWS STS check
- `-EnvFile <path>` uses a different env file

Stop shortcut:

```powershell
.\scripts\local-down.ps1
.\scripts\local-down.ps1 -WithWorkers
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
docker compose --env-file ..\.env.local -f ..\docker-compose.yml up -d --build evangelionerpv2
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
3. Keep access restricted at network level (security group / WAF / allowlist) if exposure should be limited.

## RabbitMQ note

`docker-compose.yml` does not start a RabbitMQ container.

When `--profile workers` is enabled, workers expect a reachable external broker configured through secrets/environment values.
For local runs, define `EVA_RABBITMQ_*` variables in `.env.local` (see `.env.local.example`) and point them to a reachable broker (for example `host.docker.internal:5672`).

## Stop and cleanup

Stop default services from project root:

```powershell
docker compose --env-file .env.local down
```

or:

```powershell
.\scripts\local-down.ps1
```

Stop everything including profile services (`workers` and `proxy`) and remove orphans:

```powershell
docker compose --env-file .env.local --profile workers --profile proxy down --remove-orphans
```

If you run commands from `EvangelionERPV2.Web`:

```powershell
docker compose --env-file ..\.env.local -f ..\docker-compose.yml --profile workers --profile proxy down --remove-orphans
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
- check AWS access from host: `aws sts get-caller-identity`

### Error on login API: `Failed to resolve AWS credentials`

Cause:

- container started without AWS profile/credentials-file mapping.

Fix:

1. Confirm `.env.local` includes:
   - `EVA_CONN_STR=evangelion/dev/database:DefaultConnection`
   - `EVA_AWS_SECRET_NAME=AWSCredentials`
   - `AWS_CREDENTIALS_DIR=.../.aws`
   - `AWS_PROFILE=default`
2. Rebuild web + nginx:

```powershell
docker compose --env-file .env.local --profile proxy up -d --build evangelionerpv2 nginx
```

3. If old containers are still cached, recreate them:

```powershell
docker compose --env-file .env.local --profile proxy down --remove-orphans
docker compose --env-file .env.local --profile proxy up -d --build --force-recreate evangelionerpv2 nginx
```

### Error: `None of the specified endpoints were reachable`

Cause:

- workers started, but RabbitMQ is not reachable at the configured endpoint.

Fix options:

1. Point `EVA_RABBITMQ_URI` / `EVA_RABBITMQ_*` to a reachable broker.
2. Start a local RabbitMQ:

```powershell
docker run -d --name rabbitmq-local -p 5672:5672 -p 15672:15672 rabbitmq:3-management
```

3. Restart workers:

```powershell
docker compose --env-file .env.local --profile workers up -d --build
docker compose --env-file .env.local logs --tail=200 worker_order worker_email
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
