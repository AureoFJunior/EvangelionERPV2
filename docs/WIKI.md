EvangelionERPV2
===============

Comprehensive documentation for **EvangelionERPV2**, a modular ERP backend built on **.NET 10**.
This page is both onboarding material and long-term technical reference: architecture, domain rules,
configuration, deployment and development workflows.

Last verified against the working tree on **2026-08-17** (branch `feature/33-Boleto-and-NFE-emission`).

---

Table of Contents
-----------------

* [Project Overview](#project-overview)
* [Architecture and Modules](#architecture-and-modules)
* [Technology Stack](#technology-stack)
* [Domain and Business Rules](#domain-and-business-rules)
* [Data Model Overview](#data-model-overview)
* [API Overview](#api-overview)
* [Authentication, RBAC and Security](#authentication-rbac-and-security)
* [Background Processing and Workers](#background-processing-and-workers)
* [Configuration and Environment Variables](#configuration-and-environment-variables)
* [Running Locally](#running-locally)
* [Database Migrations and Testing](#database-migrations-and-testing)
* [CI/CD and Infrastructure](#cicd-and-infrastructure)
* [Logging, Metrics and Diagnostics](#logging-metrics-and-diagnostics)
* [Known Gaps and In-Flight Work](#known-gaps-and-in-flight-work)
* [Troubleshooting](#troubleshooting)
* [Contributing](#contributing)

---

Project Overview
----------------

**EvangelionERPV2** is a **modular monolith**: one API host, each business context isolated into its
own `Application` / `Domain` / `Infra` projects (64 projects in `EvangelionERPV2.sln`).

### Core capabilities

* CRUD for enterprises, users, customers, employees, products and orders
* Asynchronous order and email processing over RabbitMQ workers
* Billing: customer bills (boleto), payable bills, refunds, product-receipt flow
* **Payroll** — monthly per-employee payable bills with Brazilian employer charges
* **NFe / NFCe** fiscal documents — settings, A1 certificate lifecycle, document schema and
  number allocation (real SEFAZ transmission is still behind a flag, see
  [Known Gaps](#known-gaps-and-in-flight-work))
* **Cash-flow forecast** with scenario simulation
* **Opportunity Radar** — signal-based commercial recommendations
* Reports module and HTML/PDF report generation
* Audit trail with retention cleanup
* Multi-tenant (enterprise-scoped) access with RBAC permission policies

### Hosts

| Project | Role |
|---|---|
| `EvangelionERPV2.Web` | API host: controllers, security, middleware, validators, SignalR hub, Terraform, Dockerfile, nginx config |
| `EvangelionERPV2.Worker.Order` | RabbitMQ consumer for queued orders + scheduled broadcasts |
| `EvangelionERPV2.Worker.Email` | RabbitMQ consumer for outbound email |
| `EvangelionERPV2.Shared` | Cross-cutting: `AppDbContext`, entities, DTOs, migrations, auditing, encryption/AWS utils, repositories, hubs, DI |

---

Architecture and Modules
------------------------

### Layers per module

* `*.Domain` — module-specific interfaces, repository contracts, business rules
* `*.Application` — services (use cases), DI wiring (`DI/*IoC.cs`), queue managers, module settings
* `*.Infra` — data-access / integration implementations

Modules present today: **Audit, Bills (bills + payable bills + payroll + cash-flow forecast),
Customer, Email, Employee, Enterprise, NFe, OpportunityRadar, Order, Product, Reports, User.**

### Naming gotchas

* Folder names use `EvangelionERPV2.Order.Domain`, but **assembly/namespace use `Module`**:
  `EvangelionERPV2.OrderModule.Domain`. Match the `*Module.*` form in `using`/`namespace`.
* Shared **entities live in `EvangelionERPV2.Shared/Entities`**, not in each module's `*.Domain`.
* Some repository implementations sit in `*.Domain/Repositories` (Order, Customer, Employee,
  Enterprise, NFe), not `*.Infra` — check the module before assuming.
* `Employee` has **no `Infra` project**; payroll lives in **Bills**, not Employee.

### Cross-cutting concerns

* DI per module via `*IoC.Configure(services, configuration)`, all registered in
  `Web/Program.cs` → `ConfigureIoC`
* EF Core 10 (SQL Server) through `AppDbContext`
* RabbitMQ via `Shared/Utils/RabbitMQManagerBase`
* FluentValidation (validators registered in `Program.BuildValidators`)
* AutoMapper (`Shared/Configs/MapperConfig`)
* Redis distributed cache (registered in `ProductIoC`)
* Serilog logging, Prometheus metrics, global exception middleware
* Polly resilience helpers (`Shared/Utils/PollyHandler`)

### End-to-end request flow

1. Versioned route `api/v{version}/{controller}/{action}`
2. `ExceptionHandlingMiddleware` (outermost) → security headers → response compression
3. IP rate limiting (`AspNetCoreRateLimit`, trusts `X-Real-IP` from nginx) → CORS
4. `UseAuthentication` → `RequestLoggingMiddleware` → `ActiveTenantEnforcementMiddleware` →
   `UseAuthorization`
5. FluentValidation → controller → application service → repository + EF Core
6. Response returned; timing/status logged

---

Technology Stack
----------------

* .NET 10 / ASP.NET Core Web API (`global.json`: SDK `10.0.300` minimum, `rollForward: latestFeature`)
* Entity Framework Core 10 (SQL Server)
* Serilog + Prometheus (`/metrics`) + SignalR (`/orderHub`)
* JWT bearer + refresh token + Google login
* RBAC permission policies, reCAPTCHA verification
* Redis cache, RabbitMQ
* AWS Secrets Manager / KMS (`plain:` fallback for local runs), S3 for pictures
* Docker + Docker Compose + nginx, Terraform, GitHub Actions, SonarQube
* Tests: xUnit + Moq + coverlet

### Key packages

`Microsoft.EntityFrameworkCore.*`, `Microsoft.AspNetCore.Authentication.JwtBearer`,
`Microsoft.AspNetCore.Authentication.Google`, `AutoMapper`, `FluentValidation`,
`AspNetCoreRateLimit`, `prometheus-net.AspNetCore`, `Serilog`, `StackExchange.Redis`,
`RabbitMQ.Client`, `AWSSDK.*`, `Polly`.

---

Domain and Business Rules
-------------------------

### General

* Every entity inherits `BaseEntity` — `Id`, `CreatedAt`, `UpdatedAt`, `IsActive`
* **Soft delete only** (`IsActive = false`); no physical deletes
* All timestamps UTC; updates refresh `UpdatedAt`
* Every query is **enterprise-scoped** — services take `enterpriseId` and filter on it; the tenant
  gate in middleware rejects a token whose user/enterprise is no longer active
* Money columns are `decimal` (migration `ConvertMoneyColumnsToDecimal`), never `double`

### Orders

* Update requires loading the entity; missing → `NotFoundDatabaseException`
* Delete is soft delete
* Orders can be enqueued (`OrderRabbitMQManager.EnqueueAsync`); the worker posts them back to
  `/Order/InsertQueuedOrder` using a machine-only policy
* Refund flow: `POST /Order/RefundOrder/{id}` with a reason; refunds adjust the enterprise balance
  (`Shared/Utils/EnterpriseBalanceContributionHelper`)
* Monthly billing report (HTML) generated by `OrderReportGeneratorService`; broadcast endpoints are
  triggered by the workers on wall-clock slots (`Shared/Utils/BroadcastSchedule`) so a container
  restart does not re-fire a slot

### Bills (customer / boleto)

* `IBillsService` — `GetByOrderIdAsync`, `GenerateAsync`, `GetPdfAsync`, all enterprise-scoped
* One active bill per order (unique index `AddUniqueBillOrderIndex`)
* `BillsSettings` carries bank/beneficiary data: `BankCode` (default 237), beneficiary address,
  agency/account, wallet, `NossoNumeroLength`, instructions, default payer fallbacks
* `BillsSettings.Enabled` is the module kill switch; `AttachPdfToBillEmail` falls back to a
  digitable-line-only email when no headless browser is available

### Payable bills

* Full CRUD + `MarkProductsReceived`, `RefundPayableBill`, replenishment suggestions
* `EnumPayableBillType` classifies the bill (purchase, payroll, …)
* `PayableBillProduct` links purchased products; unique active index per bill/product

### Payroll (in the Bills module)

* `EnterprisePayrollSettings` per tenant: `InssRate` (0.20), `RatRate` (0.02), `ThirdPartyRate`
  (0.058), `FgtsRate` (0.08), `PayrollDueDay` (default 5, max 28)
* `GeneratePayroll` creates **one payable bill per employee** per competência (year/month), with a
  `PayrollBillDetail` row recording gross salary, worked days, 13th-salary provision (gross/12),
  vacation provision (gross/9), and INSS/RAT/third-party/FGTS amounts plus `TotalCost`
* Worked days = the month intersected with `[HireDate, TerminationDate]`; **absences do not yet
  reduce pay** (HR expansion is specified in `docs/employee/SPEC-hr-module.md`, not built)
* Payroll year is bounded to 2000–2100; rates are validated on upsert
* RBAC: `payroll.read`, `payroll.generate`, `payroll.settings.update` — **admin-only**

### Cash-flow forecast

* `GetForecast(horizonInDays)` and a balance-override variant project receivables (orders) against
  payables from the current enterprise balance
* `RunSimulation` applies scenario multipliers and persists a `ForecastSimulationLog`
* Guardrails: `MaxCurrentBalance = 1e15`, `MaxPayableMultiplier = 1000`, scenario name ≤ 100 chars

### NFe / NFCe

* `EnterpriseFiscalSettings` per tenant: CNPJ, IE/IM, razão social, CRT, full address with IBGE
  codes, environment (1 produção / 2 homologação), series per model, CSC id, schema version,
  cancellation windows (NFe 1440 min, NFCe 30 min), NFCe customer-identification threshold,
  fiscal defaults (CFOP intra/inter, CST/CSOSN, origem, ICMS/PIS/COFINS), contingency flags,
  and **certificate metadata only** (thumbprint, subject CN, `NotAfter`, uploaded-at)
* **A1 certificates live in AWS Secrets Manager**, one secret per enterprise under
  `NFeSettings.CertificateSecretPrefix`. `plain:` values are rejected in every environment — a
  base64 PFX in configuration is a leaked signing identity. Upload is rejected when the certificate
  CNPJ does not match the configured emitente
* Fiscal data resolution: **product column → enterprise default → hard validation error.** Never a
  guessed value (`NFeItemFiscalResolver`)
* `NFeStatus`: `Pending(1)`, `Authorized(2)`, `Cancelled(3)`, `Error(4)`, `Rejected(5)`, `Denied(6)`.
  `Error` is *our* failure and is retryable; a SEFAZ refusal is `Rejected`. `Denied` is terminal
* "Did it reach SEFAZ?" is answered by `TransmittedAtUtc`, not by the status — `IsOutcomeUnknown`
  is `Pending && TransmittedAtUtc != null`
* Issuance ordering is the safety mechanism: claim the row → allocate the number atomically
  (`NFeNumberSequence`) → build the access key locally and sign → persist key/number/XML and commit
  → set `TransmittedAtUtc` and commit → **only then** transmit. Blind retries either double-emit
  (real tax liability) or burn a number
* One active document per `(OrderId, Type)` — unique index

### Opportunity Radar

* Ingests `OpportunitySignal`s, produces `Opportunity` + `OpportunityRecommendation`, records
  `OpportunityRunLog` and `OpportunityFeedback`
* Config `OpportunityRadarSettings`: `Enabled` (false by default), `DefaultHistoryWindowDays` (180),
  `MinimumSampleOrders` (3)

### Products

* Soft delete; pictures stored in S3 (`ProductPicture`), read through Redis-backed caching
* List endpoints take `includePictures` so grids do not pay for base64 payloads

### Employees

* CRUD + picture upload. `Document` and `Salary` are **LGPD-sensitive**: withheld from `EmployeeDTO`
  (pinned by `EvangelionERPV2.Test/Security/EmployeeDtoLgpdSerializationTests.cs`) and `/Employee/*`
  write bodies are redacted in `RequestLoggingMiddleware`
* No `Employee ↔ User` link; no employee self-service

### Users and authentication

* JWT issued by `TokenService`; refresh tokens and password-reset tokens are entities with unique
  active indexes
* Google login: `LoginWithGoogle` (id-token) and `LoginWithGoogleCode` (code exchange)
* Password reset by numeric code e-mailed to the user
* Self-service endpoints: theme, language, profile picture, access level
* `FixedPortCodeReceiver` (interactive Edge/HttpListener OAuth) still exists for local delegated
  flows only — keep `EVA_ENABLE_INTERACTIVE_EMAIL_OAUTH=false` in Docker/EC2

### Audit

* `AuditTrail` records entity changes with an enterprise snapshot; `AuditTrailEntryFactory` builds
  the entries, `AuditTrailService` exposes paged queries and `CleanupRetention(retentionDays)`

---

Data Model Overview
-------------------

All entities inherit `BaseEntity`.

| Group | Entities |
|---|---|
| Tenancy / identity | `Enterprise`, `User`, `RefreshToken`, `PasswordResetToken` |
| Sales | `Customer`, `Order`, `OrderedProduct`, `Product`, `ProductPicture` |
| Finance | `Bill`, `PayableBill`, `PayableBillProduct`, `ForecastSimulationLog` |
| HR / payroll | `Employee`, `EnterprisePayrollSettings`, `PayrollBillDetail` |
| Fiscal | `EnterpriseFiscalSettings`, `NFeDocument`, `NFeNumberSequence` |
| Email | `Email`, `EmailStructure`, `BaseEmailSettings` |
| Radar | `Opportunity`, `OpportunitySignal`, `OpportunityRecommendation`, `OpportunityFeedback`, `OpportunityRunLog` |
| Audit | `AuditTrail` |

Enums: `EnumAccessLevel` (Admin, Manager, Supervisor, Employee), `EnumOrderStatus`,
`EnumPayableBillType`, `EnumReportType`, `EnumStockStatus`, `EnumUnitOfMeasure`, `EnumLanguage`,
`NFeDocumentType` (NFe=1, NFCe=2), `NFeStatus`.

### Indexing

Composite indexes are declared with EF Core attributes on the entities; the redundant per-column
`BaseEntity` indexes were dropped in `20260721120000_DropRedundantBaseEntityIndexes`. Uniqueness is
enforced for active rows only (order↔bill, order↔NFe type, active user identities, payable-bill
products).

### Repositories

Generic `IRepository<TEntity>` (in `Shared/Repositories`) provides sync/async CRUD, pagination
(`Shared/Utils/PaginationExtensions`), predicate filtering and transaction commit. Module
repositories add query methods on top.

---

API Overview
------------

Base route: `api/v{version}/{controller}/{action}`, default version `1.0`. Version can also be sent
as the `x-api-version` header or media-type parameter. JSON uses string enums and
`ReferenceHandler.IgnoreCycles`.

| Controller | Route base | Highlights |
|---|---|---|
| `UserController` | `/User/*` | `LogInto`, `LoginWithGoogle`, `LoginWithGoogleCode`, `RequestPasswordReset`, `ResetPassword` (all anonymous); users CRUD; `UpdateTheme`, `UpdateLanguage`, `UpdateProfilePicture`, `GetAccessLevel` |
| `EnterpriseController` | `/Enterprise/*` | CRUD, filter, `GetMyEnterprise` |
| `CustomerController` | `/Customer/*` | CRUD + paged filter |
| `EmployeeController` | `/Employee/*` | CRUD + paged filter + `UploadPicture`, `includePictures` toggle |
| `ProductController` | `/Product/*` | CRUD + paged filter + `UploadPicture` |
| `OrderController` | `/Order/*` | CRUD, filter, `InsertOrder`, `InsertQueuedOrder` (machine policy), `RefundOrder` |
| `BillsController` | `/Bills/*` | `GetByOrder`, `Generate`, `Pdf` |
| `PayableBillsController` | `/PayableBills/*` | CRUD, `MarkProductsReceived`, `RefundPayableBill`, `GetReplenishmentSuggestions` |
| `PayrollController` | `/Payroll/*` | `GetPayrollSettings`, `UpdatePayrollSettings`, `GeneratePayroll`, `GetPayroll` |
| `CashFlowForecastController` | `/CashFlowForecast/*` | `GetForecast/{horizonInDays}`, `GetForecastWithBalanceOverride`, `RunSimulation` |
| `NFeController` | `/NFe/*` | `GetByOrder`, `Issue`, `Consult`, `Cancel` |
| `NFeFiscalSettingsController` | `/NFeFiscalSettings/*` | `GetFiscalSettings`, `UpdateFiscalSettings`, `UploadCertificate`, `GetCertificateStatus`, `DeleteCertificate` |
| `OpportunitiesController` | `/opportunities` | list, get by id, `POST {id}/feedback`, `POST recompute`, `GET summary` |
| `ReportsController` | `/reports` | list, `GenerateReport`, get by id |
| `AuditTrailController` | `/AuditTrail/*` | paged list, filter, get by id, `CleanupRetention/{retentionDays}` |
| `EmailController` | `/Email/*` | `SendManualEmail`, `SendEmail`, `SendQueuedEmail`, `SendMonthEmail`, `SendStockEmail`, `AddEmail`, plus `SendMonthlyBillingBroadcast` / `SendWeeklyStockBroadcast` (machine policy) |

Pagination convention: `{pageNumber?}/{pageSize?}` on GET list actions;
`POST {descending}/{pageNumber?}/{pageSize?}` with a filter DTO body for filtered lists.

### Real-time

SignalR hub `OrderHub` at `/orderHub`, requiring `orders.read`. WebSockets / SSE / long-polling are
all enabled; the JWT may be passed as an `access_token` query parameter for this path only.

### Non-controller endpoints

* `GET /health` — anonymous liveness probe
* `GET /metrics` — Prometheus, requires the `metrics.read` permission
* `GET /swagger` — **Development only**

---

Authentication, RBAC and Security
---------------------------------

* JWT bearer is global via a **fallback authorization policy** — every endpoint requires an
  authenticated user unless it is explicitly `[AllowAnonymous]`
* Signing key comes from AWS (`AWSKMSKeyProvider.GetKMSKey`) using the `Encryption:TokenKey`
  reference; issuer, audience and lifetime are all validated with 1 minute of clock skew
* `ActiveTenantEnforcementMiddleware` rejects (401) any authenticated request whose user or
  enterprise is missing/inactive, so deactivating a tenant takes effect without waiting for
  token expiry
* Permission model: `RbacPermissions` (string constants), `RbacPolicies` (policy names,
  `rbac:<permission>`), `RbacRolePermissionMap` (role → permission set),
  `PermissionAuthorizationHandler` and `SelfOrPermissionAuthorizationHandler` (own record *or*
  the broader permission)

### Role tiers

| Role | Gets |
|---|---|
| **Employee** | health read, own profile read/update, orders read/create |
| **Supervisor** | Employee + metrics, audit read, bills, cash-flow read/simulate, customers read/create/update, NFe read/issue/cancel, order update/refund, full products, reports |
| **Manager** | Supervisor + customer delete, enterprise self-read (exposes `CurrentBalance`), **employees** (LGPD-sensitive: document, salary), NFe certificate **read**, payable bills, opportunities, order delete |
| **Admin** | everything, including payroll, fiscal settings, certificate upload/delete, user management, audit retention cleanup |

Machine-only endpoints (worker → API broadcasts, queued order insert) use
`RbacPolicies.Machine.SelfApiBroadcast` with credentials from `EVA_WORKER_SELF_API_AUTH`.

### Hardening

* Security headers on every response: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
  `Referrer-Policy: no-referrer`, a restrictive `Permissions-Policy`, and
  `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`
* HTTPS redirection always; HSTS outside Development
* Request body size capped at `RequestLimits:MaxRequestBodySizeBytes` (default 256 KB) on both
  Kestrel and IIS
* CORS: `Cors:AllowedOrigins` is **required outside Development** — startup throws if empty.
  Development falls back to localhost origins (19006, 8081, 3000, 8082)
* IP rate limiting via `IpRateLimiting` / `IpRateLimitPolicies` configuration
* reCAPTCHA (`RecaptchaVerifier`, 5 s timeout, fail-closed on auth endpoints)
* `RequestLoggingMiddleware` redacts sensitive bodies (auth, employee writes); request-body logging
  is off by default (`RequestLogging:EnableRequestBodyLogging`)
* `ControllerResponseSanitizer` supplies the generic `"Invalid request payload."` message so
  validation failures never echo internal details back to the caller
* Secrets come from AWS Secrets Manager; **never hardcode secrets**

---

Background Processing and Workers
---------------------------------

* RabbitMQ is the broker; the API enqueues, workers consume
* `EvangelionERPV2.Worker.Order` — consumes queued orders and posts them back through the API
  (`/Order/InsertQueuedOrder`) with machine credentials
* `EvangelionERPV2.Worker.Email` — consumes the email queue and sends via the configured SMTP sender
* Scheduled broadcasts (monthly billing, weekly stock) are anchored to wall-clock slots by
  `BroadcastSchedule`, so redeploys do not fire extra runs
* Compose does **not** start a RabbitMQ container — workers expect a reachable broker configured via
  AWS secret references

---

Configuration and Environment Variables
---------------------------------------

Configuration is a mix of `appsettings*.json` sections and `EVA_*` environment variables. Most
`EVA_*` values are **AWS Secrets Manager references** in the form `secret/name:jsonKey`, with a
`plain:` prefix supported for local runs.

### appsettings sections

| Section | Keys |
|---|---|
| `ConnectionStrings` | `DefaultConnection` (secret reference) |
| `Logging:LogLevel` | Default / Microsoft / Microsoft.Hosting.Lifetime |
| `RequestLogging` | `EnableRequestBodyLogging` (false) |
| `RequestLimits` | `MaxRequestBodySizeBytes` (262144) |
| `Cors` | `AllowedOrigins[]` (mandatory outside Development) |
| `AllowedHosts` | host allowlist, e.g. `app.evangelionerp.com;evangelionerpv2;localhost;127.0.0.1` |
| `JwtSettings` | `Issuer`, `Audience`, `AccessTokenMinutes` (60), `RefreshTokenDays` (7) |
| `Encryption` | `Key`, `TokenKey` (secret references) |
| `GoogleSettings` | `ClientId`, `ClientSecret`, `AllowedRedirectUris[]` |
| `RecaptchaSettings` | `Enabled`, `SecretKey`, `AllowedHostnames[]` |
| `SelfAPIAuth` | machine credential used by the workers |
| `AWSSettings` | `SecretName`, `BucketProducttName`, `BucketUserName` |
| `RedisSettings` | `InstanceName`, `ConnectionString` |
| `RabbitMQSettings` | `HostName`, `UserName`, `Password`, `VirtualHost`, `Port`, `Uri` |
| `OrderChannelSettings` / `EmailChannelSettings` | `QueueName` (`orderQueue` / `emailQueue`), `BatchSize` |
| `EmailSettings` | SMTP `HostName`, `Username`, `Password`, `Port`, `EnableInteractiveGoogleOAuthFallback` |
| `HttpConfig` / `Frontend` | `DefaultApiUrl`, `BaseUrl` (used in outbound links) |
| `IpRateLimiting` / `IpRateLimitPolicies` | `EnableEndpointRateLimiting`, `RealIpHeader` (`X-Real-IP`), `HttpStatusCode` (429), `GeneralRules[]` |
| `BillsSettings` | `Enabled`, `AttachPdfToBillEmail`, bank/beneficiary/wallet fields |
| `NFeSettings` | `Enabled`, `Environment`, `StateCode`, `Series`, `StartingNumber`, `CertificateSecretPrefix` |
| `OpportunityRadarSettings` | `Enabled`, `DefaultHistoryWindowDays`, `MinimumSampleOrders` |

### Environment variables (`.env.local.example`)

```
WEB_IMAGE / WORKER_ORDER_IMAGE / WORKER_EMAIL_IMAGE
WEB_HOST_PORT=8080   NGINX_HOST_PORT=8082   SQLSERVER_HOST_PORT=14333
ASPNETCORE_ENVIRONMENT=Development
SQLSERVER_SA_PASSWORD=...
EVA_CONN_STR=plain:Server=sqlserver,1433;Database=EvangelionERPV2;...
EVA_REDIS_INSTANCE / EVA_REDIS_CONNECTION
EVA_ENCRYPTION_KEY / EVA_TOKEN_KEY
EVA_AWS_SECRET_NAME=AWSCredentials
EVA_AWS_PRODUCT_BUCKET / EVA_AWS_USER_BUCKET
EVA_ENABLE_INTERACTIVE_EMAIL_OAUTH=false
EVA_WORKER_SELF_API_AUTH
EVA_RABBITMQ_HOSTNAME / _USERNAME / _PASSWORD / _VHOST / _PORT / _URI
AWS_CREDENTIALS_DIR / AWS_PROFILE / AWS_REGION / AWS_EC2_METADATA_DISABLED / AWS_SDK_LOAD_CONFIG
```

Fiscal secrets follow `evangelion/{env}/nfe/cert/{enterpriseId}` (A1 certificate) and
`evangelion/{env}/nfe/csc/{enterpriseId}` (NFCe CSC).

---

Running Locally
---------------

### Prerequisites

* Docker Desktop (or Docker Engine + Compose)
* AWS CLI v2 with a profile able to read `evangelion/dev/*` secrets
* Free host ports `8080`, `8082`, `14333` (profile-dependent)
* .NET 10 SDK if running outside containers

### Docker Compose (single file `docker-compose.yml`)

```powershell
Copy-Item .env.local.example .env.local     # then edit AWS_* + SQLSERVER_SA_PASSWORD
.\scripts\local-up.ps1                       # SQL Server + API + nginx (localdb, proxy profiles)
.\scripts\local-down.ps1                     # stop  (-WithWorkers / -WithOrderWorker to include workers)
```

`local-up.ps1` validates AWS identity (`aws sts get-caller-identity`), tears down orphans, then
brings the stack up with `--build`. Flags: `-WithWorkers`, `-WithOrderWorker`, `-NoBuild`,
`-SkipIdentityCheck`, `-EnvFile <path>`.

Compose profiles:

| Profile | Services |
|---|---|
| `localdb` | `sqlserver` |
| `proxy` | `nginx` |
| `workers` | `worker_order`, `worker_email` (off unless requested) |

Raw compose equivalent:

```powershell
docker compose --env-file .env.local --profile localdb --profile proxy up -d --build sqlserver evangelionerpv2 nginx
```

### Base URLs

| Mode | URL |
|---|---|
| Through nginx (default local) | `http://localhost:8082/api/v1` |
| API direct | `http://localhost:8080/api/v1` |
| Inside the Docker network | `http://evangelionerpv2:8080/api/v1` |
| EC2 without nginx | `http://<EC2_PUBLIC_IP>:5000/api/v1` |

Health: `http://localhost:8082/health`. Swagger (Development only): `/swagger`.

### EC2

```powershell
Copy-Item .env.ec2.example .env.ec2
docker compose --env-file .env.ec2 pull
docker compose --env-file .env.ec2 --profile proxy up -d --no-build evangelionerpv2 nginx
```

### Running from source

```bash
dotnet restore
dotnet build EvangelionERPV2.sln
dotnet run --project EvangelionERPV2.Web
dotnet run --project EvangelionERPV2.Worker.Order
dotnet run --project EvangelionERPV2.Worker.Email
```

---

Database Migrations and Testing
-------------------------------

The `AppDbContext` and all migrations live in **`EvangelionERPV2.Shared`**; the startup project is
`EvangelionERPV2.Web`.

```bash
dotnet ef migrations add <Name> \
  --project EvangelionERPV2.Shared --startup-project EvangelionERPV2.Web
dotnet ef database update \
  --project EvangelionERPV2.Shared --startup-project EvangelionERPV2.Web
```

Migration cautions learned the hard way:

* **Never `git checkout` the model snapshot to redo a migration.** EF diffs the next migration
  against `AppDbContextModelSnapshot.cs`; reverting it makes EF re-emit tables that earlier
  migrations already create.
* Several migrations were hand-written (payroll, fiscal, index cleanups). Keep the snapshot in sync
  and read the existing file before adding another.
* **Verify the real schema before trusting `database update`** on an existing environment — the
  repo's migration chain has not necessarily been the source of truth for every deployed database.

### Tests

```bash
dotnet test EvangelionERPV2.sln                                        # whole suite
dotnet test EvangelionERPV2.NFeModule.Test/EvangelionERPV2.NFeModule.Test.csproj   # one module
```

Test projects: `Bills`, `Customer`, `Employee`, `NFe`, `OpportunityRadar`, `Order`, `Product`,
`Reports`, `User` module tests, plus the cross-cutting `EvangelionERPV2.Test`
(`Resilience/`, `Security/` — idempotency, tenant scoping, RBAC and LGPD serialization).
All test projects enable `ImplicitUsings` + `Nullable` and use a global `using Xunit;`.

---

CI/CD and Infrastructure
------------------------

| Workflow | Purpose |
|---|---|
| `runTests.yml` | restore → build (Release) → `dotnet test` on the whole solution (.NET 10.0.x) |
| `buildPushDeploy.yml` | Docker Buildx (multi-arch) images for web + both workers, Docker Hub and AWS ECR push (OIDC with access-key fallback), then ECS service deploy |
| `sonarqube.yml` | Static analysis |
| `terraformDestroy.yml` | Manual `terraform destroy` in `EvangelionERPV2.Web/terraform` (Terraform 1.2.6) |
| `updateTag.yml` | Version tag maintenance |

Terraform lives in `EvangelionERPV2.Web/terraform`; nginx config in `EvangelionERPV2.Web/nginx.conf`.

### Metrics scraping (Grafana Cloud)

Scrape `https://<domain>/metrics` with a token belonging to an account that has `metrics.read`, and
keep the endpoint restricted at network level (security group / WAF / allowlist).

---

Logging, Metrics and Diagnostics
--------------------------------

* Serilog configured in `Web/Logging/LogConfig`; workers have their own `*LogConfig`
* `RequestLoggingMiddleware` logs endpoint, caller, timing and status; bodies only when enabled and
  never for redacted routes
* `ExceptionHandlingMiddleware` is registered first so every failure produces a consistent payload
* Prometheus HTTP metrics via `UseHttpMetrics()` exposed at `/metrics`
* Health endpoint `/health` is anonymous and unauthenticated for load balancers

---

Known Gaps and In-Flight Work
-----------------------------

Tracked on branch `feature/33-Boleto-and-NFE-emission` (work item **AB#33**, commit prefix
`AB#33 - `). Detailed specs live under `docs/`.

### NFe / NFCe — phases

| Phase | State |
|---|---|
| 0 — Unimake.DFe feasibility gate | 3 of 4 checks green; the last needs a real A1 certificate |
| 1 — Fiscal settings CRUD | done |
| 2 — Certificate lifecycle | done |
| 3 — Document schema + numbering | done |
| 4 — Real NFe 55 issuance | started (library-agnostic slice only) |
| 5–9 — cancel/consult, NFCe, DANFE PDF + XML, email attachments, retire the stub | not started |

**Nothing issues a real note yet.** `StubNFeProvider` is still the registered `INFeProvider` and
`NFeSettings.Enabled` is `false` everywhere; the stub reports success without contacting SEFAZ,
which is exactly why the flag stays off.

Manual blockers (see `docs/fiscal/TODO-manual.md`): buying an e-CNPJ A1 certificate (no SEFAZ
sandbox certificate exists), IAM provisioning for `secretsmanager:GetSecretValue`, choosing the
pilot UF and tax regime, verifying per-UF cancellation windows, and confirming Chromium resolves in
the runtime image for PDF rendering.

Planned breaking changes: `nfe.cancel` moves Supervisor → Manager (a privilege reduction that needs
release notes), and `204` disappears from `NFeController` in favour of explicit statuses.

### Other open items

* HR expansion (atestados, férias, advertências) is specified in `docs/employee/SPEC-hr-module.md`
  but not implemented; absences do not yet reduce payroll worked days
* Architecture audit backlog: SOLID and hygiene tiers remain (query layer, indexes and
  money-to-decimal are done)
* `EvangelionERPV2.NFe.Infra` carries no SEFAZ library reference yet — only certificates and XML
  helpers

---

Troubleshooting
---------------

### `Failed to resolve AWS credentials`

The container has no usable AWS identity. Check `AWS_CREDENTIALS_DIR` in `.env.local` points at your
real `.aws` folder, that `AWS_PROFILE` exists in that folder's `credentials`, and that
`aws sts get-caller-identity` works on the host. Compose mounts the folder at `/home/app/.aws` and
sets `AWS_SHARED_CREDENTIALS_FILE` / `AWS_CONFIG_FILE`.

### Login endpoint fails with the same error

Confirm `EVA_CONN_STR`, `EVA_AWS_SECRET_NAME`, `AWS_CREDENTIALS_DIR` and `AWS_PROFILE`, then rebuild:

```powershell
docker compose --env-file .env.local --profile localdb --profile proxy up -d --build --force-recreate sqlserver evangelionerpv2 nginx
```

### Password-reset email tries an interactive Google OAuth flow

Containers cannot complete the browser flow. Configure the SMTP sender (or Gmail app password) and
keep `EVA_ENABLE_INTERACTIVE_EMAIL_OAUTH=false` in Docker and EC2.

### `None of the specified endpoints were reachable`

Workers started without a reachable RabbitMQ. Fix the `EVA_RABBITMQ_*` secret references or stop the
`workers` profile.

### Worker container survives `down`

```powershell
docker rm -f evangelionerpv2_worker_order evangelionerpv2_worker_email
```

### CORS / startup throws `Cors:AllowedOrigins must be configured`

Expected outside Development — set the allowed origins for that environment.

### Boleto or DANFE PDF renders locally but not in the container

`mcr.microsoft.com/dotnet/aspnet:10.0` ships no Chromium shared libraries. Either install them in the
image or set `BillsSettings.AttachPdfToBillEmail=false` to fall back to the digitable line.

---

Contributing
------------

* Follow the existing layering: controller → application service → repository. No EF queries in
  controllers, no cross-module repository access
* Add a new module by mirroring an existing Domain/Application/Infra triple and registering its
  `*IoC` in `Web/Program.cs` → `ConfigureIoC` (the `dotnet-module` skill scaffolds this)
* Every new endpoint needs an RBAC permission, a policy, a role-map entry, and a test
* Enterprise-scope every query; keep LGPD-sensitive fields out of DTOs and logs
* Run `dotnet test EvangelionERPV2.sln` before opening a PR; use the `net-review` skill before merge
* Work on feature branches; prefix commits with the work item (e.g. `AB#33 - `)
* Document behaviour changes, schema changes and required migrations in the PR description
