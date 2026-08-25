# Roadmap status — where the work stopped

Written 2026-08-19, rewritten 2026-08-24 after M14–M22 landed, extended 2026-08-25 with M24–M28,
on `feature/33-Boleto-and-NFE-emission`. **Read this first when resuming.**

Three roadmaps, all built out to their gates:

- **M0–M13, the system of record** — the Artifact **"Completing Evangelion ERP"**:
  <https://claude.ai/code/artifact/2edfd7e3-3715-4a0a-965c-299164b28e4a>
- **M14–M23, the daily-routine layer** — `~/.claude/plans/let-s-plan-more-needed-scalable-hare.md`
- **M24–M28, the selling layer** — `~/.claude/plans/i-want-some-new-cheerful-boole.md`

None is in the repo, and each lists what deliberately must **not** be built (warehouses, double-entry
accounting, CRM pipeline, `PurchaseOrder`, NFCe, PIX, Quartz/Hangfire, multi-currency, a v2 API,
employee self-service, a global-search backend, price lists, a Notification table, XLSX). Proposing
any of them is re-litigating a settled decision.

## State of the tree

| | |
|---|---|
| Branch | `feature/33-Boleto-and-NFE-emission`, **not pushed** (no upstream) |
| Head | `6a9b6cd` — the customer map. FrontV2 `7f3c3dc` |
| Build | 0 errors (20 pre-existing `NU1510` warnings) |
| Tests | **1386 passing / 0 failing** across 14 assemblies (1346 at M23) |
| FrontV2 | `npx tsc --noEmit` clean, `npx jest` **96 passing**. Cypress **not re-run since M23** — five new screens are unexercised |
| Migrations | chain ends `20260825120000_AddCustomerCoordinates`. The three M24–M28 migrations are **NOT applied anywhere** — the local container was down on 2026-08-25. Run `./scripts/local-db-update.sh` before touching the new screens or every one of them answers 500 with `Invalid column name` |
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
| M20 | NFe de entrada (XML → payable), one payable per duplicata | **Done** | `af774a6`, `f4a5cf3` |
| M20b | DFe distribuição + manifestação | **Gated** — same certificate gate as M7 | |
| M21 | Limite de crédito + override | **Done** | `0e24765` |
| M22 | Contas fixas | **Done** | `61f6131` |
| M23 | Orçamento → pedido | **Done 2026-08-24** — backend, screen and conversion; no PDF yet | `875a854`, FrontV2 `5d6f19b` |
| M24 | Custo e margem real | **Done 2026-08-25** | `68986a8`, FrontV2 `8df4213` |
| M25 | Metas + comissão de vendedores | **Done 2026-08-25** | `760eeba`, FrontV2 `a82ac5d` |
| M26 | Ficha do cliente (Customer 360) | **Done 2026-08-25** | `be4a34a`, FrontV2 `005de5d` |
| M27 | Busca global (Ctrl+K) + WhatsApp | **Done 2026-08-25** — FrontV2 only | FrontV2 `198186e` |
| M28 | Mapa de clientes | **Done 2026-08-25** | `6a9b6cd`, FrontV2 `7f3c3dc` |

## What M24–M28 added, and what they left open

Five features aimed at the person who *sells*, none of which re-litigates a settled cut — three of
them are the roadmap's own cut list turned into its recommended "honest 5%".

- **M24 custo e margem.** `Product.CostPrice` plus `OrderedProduct.UnitCost`, a **snapshot** taken at
  line creation so editing a cost never rewrites a past order's margin. `Shared/Utils/OrderMargin` is
  the one implementation; an unknown cost answers **null**, never zero, and one uncosted line makes
  the whole order unknown rather than reporting a margin that is far too good. Cost is gated behind
  the new `products.cost.read` (Manager+) and withheld in one funnel per controller;
  `OrderedProduct.UnitCost` is `[JsonIgnore]`d so it cannot leak through the order lines.
  **Open:** nothing backfills cost — every existing product is uncosted until somebody types it, so
  the margin columns read "—" until then. Quotes carry no margin.
- **M25 metas + comissão.** `User.CommissionRate` (decimal(5,4) — the money precision would round
  0.035 to 0.04) and a `SalesGoal` table. **Commission is paid on what was *received***, resolved
  through the new `ReceivableResolver.ResolveReceivedAmount`, not on what was sold.
  **Open:** a seller cannot see their own row — the whole screen is Manager+. The enterprise row is
  deliberately not the sum of the seller rows (a queued order has no seller).
- **M26 ficha do cliente.** One endpoint, `Customer/GetCustomerSummary`, composed entirely of reads
  that already existed. The financial block is nulled for a caller without `bills.read`.
  **Open:** it reuses `GetAgingAsync`, which scans the tenant's whole open carteira to read one
  customer's row (`ponytail:` — give that service a customer filter if a big tenant feels it).
- **M27 busca global + WhatsApp.** FrontV2 only, as the roadmap scoped it: `Ctrl/Cmd+K` fans out over
  the existing `*ByFilter` endpoints, and `utils/whatsapp.ts` builds `wa.me` links.
  **Open:** the palette switches module but does not yet open the specific record.
- **M28 mapa de clientes.** `Customer.Latitude/Longitude`, a bounding-box + Haversine `GetNearbyCustomers`,
  and a batch `GeocodePendingCustomers`. **Geocoding never runs on the write path** — saving a
  customer must not depend on a public API being up.
  **Open:** precision is CEP-level via BrasilAPI, not street-level. **One new npm dependency,
  `leaflet`**, web-only and `require`d lazily so native never loads it; native shows the ranked list.
  Nothing has verified the OSM tile host is reachable from the production network.

## What still needs doing

Nothing here is half-built: every milestone is either finished or waiting on something outside the
code. In rough order of what unlocks the most.

**M23 landed without its answer.** You said to continue the roadmap, so it was built rather than
left pending: `Quote` + `QuoteItem` + `QuoteNumberSequence`, a screen, and conversion into an
ordinary order. If the business turns out not to send quotes, nothing else depends on it — the
tables are isolated and no financial read touches them. What it does **not** have yet is a PDF: a
quote you cannot send is half a feature, and the renderer is the one path `M-B8` still shadows in
production. That is the obvious next slice.

### A. Waiting on you — nothing moves until these are answered

| # | What | Why it blocks | Size once answered |
|---|---|---|---|
| A1 | **Buy the A1 certificate** (`TODO-manual.md` §M1) and finish credenciamento (§M4) | M6 is unverified and M7 + M20b are shut until one real note round-trips. Every `SefazCStatMap` entry is a guess until an autorizador produces it | the round trip itself is one session — `GO-LIVE-CHECKLIST.md` is the procedure |
| A2 | **The four `infRespTec` values**: your CNPJ, a contact name, e-mail, phone | Without them every real transmission is rejected under NT 2018.005. **No longer a code change** — fill `NFeRespTecSettings` in appsettings | minutes |
| A3 | ~~Does the business send quotes today?~~ | **Built anyway 2026-08-24.** If the answer turns out to be no, delete the module — nothing depends on it | done |
| A4 | **Juros e multa policy** (`M-B12`), with your accountant | The dunning e-mail says amount, due date and **no late fee**. Inventing a rate is a claim the ERP cannot back | S — a rate and a rule |
| A5 | **A bank** (`M-B1`: platform account or per-tenant?) | M9's CNAB remessa/retorno cannot start without the layout and the beneficiary decision | L |
| A6 | **Does the pilot sell services?** | NFS-e is per-municipality webservices, hundreds of incompatible ones. Changes the cut list | L if yes |
| A7 | **Confirm the pilot only issues plain sales** (§M14) | `natOp` and `tPag` are single constants because `Order` records neither. Wrong the moment devolução or a real payment breakdown matters | S — two fields on `Order` |

### B. Operational, before any of this is real

| # | What |
|---|---|
| B1 | ~~Apply the migrations.~~ **Done 2026-08-24** on the local container. Production is still untouched — nothing calls `Database.Migrate()` |
| B2 | **Push the branch.** `feature/33-Boleto-and-NFE-emission` has no upstream and carries M14–M28 plus the bug pass |
| B6 | **Apply the three M24–M28 migrations** (`20260825100000`, `110000`, `120000`). The container was down on 2026-08-25, so they exist only as files. `./scripts/local-db-update.sh` |
| B7 | **Re-run Cypress.** Five screens landed since the last pass — margin fields, the performance screen, the ficha, the command palette and the map — and none has been through a browser. That suite is the only gate that catches a hook called inside a branch |
| B3 | **Confirm Chromium in the *production* image** (`M-B8`). Partly answered 2026-08-24: in the **local** container `/Bills/Pdf` returns 200 with a real PDF, so PuppeteerSharp works there. Production is a different image and a different network, and `BuildBillAttachmentsAsync` still swallows a renderer failure and mails the bill with no PDF |
| B4 | ~~Run Cypress once.~~ **Done 2026-08-24 — and it earned its keep.** 16/30 on the first run, **27/30** now. It found four app bugs (see below). One test remains flaky: the two Products specs share a single seeded product, so whichever runs second finds it already deactivated and `GetProductsByFilter` answers 204. A fixture problem in the spec, not the app. **Run `./scripts/e2e-cleanup.sh --apply` after a session** — the suite can only soft-delete through the API, so every run leaves its seeds in the tables (204 rows after four runs, cleared 2026-08-24) |
| B5 | **Decide on FrontV2 `abb1b46`** — it carries a `Co-Authored-By` trailer and is already pushed to `origin/master`. Removing it means force-pushing a shared branch |

### C. Known debt — none of it silent, none of it urgent

Each is recorded with its reasoning in *Open follow-ons* below. The ones most likely to bite first:

- **M11 P5 abono** — days sold after the first bill go unbilled. Needs a column.
- **M20 orphan supplier** — a refused import leaves the supplier row it created; the resumable path
  reuses it, so this is untidy rather than wrong.
- **M21 credit-limit race** — two simultaneous orders that each fit but together do not both pass.
- **Users `DeleteUser`** is a hard delete, the only one in the system.
- **Three duplicate `IDistributedCache` registrations** — last one wins, the other two are dead weight.

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

- **Cypress seeds accumulate.** Teardown goes through the API, which only soft-deletes, so every
  run leaves its `E2E `-prefixed customers, products, employees and orders in the tables — invisible
  in the UI, in the way of anyone reading the database. `./scripts/e2e-cleanup.sh` removes them in
  foreign-key order (dry run by default, backup before it commits).
- **A cached read must be dropped on write.** `OrderRepository.GetByIdAsync` serves from Redis for
  an hour and nothing cleared it, so a soft-deleted order kept answering 200 with `isActive: true`
  while the database held `IsActive = 0` — and a refund or an edit read back stale the same way.
  `Update`/`UpdateRange`/`Delete` now drop the key (and never fail the write for a cache outage).
  Any future entity that gains a read cache needs the same override.
- **A business-rule refusal that gets rewrapped becomes a 500.** `OrderService.Delete` and `Update`
  caught their own `InsertDatabaseException` in the generic handler and rewrapped it with an inner,
  so "Orders with status Finished or Refund cannot be edited" reached the caller as an unhandled
  server error. Rethrow it unwrapped and let the controller map it to 400.
- **A React hook inside an early return crashes the screen, and only a browser sees it.**
  `useFeedbackToast` was being called inside `if (loading)`, inside `if (!canRead)` / `if (!isAdmin)`,
  and — worst — inside `renderBill`, a per-row callback, so React's hook count tracked the number of
  bills on screen. Nine components, seven Cypress failures, invisible to `tsc` and to jest because
  no unit test renders those screens through a state change. Cypress is the only gate that catches
  this class: **run it before believing a screen works**.
- **A `testID` on the compact layout only is a testID that does not exist.** `Orders` exposed
  `order-view-{id}` on the mobile card and nothing on the desktop table row, so nothing could open an
  order at a browser width.

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
- **`CustomerRepository.GetAllAsync` and `SupplierRepository.GetAllAsync` throw
  `NotFoundDatabaseException` on an *empty* result** instead of returning an empty list — and that
  exception maps to **204 No Content**, so a caller that treats "no rows" as normal silently answers
  nothing. It cost four shipped bugs (2026-08-24): the NF-e import could not create an unknown
  supplier, the accountant package 204'd for any tenant with no suppliers, the aging report did the
  same for a tenant with no customers, and the credit guard turned a missing customer into a generic
  create failure. **Use `GetPagedAsync` for these two entities** — the un-overridden path, no
  `Include`, no throw. Their test doubles now throw on empty too, so the next one fails a test.
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
  A half-finished import now resumes: only the missing duplicatas are created on the next attempt,
  and a nota imported before the key widened (null duplicata) is never re-entered as instalments.
  Still open there: an unknown emitter's supplier row is created before the bills, so a refused
  import can leave an orphan supplier — which the resumable path then reuses.
- **M21**: two simultaneous orders that each fit under the limit but together do not will both pass.
  The limit is a policy, not a ledger invariant; serialising it would lock every order.
- **M22**: a long-dormant chain catches up one month per sweep, by design. Both sweep queries are
  bounded by `RecurrenceUntilUtc >= today`, so an installation's finished chains do not make the
  morning pass read a growing table to do nothing. Recurrence must travel on
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
