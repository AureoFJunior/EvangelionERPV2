# Roadmap status — where the work stopped

Written 2026-08-19, updated 2026-08-20 (M6, then M11 P4, P5, P5a, then M13 across both trees), on `feature/33-Boleto-and-NFE-emission`. **Read this first when resuming.**

The plan itself is the Artifact **"Completing Evangelion ERP"** (M0–M13):
<https://claude.ai/code/artifact/2edfd7e3-3715-4a0a-965c-299164b28e4a>. It is not in the repo — fetch it
before planning, because it also lists what deliberately must **not** be built (warehouses,
double-entry accounting, CRM pipeline, `PurchaseOrder`, NFCe, PIX, Quartz/Hangfire, multi-currency,
a v2 API). Proposing any of those is re-litigating a settled decision.

## State of the tree

| | |
|---|---|
| Branch | `feature/33-Boleto-and-NFE-emission`, **not pushed** (no upstream) |
| Head at stop | M13 complete — backend and the FrontV2 admin screens |
| Build | 0 errors (20 pre-existing `NU1510` warnings) |
| Tests | **1164 passing / 0 failing** across 14 assemblies (1106 before P5a, 1117 before M13; the two new assemblies are the Audit/Email/Enterprise test projects) |
| Migrations | chain ends `20260820110000_AddEmployeeVacationAccrual` → `20260820130000_AddVacationPayLinks` → `20260820140000_AddVacationBillGrossAmount` |
| Commits | authored solely by Aureo — **no `Co-Authored-By` trailers**, verified 0 on the branch |

## Milestones

| # | Milestone | State | Commits |
|---|---|---|---|
| M0 | Close both working trees | **Done**, except the orphan projects below | `f0765ee`, FrontV2 `e70760b` |
| M1 | Integrity + validation holes | **Done** | `0bb2f2d` |
| M2 | Boleto becomes a real receivable | **Done** | `0a7e77b` |
| M3 | Stock ledger | **Done** | `f210e44`, `62d3d8f` |
| M4 | Tenant isolation by construction | **Done** | `939b055` |
| M5 | Ops spine (hosted service + Redis lease) | **Done** | `fc6f19d` |
| M6 | Real NFe issuance, phases 4 + 5 | **Code complete 2026-08-20; unverified against SEFAZ** | `596a5d8`, `9d061f0`, this branch's M6 commit |
| M7 | NFe phases 7–9 | Not started | |
| M8 | Suppliers | **Done** | `57f69a9` |
| M9 | Per-tenant billing + CNAB | Not started, gated on a bank | |
| M10 | HR — absences, atestados, advertências | **Done** | `5b7ab82` |
| M11 | HR — férias ledger and férias pay | **Done 2026-08-20** — P4, P5 and P5a (bill netting) | M11 P4, P5, P5a commits |
| M12 | Bank reconciliation | **Done** | `faccd6c` |
| M13 | Admin surfaces + test debt | **Done 2026-08-20** — statement-line resolution, first tests for Audit/Email/Enterprise, and the FrontV2 admin screens | `a108bb1`, `8cd6a12`; FrontV2 `07e4988`, `039cd2b`, `bcb2511` |

## Resume here: M6 verification, or the open follow-ons

**Every milestone M0–M13 is now built.** What remains is not a milestone; it is the fiscal work that
needs a human (buy the A1 certificate, §M1 in `docs/fiscal/TODO-manual.md`, and answer §M13) so that M6
can be verified against SEFAZ, plus the follow-ons listed further down. Do **not** start M7 until one real
note round-trips — phases 5–9 all consume SEFAZ's answers, and building on an unverified cStat mapping
compounds guesswork.

### What M13 turned out to be

- **Backend**: the bank statement resolution endpoints M12 left open, and first test projects for Audit,
  Email and Enterprise — the three modules that had none.
- **FrontV2**: three new screens — **user management** (admin-only), **audit trail** (Supervisor and up)
  and **opportunity radar** (Manager and up), each with its API client bindings and a test over its pure
  logic. The fourth screen the plan named, **enterprise admin, already existed** as `components/Settings.tsx`
  (company data, currency, balance, payroll rates, fiscal settings); it was not rebuilt.
- Also closed there: the five `features/` re-export shims M0's first pass missed, and the API client
  bindings for M6's customer fiscal address and M11's férias endpoints, which had been sitting
  uncommitted in the FrontV2 tree.

**FrontV2 gate**: `npx tsc --noEmit` clean, `npx jest` 31 passing across 9 suites. **Cypress was not run** —
`cypress.config.js` needs the Expo web server on :8081 and a live API on :8080 with a seeded admin, so it
only means anything against a running stack.

## Needs a human, not code

1. **Delete the three orphan root projects.** `EvangelionERPV2.Application`, `.Domain`, `.Infra` — a
   verified closed island: absent from the solution, and the only reference to `.Domain`/`.Infra`
   comes from `.Application`, which nothing references. `git rm -r` on them was blocked by the agent
   sandbox and needs to be run by hand.
2. **FrontV2 `abb1b46`** ("refactor(ui): split page modals into per-page components") still carries a
   `Co-Authored-By` trailer and is **already pushed to `origin/master`**. Removing it means
   force-pushing a shared branch — a deliberate decision, not a cleanup.
3. **Chromium in the production image** (M2's ops check, inherited by M7):
   `BuildBillAttachmentsAsync` swallows a renderer failure and mails the bill with no PDF, so this
   fails silently. Nobody has confirmed PuppeteerSharp's runtime download succeeds in the production
   network, or that its download path survives a read-only filesystem.
4. **Redis connection string is probably broken in production.** The 11 existing
   `AddStackExchangeRedisCache` calls pass `RedisSettings:ConnectionString` **raw**, but in production
   that value is a Secrets Manager reference, and IoC's `try/catch` swallows the failure. M5's lease
   resolves the secret properly; the cache registrations were left alone. Worth a ticket.

## Traps this session paid for — do not rediscover

- **Migrations do not apply themselves.** No `Database.Migrate()` call exists anywhere in the host or
  the workers, so writing a migration and committing it changes no database. The local stack ran nine
  migrations behind until 2026-08-20; `./scripts/local-db-update.sh` applies them (it also carries the
  two traps that cost a run: `dotnet ef database update` needs the app's Secrets Manager connection
  string, and `sqlcmd` without `-I` fails every filtered index with Msg 1934).
- **Migrations are hand-written.** No `dotnet-ef` tool, no design-time factory. Every migration needs
  an explicit `[Migration(...)]` attribute or EF silently never sees it, and
  `AppDbContextModelSnapshot.cs` is hand-edited. `AppDbContextModelSnapshotSyncTests` catches drift;
  `dotnet ef migrations list --no-connect` is the only check that catches a missing attribute.
- **Parallel agents must be given distinct migration timestamps.** Two agents independently stamped
  `20260819120000` and EF then ordered them alphabetically by accident.
- **Worktrees were created from a stale commit** (`58cf5b3`) every single time. Always verify the base
  and `git reset --hard` onto the branch head before starting.
- **Tenant isolation is a model convention now.** `AppDbContext.ApplyTenantQueryFilters` filters every
  entity carrying an `EnterpriseId`; a new module inherits it for free and must not add its own
  `HasQueryFilter`. `Guid.Empty` disables the filter, which is what keeps workers and the login path
  working. The ambient tenant is read through `IHttpContextAccessor` — never from context state,
  because EF caches the model and the filters close over whichever instance built it.
- **Four gates fail loudly if a new surface forgets something:** a new RBAC permission must appear in
  four places or startup throws; every new controller action needs an `[InlineData]` row in
  `RbacControllerPolicyCoverageTests`; every `[FromBody]`/`[FromForm]` type needs a validator
  (`ValidatorCoverageTests`); every tenant-owned entity needs its filter
  (`TenantQueryFilterTests.EveryTenantOwnedEntity_HasATenantFilter`).
- **Commits carry no `Co-Authored-By` trailer.** Aureo is the sole author. Subagents must be told
  explicitly, since their default instructions add one.

## Open follow-ons left by finished milestones

- **M12**: ~~no resolution endpoint~~ — **closed by M13**. `BankReconciliation/MatchLine` points a line at
  an Order or PayableBill; `IgnoreLine` clears a line that answers to no ERP document (a fee, a tax, a
  transfer between the company's own accounts) via the new `BankStatementLineStatus.Ignored`. Both sit
  behind `bank-reconciliation.resolve` at Manager. Manual resolution deliberately does **not** re-apply the
  amount-and-window rule — every line that reaches it is one that rule could not take — and it is
  reversible, so a mistaken judgement is correctable.
- **M3**: the stock ledger covers `Sale`/`Refund` only. Bills' goods receipt and its reversal still
  mutate `StorageQuantity` directly; `Receipt`/`Adjustment` exist in the enum and the shared method is
  ready. Also `PayableBillService._orderRepository` and `_orderedProductRepository` are now dead.
- **M10**: adding an absence after a competência is billed does not retroactively adjust the bill.
  Delete-and-regenerate is the existing escape hatch.
- **M11 P5**: `PayrollBillDetail` has no `EnterpriseId`, so it inherits no ambient tenant filter. The
  drawdown scopes by `EmployeeId` taken off a tenant-filtered accrual, which is sound — but anything else
  querying that table directly is on its own for isolation.
- **M11 P5**: abono settles with the first bill an accrual produces. `SoldDays` is a cumulative counter and
  nothing records how much abono a bill already paid, so days sold *after* the first bill go unbilled.
  Marked `ponytail:`; fixing it needs a column.
- **M11 P5**: a férias bill whose stretch is cancelled after issue is reported as an orphan rather than
  hidden. The money left; nothing reconciles that yet.
- **M11 P5a**: `PayableBill.Amount` on a férias bill is now a *forecast* figure — the netted one. Whoever
  pays the employee has to read `GrossAmount` (or `VacationPayDTO.TotalAmount`). No payment surface reads
  férias bills today, so nothing is wrong yet; a payment screen that treats `Amount` as the cheque would
  underpay by the provision. M12's matcher was fixed in the same pass — it now builds payable candidates
  from `GrossAmount ?? Amount`, because the bank sees the whole payment leave.
- **M11 P4**: the approaching-deadline liability read exists on `EmployeeVacationLapseService` but has
  **no controller action and no RBAC permission** — `SPEC-hr-module.md` §8 puts that KPI out of scope, so
  the endpoint would be scaffolding for a screen nobody asked for. One action away when a screen wants it.
- **M11 P4**: `EmployeeAbsence` has no FK to the accrual a férias belongs to; the link is the calendar
  ("starts inside this accrual's concessivo window"). Exact rather than heuristic, because consecutive
  concessivo windows are disjoint — but it is a join nobody can see in the schema.
- **M2**: `Rejected` is reachable in the model but nothing writes it yet — it exists so the CNAB
  retorno mapping in M9 does not have to migrate the column later.
- **M6**: `NFeXmlBuilder` supports ICMS CST 00 and CSOSN 102/103/300/400 only; every other code throws
  by name. Widening it needs the fields those codes require (`pRedBC`, ST margins, presumed credit) on
  `Product` first. `natOp` and `tPag` are single constants because `Order` records neither — §M14.
- **M6**: `CustomerDTO` returns the new fiscal address unmasked while `Document` stays `[JsonIgnore]`.
  Deliberate — the fiscal UI has to read back what it wrote — but if the XML download is Manager-tier
  for exactly this data, the customer read path may deserve the same tightening. RBAC decision.
- **M6**: audit redaction is keyed by property name, so redacting the customer address also redacts
  the emitente's, which shares the names. Judged correct; exempting one needs a per-entity rule.

## The gate every milestone runs

```bash
dotnet build EvangelionERPV2.sln
dotnet test  EvangelionERPV2.sln
dotnet ef migrations list \
  --project EvangelionERPV2.Shared/EvangelionERPV2.Shared.csproj \
  --startup-project EvangelionERPV2.Web/EvangelionERPV2.Web.csproj --no-connect
```

FrontV2: `npx tsc --noEmit && npx jest && npx cypress run`. Run the `net-review` skill over each diff
before merging.
