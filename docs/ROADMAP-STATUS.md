# Roadmap status — where the work stopped

Written 2026-08-19, rewritten 2026-08-24 after M14–M22 landed, on `feature/33-Boleto-and-NFE-emission`.
**Read this first when resuming.**

Two roadmaps, both now built out to their gates:

- **M0–M13, the system of record** — the Artifact **"Completing Evangelion ERP"**:
  <https://claude.ai/code/artifact/2edfd7e3-3715-4a0a-965c-299164b28e4a>
- **M14–M23, the daily-routine layer** — `~/.claude/plans/let-s-plan-more-needed-scalable-hare.md`

Neither is in the repo, and both list what deliberately must **not** be built (warehouses, double-entry
accounting, CRM pipeline, `PurchaseOrder`, NFCe, PIX, Quartz/Hangfire, multi-currency, a v2 API,
employee self-service, a global-search backend, price lists, a Notification table, XLSX). Proposing
any of them is re-litigating a settled decision.

## State of the tree

| | |
|---|---|
| Branch | `feature/33-Boleto-and-NFE-emission`, **not pushed** (no upstream) |
| Head | M20 duplicatas — see git log |
| Build | 0 errors (20 pre-existing `NU1510` warnings) |
| Tests | **1311 passing / 0 failing** across 14 assemblies (1164 at M13) |
| FrontV2 | `npx tsc --noEmit` clean, `npx jest` **77 passing** across 18 suites. Cypress still never run — it needs the Expo web server on :8081 and a live API |
| Migrations | chain ends `…110000_AddEnterprisePeriodLock` → `…120000_AddSupplierNFeAccessKey` → `…140000_AddPayableBillRecurrence` → `…150000_AddSupplierNFeDuplicata` |
| Commits | authored solely by Aureo — **no `Co-Authored-By` trailers** |

## Milestones

| # | Milestone | State | Commit |
|---|---|---|---|
| M0–M5 | Working trees, integrity, boleto receivable, stock ledger, tenant isolation, ops spine | **Done** | see git history |
| M6 | Real NFe issuance, phases 4 + 5 | **Code complete; unverified against SEFAZ** | `596a5d8`, `9d061f0` |
| M7 | NFe phases 7–9 | **Gated** — needs one real note to round-trip first | |
| M8 | Suppliers | **Done** | `57f69a9` |
| M9 | Per-tenant billing + CNAB | **Gated** on a bank | |
| M10–M11 | HR absences, férias ledger and férias pay | **Done** | `5b7ab82`, M11 P4/P5/P5a |
| M12 | Bank reconciliation | **Done** | `faccd6c` |
| M13 | Admin surfaces + test debt | **Done** | `a108bb1`, `8cd6a12` |
| M14 | Shared PDF renderer + Receipt/Adjustment ledger hole | **Done** | `60ae973` |
| M15 | Painel do dia | **Done** | `ec3bd72` (+ policy fix `1cf0f9b`) |
| M16 | Aging + extrato + inadimplência | **Done** | `72644d8` |
| M17 | Régua de cobrança + `DailySweepWorker` | **Done** | `e16bfd5` |
| M18 | Pacote do contador | **Done** | `b023f2c` |
| M19 | Fechamento de período | **Done** | `bc23832` |
| M20 | NFe de entrada (XML → payable) | **Done** | `af774a6` |
| M20b | DFe distribuição + manifestação | **Gated** — same certificate gate as M7 | |
| M21 | Limite de crédito + override | **Done** | `0e24765` |
| M22 | Contas fixas | **Done** | `61f6131` |
| M23 | Orçamento → pedido | **Not started — decide first** | |

## Resume here

**Everything buildable is built.** What remains needs a human decision or a thing only a human can buy:

1. **M23, orçamento** — the plan's own recommendation is to build it last and only if the business
   actually sends quotes today. If they draft orders as `Pending` and it works, this is a formatting
   feature wearing a milestone costume. It is also blocked in production by `M-B8` (Chromium, below).
2. **The A1 certificate** (`docs/fiscal/TODO-manual.md` §M1) plus an answer to §M13 (`infRespTec`
   carries the *software developer's* CNPJ and CSRT — it will cause a rejection on the first real
   transmission). Until one note round-trips, M6 stays unverified and M7 / M20b stay shut. Do **not**
   start M7 on an unverified cStat mapping: phases 5–9 all consume SEFAZ's answers.
3. **A bank**, for M9's CNAB remessa/retorno.
4. **Juros e multa** (`M-B12`) — not modelled anywhere. The dunning email states amount and due date
   and **no late fee** until you and the accountant set a policy.
5. **NFS-e** — does the pilot sell services? It changes the cut list and it is large.

## Needs a human, not code

1. **FrontV2 `abb1b46`** still carries a `Co-Authored-By` trailer and is **already pushed to
   `origin/master`**. Removing it means force-pushing a shared branch — a decision, not a cleanup.
2. **Chromium in the production image** (`M-B8`): `BuildBillAttachmentsAsync` swallows a renderer
   failure and mails the bill with no PDF, so this fails silently. Nobody has confirmed
   PuppeteerSharp's runtime download succeeds in the production network or survives a read-only
   filesystem. Inherited by M23, which would render quotes the same way.
3. ~~Redis connection string is broken in production.~~ **Checked 2026-08-24: it is not.** The
   claim survived in this file from an older state of the tree. There are three
   `AddStackExchangeRedisCache` calls, not eleven — `OrderIoC:85`, `ProductIoC:66`, `ReportsIoC:26` —
   and all three resolve `RedisSettings:InstanceName` and `:ConnectionString` through
   `AWSKMSKeyProvider.GetKMSKey`, exactly as `RedisLease` does. `appsettings.Production.json` holds
   Secrets Manager references (`evangelion/prd/redis:ConnectionString`), which is what that resolver
   expects, and `RabbitMQManagerBase` handles its six settings the same way. Nothing to fix.

   Two residual notes, neither a defect: the three registrations each add an `IDistributedCache`
   descriptor, so the last one registered wins and the other two are dead weight; and because the
   values are read inside the options callback rather than at registration, a Secrets Manager
   outage surfaces the first time something touches the cache, not at startup.

## Traps this session paid for — do not rediscover

- **Migrations do not apply themselves.** No `Database.Migrate()` call exists anywhere. Apply with
  `./scripts/local-db-update.sh` (it carries the two traps that cost a run: `dotnet ef database
  update` needs the app's Secrets Manager connection string, and `sqlcmd` without `-I` fails every
  filtered index with Msg 1934 — M20 and M22 both add one).
- **Migrations are hand-written.** Explicit `[Migration(...)]` or EF silently never sees it;
  `AppDbContextModelSnapshot.cs` is hand-edited. `AppDbContextModelSnapshotSyncTests` catches drift;
  `dotnet ef migrations list --no-connect` is the only check for a missing attribute.
- **Parallel agents must be given distinct migration timestamps.**
- **Tenant isolation is a model convention.** `AppDbContext.ApplyTenantQueryFilters` filters every
  entity carrying an `EnterpriseId`; a new module inherits it and must not add its own
  `HasQueryFilter`. `Guid.Empty` disables it, which is what keeps the workers and the login path
  working — and what the cross-tenant sweeps rely on.
- **Four gates fail loudly:** a new RBAC permission must appear in four places or startup throws;
  every new controller action needs an `[InlineData]` row in `RbacControllerPolicyCoverageTests`;
  every `[FromBody]`/`[FromForm]` type needs a validator; every tenant-owned entity needs its filter.
- **A defaulted bool on a service method is how a capability gets added without touching callers.**
  `CreateAsync(order, allowCreditOverride = false)` and `CreateAsync(bill, useAmbientTransaction,
  keepProvidedAmount)` both use it — and both broke every Moq setup of the old arity, which is a
  compile error in the test project, not a runtime surprise.
- **Commits carry no `Co-Authored-By` trailer.** Subagents must be told explicitly.

## What M14–M22 changed that later work should know

- **`Shared/Utils` is where a cross-module rule lives now.** `HtmlPdfRenderer` (M14), `Csv` (M18),
  `PeriodLock` (M19) and `ReceivableResolver` (moved out of Bills in M21, because the credit guard
  needed the same exposure figure and Order.Application must not reference Bills.Application).
- **`DailySweepWorker` is the one daily slot**, one Redis lease, one try/catch per job: dunning, then
  recurring payables. It no longer gates on `DunningSettings.Enabled` — dunning gates itself, and a
  cloned bill emails nobody. A third daily job belongs here, not in a new worker.
- **409 is the answer for a refused write.** `PeriodClosedException` and `CreditLimitExceededException`
  both map there in `ExceptionHandlingMiddleware`, and their messages are the only details echoed
  verbatim — they are built from dates and amounts this code chose, never from submitted values.
  FrontV2's `apiClient` reads `application/problem+json` and surfaces `detail`.
- **The period lock is enforced at three call sites**, not in `SaveChanges`: `OrderService`
  create/update/delete/refund, `PayableBillService` create/update/delete/refund, and
  `ProductService.ApplyStockMovementAsync`. Settling a boleto in a closed month is deliberately still
  allowed — the money arrived today. NFe issuance is **not** guarded: SEFAZ governs it.
- **Idempotency is an index, never a timestamp.** `DunningLog (EnterpriseId, BillId, OffsetDays)`,
  `PayableBill (EnterpriseId, SupplierNFeAccessKey)`, `PayableBill (RecurrenceParentId)`.

## Open follow-ons left by finished milestones

- **M14**: the payable-refund stock reversal now **throws** where it used to clamp at zero. Correct,
  but user-visible — it belongs in a release note.
- **M15**: the stock signal is cached 15 minutes; the panel can be that stale.
- **M18**: the package is built entirely in memory (`ponytail:`). Stream it if a tenant makes that hurt.
- **M19**: every guarded write costs one primary-key read of `Enterprise`; on the stock path that is
  one per order line (`ponytail:`).
- ~~**M20**: only the first duplicata is honoured.~~ **Closed 2026-08-24**: the import now creates one
  payable per duplicata, keyed `(EnterpriseId, SupplierNFeAccessKey, SupplierNFeDuplicata)`
  (migration `…150000`). Item lines ride the first instalment only — the goods arrive once. A nota
  with no schedule, or one whose duplicatas carry no amounts, is still a single bill for vNF.
  Still open there: an unknown emitter's supplier row is created before the bills, so a refused
  import can leave an orphan supplier the next attempt reuses.
- **M21**: two simultaneous orders that each fit under the limit but together do not will both pass.
  The limit is a policy, not a ledger invariant; serialising it would lock every order.
- **M22**: a long-dormant chain catches up one month per sweep, by design. Recurrence must travel on
  **every** payable update — a missing value means "stop repeating", which is how a chain is ended.
- **M11 P5**: abono settles with the first bill an accrual produces; days sold afterwards go unbilled
  (`ponytail:`, needs a column). A férias bill whose stretch is cancelled after issue is reported as
  an orphan. `PayableBillDetail` has no `EnterpriseId`.
- **M11 P5a**: `PayableBill.Amount` on a férias bill is the *netted* figure; whoever pays reads
  `GrossAmount`. M12's matcher and M18's export both already do.
- **M11 P4**: the approaching-deadline liability read has no controller action and no permission —
  `SPEC-hr-module.md` §8 puts that KPI out of scope.
- **Users**: `DeleteUser` is a **hard** delete, the only one in the system.
- **M6**: `NFeXmlBuilder` covers ICMS CST 00 and CSOSN 102/103/300/400 only; `natOp` and `tPag` are
  single constants because `Order` records neither.

## The gate every milestone runs

```bash
dotnet build EvangelionERPV2.sln
dotnet test  EvangelionERPV2.sln
dotnet ef migrations list \
  --project EvangelionERPV2.Shared/EvangelionERPV2.Shared.csproj \
  --startup-project EvangelionERPV2.Web/EvangelionERPV2.Web.csproj --no-connect
./scripts/local-db-update.sh
```

FrontV2: `npx tsc --noEmit && npx jest`. Run the `net-review` skill over each diff before merging.
