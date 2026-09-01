# Roadmap status — where the work stopped

Written 2026-08-19, rewritten 2026-08-24 after M14–M22 landed, extended 2026-08-25 with M24–M28,
extended 2026-08-28 with M29 (the till). **Read this first when resuming.**

Four streams, all built out to their gates:

- **M0–M13, the system of record** — the Artifact **"Completing Evangelion ERP"**:
  <https://claude.ai/code/artifact/2edfd7e3-3715-4a0a-965c-299164b28e4a>
- **M14–M23, the daily-routine layer** — `~/.claude/plans/let-s-plan-more-needed-scalable-hare.md`
- **M24–M28, the selling layer** — `~/.claude/plans/i-want-some-new-cheerful-boole.md`
- **M29, the till** — plans live in the *other repo*,
  `~/CodeProjects/EvangelionERPV2.PDV/docs/superpowers/plans/`, one per slice

None is in the repo, and each lists what deliberately must **not** be built (warehouses, double-entry
accounting, CRM pipeline, `PurchaseOrder`, NFCe, PIX, Quartz/Hangfire, multi-currency, a v2 API,
employee self-service, a global-search backend, price lists, a Notification table, XLSX). Proposing
any of them is re-litigating a settled decision.

## State of the tree

Measured 2026-09-01, not remembered.

| | |
|---|---|
| Branch | `feature/33-pdv-terminal-login`, **not pushed** (no upstream). **294 commits ahead of `origin/main`** |
| Head | `05b8d62` — an abono no bill could pay is refused at the source. FrontV2 `57fd4e6`, PDV `046e573`; working tree clean in all three. **Another session was committing to this branch in parallel on 2026-08-31** (the PIX commits `b2354a9`…`0415e4e`); re-read the log before trusting any head recorded here |
| Build | **0 errors, and 31 warnings down to 7 as of 2026-09-01** (`8629970`, `35ec606`). Most of the 31 were NU1510: five packages the framework already provides were referenced by hand in up to 32 projects each — `Microsoft.AspNetCore.SignalR` among them, pinned at **1.2.11**, a 2019 package, in 32 projects of which only `Shared`, `Order.Application` and `Web` use SignalR at all. `Shared` already carries `<FrameworkReference Include="Microsoft.AspNetCore.App" />` and that flows to everything referencing it, so all of them came out with 0 errors. Behind them sat 20 more: `EF1002` on two raw-SQL sites that are safe and now say so (`RepositoryBase.LockForUpdateAsync` interpolates only the table name EF's own metadata just returned, and the id is a parameter; `PdvTestContext` builds DDL from the same metadata), a real `CS8604` in `OrderRepository.Update/Delete` where `entity?.Id` guarded the cache but then handed the same possibly-null entity to a non-nullable parameter (now `ArgumentNullException.ThrowIfNull`, which names the caller bug instead of surfacing it as an NRE inside EF), and the rest test-only nullability and xUnit analyzer nits. **The remaining 7 are all `CS0618`** — `Unimake.DFe` deprecating `NFe.InfNFe` in favour of `InfNFeField` — and are **deliberately left**: that is the signed fiscal XML, it has never been validated against a real autorizador (A1, §A1), and a warning sweep is the wrong moment to change how a document SEFAZ will check is built. **Measure with `--no-incremental`**: an incremental build replays no warnings and reports 0. *Earlier:* 0 errors as of 2026-08-28 — it was 1 until then. `2f8064c` added a required `PasswordLoginService` parameter to `UserController` and updated every call site but one, so `EvangelionERPV2.UserModule.Test` had not compiled since. `dotnet test` on the solution *reports success anyway*: it runs the 13 assemblies that did build and never mentions the one it skipped |
| Tests | **1661 passing / 0 failing** across 15 assemblies, measured 2026-09-01 (1592 on 2026-08-31). `Web` 611, `NFeModule` 377, `PdvModule` 85, `BillsModule` 278, `OrderModule` 78, `EmployeeModule` 86. A whole-solution run *while another session was rebuilding* once reported a spurious `Web` failure that did not reproduce — re-run before believing a lone failure |
| FrontV2 | `npx tsc --noEmit` clean and `npx jest` **107 passing, re-run 2026-08-31**. Cypress **re-run 2026-09-01**: 30 tests, 26 passing, 0 failing, 4 pending, against an API image rebuilt from today's commits. The M24–M29f screens remain unexercised because no spec covers them (see B7) |
| PDV till | separate repo `~/CodeProjects/EvangelionERPV2.PDV`, branch `feature/33-pdv-login-and-sync` at `046e573` (58 commits). `dotnet test EvangelionERPV2.PDV.slnx` **385 passing / 0 failing** (379 unit + 6 integration), build 0 warnings, measured 2026-09-01. **That repo has no git remote at all** — it exists on this machine only. Its `.claude/` is now gitignored — two agent worktrees were nearly committed as embedded repositories |
| Migrations | chain ends `20260901192637_AddIcmsReductionAndSimplesCredit` (after `…133552_AddPayrollDetailEnterprise` and `…132721_AddAbonoDaysSettled`); both applied and verified in `sys.columns` 2026-09-01, and the whole chain replays clean onto an empty database since the `EXEC` fix in the traps list. **All applied to the local container**, verified against `__EFMigrationsHistory` and `sys.default_constraints`: `NFeDocument.TipoEmissao` defaults to `1`, `NFCeContingencySeries` to `N'9'` and `EnterpriseFiscalSettings.DefaultNatOp` to `N'VENDA DE MERCADORIA'` — **all three hand-corrected**, because EF generated `0`, `""` and `""`. Every one of those tables is **empty locally**, so each backfill is verified as a constraint and not observed on a row. Production is still untouched |
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
| M23 | Orçamento → pedido | **Done 2026-08-24**; PDF added 2026-08-28 (U3) | `875a854`, FrontV2 `5d6f19b` |
| M24 | Custo e margem real | **Done 2026-08-25** | `68986a8`, FrontV2 `8df4213` |
| M25 | Metas + comissão de vendedores | **Done 2026-08-25** | `760eeba`, FrontV2 `a82ac5d` |
| M26 | Ficha do cliente (Customer 360) | **Done 2026-08-25** | `be4a34a`, FrontV2 `005de5d` |
| M27 | Busca global (Ctrl+K) + WhatsApp | **Done 2026-08-25** — FrontV2 only | FrontV2 `198186e` |
| M28 | Mapa de clientes | **Done 2026-08-25** | `6a9b6cd`, FrontV2 `7f3c3dc` |
| M29a | PDV backend — sessions, cash movements, sale sync | **Done 2026-08-26** | `153235b` |
| M29b | Terminal credential, till login, PIN override, sync state | **Done 2026-08-26** | `a1dda32` … `0a13c93` |
| M29c | Product thumbnails for the till | **Done 2026-08-27/28** | `ff525df` … `6f3d06d` |
| M29d | Receipt printer, drawer, scale, display | **Done 2026-08-28/29** — ESC/POS printer, drawer kick, scale, customer display, all optional so a till with nothing plugged in still sells | PDV `d59cfcb` … `98d13c1` |
| M29e | Card authorization code + PIX manual; Stripe Connect PIX | **Code complete 2026-08-31, plan tasks 1–9.** No-PSP half (1–4), then `PdvCharge` + the charge endpoints + the signature-verified webhook (5–7), then the till's `IPaymentGateway` and PIX screen (8–9). **Task 10 — wiring it up and proving it against a running ERP — is not done, and is gated** on a real Stripe BR PIX account | `30aac13` … `fa67d97`; PDV `c686f9e` … `0415e4e` |
| M29f | Backoffice screens for any of M29 | **Done 2026-08-31** — four new Supervisor-tier read endpoints plus the FrontV2 *Tills* screen | `8e3164c`, FrontV2 `57fd4e6` |

## What M24–M28 added, and what they left open

Five features aimed at the person who *sells*, none of which re-litigates a settled cut — three of
them are the roadmap's own cut list turned into its recommended "honest 5%".

- **M24 custo e margem.** `Product.CostPrice` plus `OrderedProduct.UnitCost`, a **snapshot** taken at
  line creation so editing a cost never rewrites a past order's margin. `Shared/Utils/OrderMargin` is
  the one implementation; an unknown cost answers **null**, never zero, and one uncosted line makes
  the whole order unknown rather than reporting a margin that is far too good. Cost is gated behind
  the new `products.cost.read` (Manager+) and withheld in one funnel per controller;
  `OrderedProduct.UnitCost` is `[JsonIgnore]`d so it cannot leak through the order lines.
  **Open:** ~~nothing backfills cost~~ — since 2026-08-28 a supplier NF-e import costs any matched
  product that has none (U2), so a shop that imports its notas costs itself. A product that has
  never arrived on a nota is still uncosted, and quotes still carry no margin.
- **M25 metas + comissão.** `User.CommissionRate` (decimal(5,4) — the money precision would round
  0.035 to 0.04) and a `SalesGoal` table. **Commission is paid on what was *received***, resolved
  through the new `ReceivableResolver.ResolveReceivedAmount`, not on what was sold.
  **Open:** ~~a seller cannot see their own row~~ — since 2026-08-31 `sales.performance.read.self`
  starts at the Employee tier and `GetSellerPerformance/{id}` answers one row through the existing
  `SelfOrPermission` handler; the whole table stays Manager+, and the self response carries no
  enterprise row. The enterprise row is deliberately not the sum of the seller rows (a queued order
  has no seller).
- **M26 ficha do cliente.** One endpoint, `Customer/GetCustomerSummary`, composed entirely of reads
  that already existed. The financial block is nulled for a caller without `bills.read`.
  **Open:** it reuses `GetAgingAsync`, which scans the tenant's whole open carteira to read one
  customer's row (`ponytail:` — give that service a customer filter if a big tenant feels it).
- **M27 busca global + WhatsApp.** FrontV2 only, as the roadmap scoped it: `Ctrl/Cmd+K` fans out over
  the existing `*ByFilter` endpoints, and `utils/whatsapp.ts` builds `wa.me` links.
  **Open:** ~~the palette switches module but does not yet open the specific record~~ — since
  2026-08-31 it opens the record too (U5). It still searches only customers, orders and products.
- **M28 mapa de clientes.** `Customer.Latitude/Longitude`, a bounding-box + Haversine `GetNearbyCustomers`,
  and a batch `GeocodePendingCustomers`. **Geocoding never runs on the write path** — saving a
  customer must not depend on a public API being up.
  **Open:** precision is CEP-level via BrasilAPI, not street-level. **One new npm dependency,
  `leaflet`**, web-only and `require`d lazily so native never loads it; native shows the ranked list.
  Nothing has verified the OSM tile host is reachable from the production network.

## What M29 added, and what it left open

The till is a **second repository** — `~/CodeProjects/EvangelionERPV2.PDV`, Avalonia, offline-first,
its own SQLite and outbox. This repo holds only the server half. Read that repo's `README.md` for the
three-step enrolment; nothing here duplicates it.

- **M29a, the module.** `EvangelionERPV2.Pdv.Application` — `CashSession`, `CashMovement`, `PdvSale`,
  `PdvSalePayment`, and a `PdvService` that composes `OrderService` and `ProductService`. It owns no
  repository interface of its own, deliberately: an interface with one implementation and no caller
  but its own service is the thing this codebase keeps deleting. **A till sale becomes an ordinary
  `Order`**, so margin, commission, stock and the daily panel all see it with no extra work.
  `SyncSale` is idempotent on `ClientSaleId`, and `SyncSaleBatch` drains a whole outbox in one call.
- **M29b, the credential chain.** Four separate secrets, none of them reusable as another:
  a per-terminal enrollment credential (shown once, stored only as a PBKDF2 verifier, revocable);
  a till login that takes an operator's ordinary ERP password with no browser
  (`PasswordLoginService`, now shared with `UserController` so one password check serves both);
  a `PdvTokenScope` that narrows what a token minted at a till may do, consulted by both
  `PermissionAuthorizationHandler` and `SelfOrPermissionAuthorizationHandler`; and a supervisor
  override PIN. `FailedAttemptLimiter` stops the login endpoint answering the question
  "does this user exist" faster than it answers "is this password right".
- **The override PIN verifier leaves the server on purpose.** `GetSupervisorVerifiers` hands the till
  a PBKDF2 verifier per supervisor, because an override is needed *exactly* when the network is down.
  A verifier is not a PIN, the iteration count (210 000) is what makes six digits expensive offline,
  and only users holding a `pdv.*` grant the operator lacks are listed — sending every employee's
  verifier would put the whole staff's PINs on every shop floor.
  **Open, and a deliberate ceiling:** a stolen till database is an offline brute-force target against
  a six-digit PIN. The iteration count is stored *in* the verifier string, so raising it later does
  not invalidate existing rows. Longer PINs, or a lockout the till itself enforces, are the upgrade.
- **M29b also closed the audit hole.** `PdvOverrideAudit` records **every** override attempt, refused
  ones included — a refusal is the interesting row — idempotent on `ClientOverrideId` so a flaky
  connection cannot double-count one. `GetSyncState` re-sends each operator's permissions on every
  sync, so revoking a grant reaches the shop floor without a redeploy.
- **M29c, thumbnails.** `ProductService` renders a product thumbnail once and keeps it; the till
  fills its picture cache from a cursor feed that matches the catalog feed's cursor exactly, so one
  `since` value walks both without skipping a write that landed between them.

**Open across all of M29, in the order it will bite:**

1. **A till sale produces no fiscal document.** **Decided 2026-08-31: it will issue NFC-e** — see
   `docs/fiscal/PLAN-nfce.md`. Still the item that stops a real shop using this, and still blocked on
   the A1 certificate and a CSC, neither of which the shop holds.
2. ~~**No backoffice screen exists for any of it** (M29f)~~ — done 2026-08-31. The claim that the API
   already had every endpoint was **false**: listing terminals, revoking one, listing open sessions
   and reading the override log all had to be written. They exist now, and the FrontV2 *Tills* screen
   uses them.
3. ~~**No peripherals** (M29d)~~ — done 2026-08-28/29. Receipt, drawer, scale and customer display,
   each optional: a till with nothing plugged in still sells.
4. ~~**Card and PIX tenders are recorded, not proven**~~ — closed in code 2026-08-31. A card tender
   cannot be taken without its authorization code, at the till and again at the server; a PIX tender
   records whether anyone can prove the money arrived, and `IsVerified` is now **re-derived server-side
   from a charge row the webhook moved to Paid** — never from a flag the terminal sent. **Nothing has
   run against a real Stripe account yet** (plan task 10), so no PIX row anywhere is verified in
   practice.
5. **The till repo has no remote.** A disk failure loses it.

## What still needs doing

Nothing here is half-built: every milestone is either finished or waiting on something outside the
code. In rough order of what unlocks the most.

**M29's till has no fiscal document and no backoffice screen** — the two that are half a feature
rather than half-built. M23's quote PDF was the third and landed 2026-08-28 (U3); the renderer
behind it is still the one path `M-B8` shadows in production.

### A. Waiting on you — nothing moves until these are answered

| # | What | Why it blocks | Size once answered |
|---|---|---|---|
| A1 | **Buy the A1 certificate** (`TODO-manual.md` §M1) and finish credenciamento (§M4) | M6 is unverified and M7 + M20b are shut until one real note round-trips. Every `SefazCStatMap` entry is a guess until an autorizador produces it | the round trip itself is one session — `GO-LIVE-CHECKLIST.md` is the procedure |
| A2 | **The four `infRespTec` values**: your CNPJ, a contact name, e-mail, phone | Without them every real transmission is rejected under NT 2018.005. **No longer a code change** — fill `NFeRespTecSettings` in appsettings | minutes |
| A3 | ~~Does the business send quotes today?~~ | **Built anyway 2026-08-24.** If the answer turns out to be no, delete the module — nothing depends on it | done |
| A4 | **Juros e multa policy** (`M-B12`), with your accountant | The dunning e-mail says amount, due date and **no late fee**. Inventing a rate is a claim the ERP cannot back | S — a rate and a rule |
| A5 | **A bank** (`M-B1`: platform account or per-tenant?) | M9's CNAB remessa/retorno cannot start without the layout and the beneficiary decision | L |
| A6 | **Does the pilot sell services?** | NFS-e is per-municipality webservices, hundreds of incompatible ones. Changes the cut list | L if yes |
| A7 | **Confirm the pilot only issues plain sales** (§M14) | **Narrowed in code 2026-09-01, not answered.** `tPag` is real on the till's NFC-e (built from the sale's tenders) and `natOp` is now per tenant (`EnterpriseFiscalSettings.DefaultNatOp`, migration `…122839`) instead of a constant in the builder. What still needs you: whether anything but a plain sale is ever issued. A devolução or remessa needs a natOp that follows the CFOP, and a modelo 55 still declares "Outros" because a back-office `Order` records no means of payment | S — one field on `Order`, only if the answer is no |
| A8 | ~~What fiscal document does the till issue?~~ **Answered 2026-08-31: NFC-e, with offline contingency.** Tasks 1–4 and the contingency spine shipped the same day (`8e7b594`…`fe225d9`); §6 of the plan lists them. **The contingency answer collided with a standing constraint** — the till holds no certificate, and offline signing needs one. What shipped covers "SEFAZ unreachable, till↔ERP fine"; covering a fully isolated till means an A1 certificate on a shop-floor machine, which is a decision, not a task (plan §7). Original entry: SAT-CF-e rejected (SP-only, certified hardware). Plan: **`docs/fiscal/PLAN-nfce.md`**, which supersedes `SPEC-remaining-code.md` §2.3 — that sketch predates M29 and assumed NFC-e was a back-office concern. **Scope is not yet confirmed**: five blockers in §0, of which **Q5 (offline contingency) is new and changes the shape of the work**. Smaller than "L" looked: `NFeDocumentType.NFCe`, model 65 in the numbering table, and the `TpImp`/`IndPres` branches already exist | M once §0 is answered; L if contingency is needed |
| A9 | **Does the shop hold a Stripe BR PIX account?** (invite-only) | The proven-PIX code all landed 2026-08-31 (plan tasks 5–9) and **has never been run against Stripe**. Task 10 is the round trip, and it cannot happen without the account. Until it does, treat every `SefazCStatMap`-style assumption in `StripePixProvider` as a guess | S once the account exists |

### B. Operational, before any of this is real

| # | What |
|---|---|
| B1 | ~~Apply the two newest migrations.~~ **Done 2026-08-31.** `./scripts/local-db-update.sh` backed the database up and applied `AddPdvSalePaymentVerification` and `AddPdvCharge`; verified against `sys.columns` — `PdvSalePayment.PdvChargeId` and `.IsVerified` exist and the `PdvCharge` table exists. `/health` answers Healthy. Production is still untouched — nothing calls `Database.Migrate()` |
| B2 | **Push the branch.** `feature/33-pdv-terminal-login` has no upstream and is **285 commits ahead of `origin/main`** — M14–M29 and the whole bug pass live on one unpushed branch on one machine. Now the *only* copy: the six worktree duplicates were removed 2026-08-31 (R5) |
| B6 | ~~Apply the M24–M28 migrations.~~ **Done.** Verified 2026-08-28: `__EFMigrationsHistory` ends at `20260826174529_AddPdvOverrideAudit`, so the four M29 migrations are applied locally too |
| B7 | ~~**Re-run Cypress.**~~ **Ran 2026-09-01 against an image rebuilt from today's commits: 30 tests, 26 passing, 0 failing, 4 pending** (the recaptcha-gated credential specs and two others that skip themselves), in 1m57s. The Products flake did not reproduce — both product specs passed. **This entry was wrong about what the suite covers**: it says "seven screens now … none has been through a browser", but `cypress/e2e/frontend.cy.js` is the only spec file and it exercises the module shell, the dashboard, customers, products, orders, bills, payables, the forecast, employees, payroll and settings. **None of the M24–M29f screens has a spec at all** — margin, seller performance, the ficha, the command palette, the map and the *Tills* backoffice are unexercised because nothing was ever written for them, not because the suite was not run. Writing those specs is the open work; the run itself is done, and `./scripts/e2e-cleanup.sh --apply` cleared its 11 seeded rows afterwards |
| B10 | **Give `EvangelionERPV2.PDV` a remote and push it.** `git remote -v` is empty in that repo. Every line of the till exists on one disk |
| B11 | ~~Commit or drop the two dirty FrontV2 files.~~ **Done 2026-08-31.** `useLoginController.ts` was a real fix — Google web login now goes through PKCE, which the server already required a `code_verifier` for (FrontV2 `80dec33`) — and `Dashboard.tsx` was the revenue-by-product chart rebuilt as stacked bars (`2c25979`). Both committed separately |
| B3 | **Confirm Chromium in the *production* image** (`M-B8`). Partly answered 2026-08-24: in the **local** container `/Bills/Pdf` returns 200 with a real PDF, so PuppeteerSharp works there. Production is a different image and a different network, and `BuildBillAttachmentsAsync` still swallows a renderer failure and mails the bill with no PDF |
| B4 | ~~Run Cypress once.~~ **Done 2026-08-24 — and it earned its keep.** 16/30 on the first run, **27/30** now. It found four app bugs (see below). One test remains flaky: the two Products specs share a single seeded product, so whichever runs second finds it already deactivated and `GetProductsByFilter` answers 204. A fixture problem in the spec, not the app. **Run `./scripts/e2e-cleanup.sh --apply` after a session** — the suite can only soft-delete through the API, so every run leaves its seeds in the tables (204 rows after four runs, cleared 2026-08-24) |
| B5 | **Decide on FrontV2 `abb1b46`** — it carries a `Co-Authored-By` trailer and is already pushed to `origin/master`. Removing it means force-pushing a shared branch |

### C. Known debt — none of it silent, none of it urgent

Each is recorded with its reasoning in *Open follow-ons* below. The ones most likely to bite first:

- ~~**M11 P5 abono**~~ — closed 2026-09-01. `PayableBill.AbonoDaysSettled` (migration `…132721`,
  nullable so bills older than the column keep the old inference) records what each férias bill
  settled, so days sold after the first one ride the next stretch instead of vanishing. **Residual,
  marked `ponytail:`**: days sold after *every* scheduled stretch is billed still have no bill to
  ride — that needs a férias bill with no stretch, which has no due-date rule of its own.
- ~~**M20 orphan supplier**~~ — closed 2026-09-01; the emitter and its first bill are one unit of work.
- ~~**M21 credit-limit race**~~ — closed 2026-09-01 (`898ac1c`). **The "only fix is serialising every
  order" line was wrong**: the contention is one row, not the table. `AssertWithinCreditLimitAsync`
  now takes `UPDLOCK, HOLDLOCK` on that customer's row — through a new
  `IRepository.LockForUpdateAsync`, table name from EF's own metadata and the id as a parameter —
  after the limit lookup and before the carteira read, inside the transaction `CreateAsync` already
  opened. The second order for that customer queues and reads the carteira with the first one in it;
  orders for anyone else take a different row's lock and never wait. A customer with no limit set
  locks nothing, and an `allowCreditOverride` order still bypasses it, which is what an override is.
  No-op off SQL Server, so the SQLite test harnesses are unaffected. **A true two-thread race is not
  reproducible against mocked repositories**, so the test asserts the mechanism instead — the
  recorded step order is `transaction, lock, carteira`, with that customer's id and no other — and
  says so in its own comment. Still unguarded, as it always was: `UpdateAsync` has no credit check
  at all, so raising an existing order's total past the limit is unchecked.
- ~~**Users `DeleteUser`** is a hard delete~~ — closed 2026-09-01. It deactivates now, like every
  other delete here. It was also the one that could not work: the till's `CashSession.UserId` is
  `Restrict`, so removing an operator with a shift on record threw at the database. Login, Google
  sign-in and the till all require `IsActive == true`, and the e-mail stops counting against the
  active-email uniqueness check, so it can be reused.
- ~~**Three duplicate `IDistributedCache` registrations**~~ — already closed by R4 on 2026-08-28;
  this line had gone stale. `SharedIoC:34` is the only `AddStackExchangeRedisCache` in the tree.

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

- **A backfill in the same migration as its column never ran anywhere but here.** Found and fixed
  2026-09-01. `dotnet ef migrations script --idempotent` emits one batch per migration — the
  `ALTER TABLE ... ADD` and everything after it, wrapped in `IF NOT EXISTS(...)` blocks, with the
  `GO` only between migrations. SQL Server compiles a batch whole, so an `UPDATE` naming the column
  the `ALTER` above it is about to add fails with `Invalid column name` on **every database that has
  not run that migration yet** — which is the only kind of database the backfill exists for.
  It never showed locally because those migrations were applied through `dotnet ef database update`
  (one command per operation) before `scripts/local-db-update.sh` existed. **Proven, not reasoned:**
  the pre-fix script run against a fresh database died at `Invalid column name 'CurrentBalance'`,
  then `'EnterpriseId'`; after the fix the same script applied all 59 migrations and 45 tables
  clean. Three committed migrations carried it — `AddEnterpriseCurrentBalance`,
  `AddAuditTrailEnterpriseSnapshot` and `AddNFeDocumentFiscalColumns` — plus the new
  `AddPayrollDetailEnterprise`, which is how it was found. **The fix is `EXEC(N'...')`**: dynamic SQL
  is compiled at execution, so the column exists by then. Doubling the single quotes inside is part
  of it. `CREATE INDEX` on a fresh column is fine — DDL resolves at execution; only DML is affected.
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
- **`dotnet test EvangelionERPV2.sln` says "Passed!" for a solution that does not build.** It runs
  the assemblies that compiled and never names the one that did not. `2f8064c` gave `UserController`
  a required `PasswordLoginService` parameter and missed one call site in
  `EvangelionERPV2.UserModule.Test`; that project stopped compiling and the next two days of test
  runs all reported success over 13 assemblies instead of 14. **Read the build's `N Error(s)` line,
  not just the test summary** — `dotnet build` first, and treat any error as a red suite. CI would
  have caught it (`runTests.yml` builds as its own step) but CI only runs on a PR to `develop` or
  `release`, and this branch has never been pushed.
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
  ~~Still open there: an unknown emitter's supplier row is created before the bills, so a refused
  import can leave an orphan supplier.~~ **Closed 2026-09-01.** The emitter is now created **in the
  same transaction as the first bill it was created for**, and item matching plus the cost backfill
  — everything that can fail while writing nothing — moved ahead of it. A refused import (a closed
  period is the ordinary case) now leaves nothing behind. The test double had to be fixed to earn
  the test: a loose Moq `ExecuteInTransactionAsync` answers a completed Task and **never calls the
  delegate**, so the transaction body silently did not run at all.
- **M21**: two simultaneous orders that each fit under the limit but together do not will both pass.
  The limit is a policy, not a ledger invariant; serialising it would lock every order.
- **M22**: a long-dormant chain catches up one month per sweep, by design. Both sweep queries are
  bounded by `RecurrenceUntilUtc >= today`, so an installation's finished chains do not make the
  morning pass read a growing table to do nothing. Recurrence must travel on
  **every** payable update — a missing value means "stop repeating", which is how a chain is ended.
- **M11 P5**: ~~abono settles with the first bill an accrual produces; days sold afterwards go
  unbilled~~ — closed 2026-09-01 by `PayableBill.AbonoDaysSettled`; what is still owed is now
  `SoldDays` minus what the standing bills recorded, and a bill older than the column is still
  inferred from its amount. ~~Days sold once every stretch is billed remain unbillable~~ — closed
  2026-09-01 (`05b8d62`) by refusing the sale rather than inventing the bill: `SellDaysAsync` will
  not record an abono once every scheduled stretch is billed *and* the sale would leave no days to
  schedule another. Both clauses are load-bearing — the first alone breaks the ordinary "bill one
  stretch, sell days, schedule the rest" flow. The rejected alternative was a stretchless férias
  bill, which has no art. 145 due date of its own (that rule measures from a leave start there is
  none of) and would be a permanent orphan in the read model, since `DueDate == start - 2` is the
  only stretch↔bill link `GenerateAsync` and `GetAsync` have.
  **A review pass caught one real defect in that change the same day** (`0349f84`): a bill issued
  before the column existed whose stretch was later cancelled has no recorded value *and* no stretch
  to price the inference against, and reading it as "settled nothing" put the whole abono back on
  the next bill — paying days already standing, unrefunded, on the first one. Such a bill is now
  read the old way: the abono rode the first bill this accrual produced. Under-billing an orphan
  that never carried it leaves the days visible on the accrual; over-billing one that did sends the
  money twice.
  A férias bill whose stretch is cancelled after issue is reported as an orphan — **re-read
  2026-09-01 and left as it stands**: `Orphan(...)` is terse, not wrong. It reports the bill rather
  than hiding a committed liability, recovers `StartDate` by inverting the due-date rule, and zeroes
  `Days` and the split because pricing the stretch needs the stretch. Hiding it would lose the
  liability; synthesising an `EndDate` would fabricate a leave that was cancelled. Nothing sums the
  DTO, so the zeroes mislead no computation.
  ~~`PayableBillDetail` has no `EnterpriseId`.~~ **Closed 2026-09-01** — the table is
  `PayrollBillDetail`, and it now carries the column (migration `…133552`, backfilled from the
  payable bill and then from the employee; verified locally: 6 rows, none left at `Guid.Empty`). It
  is therefore covered by the convention filter in `ApplyTenantQueryFilters` like every other
  tenant-owned entity, and `PayrollService.LoadExistingAsync` scopes its query instead of reading
  every tenant's competência and discarding the rows whose bill was not this enterprise's. Nothing
  leaked before — the post-filter was correct — but the whole installation's salary-derived rows
  were read into memory to produce one tenant's answer. **Migration trap paid for here:** an
  `UPDATE` naming the new column has to go through `EXEC`, because the idempotent script puts it in
  one batch with the `ALTER` and SQL Server parses a batch whole — the direct form failed with
  `Invalid column name 'EnterpriseId'`.

  **The rest of the entities were swept the same day.** Thirteen carry no `EnterpriseId`;
  `PayrollBillDetail` was the only one queried without either a join to its tenant-owned parent or a
  set of ids that came from a tenant-scoped query. `Bill` reaches the tenant through
  `x.Order.EnterpriseId` in every query, the radar's signals and recommendations through
  `OpportunityId`, the item tables through their parent, and `RefreshToken` / `PasswordResetToken` /
  the e-mail settings are deliberately not tenant-owned. None of those are covered by the convention
  filter, so each stays correct only as long as the next author remembers the join — a known shape,
  not a defect today.
- **M11 P5a**: `PayableBill.Amount` on a férias bill is the *netted* figure; whoever pays reads
  `GrossAmount`. M12's matcher and M18's export both already do.
- **M11 P4**: the approaching-deadline liability read has no controller action and no permission —
  `SPEC-hr-module.md` §8 puts that KPI out of scope.
- ~~**Users**: `DeleteUser` is a **hard** delete, the only one in the system.~~ Closed 2026-09-01 —
  it deactivates, and the row it used to remove is referenced by orders, cash sessions and audit
  trails.
- **M6**: `NFeXmlBuilder` covers **ICMS CST 00, 20, 40, 41, 50 and 60 and CSOSN 101, 102, 103, 300,
  400 and 500** since 2026-09-01 (`1870656`) — 00 and 20 carry arithmetic, 101 carries the credit
  the buyer may take, and the rest declare an exemption or ICMS already collected under ST upstream.
  Two nullable fiscal fields came with it (migration `…192637`): `Product.ReducaoBaseIcms` (pRedBC,
  a fraction, per product because the reduction follows the goods) and
  `EnterpriseFiscalSettings.AliquotaCreditoSn` (pCredSN, company-level because it is this company's
  effective Simples rate). **Both refuse rather than default**: a missing pRedBC read as zero
  declares the full base and overstates the tax, and a missing pCredSN read as zero silently denies
  the buyer a credit they are entitled to. Exposed through the product create/update DTOs and the
  fiscal-settings payload with exclusive 0..1 validation; **no FrontV2 field, because the whole
  per-product fiscal block is API-only today** — the frontend never had `ncm` either. Still refused:
  CST 51, 70 and 90 and CSOSN 201/202/203/900, which need a deferment percentage or an ST base and
  margin that live nowhere here. `ICMS40` carries no `vICMSDeson` (the motive code is not recorded)
  and `ICMS60`/`ICMSSN500` carry no retained-ST figures (`ponytail:`, optional in the schema; a UF
  that demands them on resale needs those columns on `Product` first). **`natOp` is no longer a
  constant**: since 2026-09-01 it is `EnterpriseFiscalSettings.DefaultNatOp` (default
  `VENDA DE MERCADORIA`, blank falls back to it, over 60 characters is cut rather than refused
  because a truncated description misstates nothing that is computed from it). It is still one
  nature per tenant — a per-CFOP one needs devolução and remessa, which need a field on `Order`.
  **`tPag` is no longer one either, for the till**: since 2026-09-01 an
  NFC-e is built from the sale's own `PdvSalePayment` rows — one `detPag` per tender, `vTroco` for
  the change, and the `card` group with the authorisation code on a card. A modelo 55 still declares
  "Outros", which is the honest answer for a back-office order that records no means of payment.
- **M29e PIX, reviewed 2026-09-01** — the slice landed on 2026-08-31 in a parallel session and had
  never been read by another pass. Two defects, both fixed the same day:
  - **A cancelled charge blocked its basket forever.** `StartPixChargeAsync` returned *any* existing
    charge for the `ClientSaleId`, so after the operator pressed Esc — which the till does, then
    clears its own handle and lets them press PIX again — the next attempt got the **cancelled** row
    back: a dead QR and a poll answering "cancelado" for a customer still standing at the counter.
    Now only **Pending** (the live intent) and **Paid** (never charge a paid basket twice) are
    handed back; Cancelled, Failed and Expired get a fresh attempt. A Pending charge for a
    *different* amount is refused rather than returned — that QR would take a figure nobody agreed to.
  - **`IsVerified` was weaker than it reads.** It required a Paid charge of the tenant named by the
    terminal, but **not that the charge was raised for this basket, nor that it settled this much**.
    The terminal chooses the id, so anything the id alone reaches is something the terminal can
    choose: a R$ 500 PIX tender could point at yesterday's paid R$ 1 charge and be recorded as
    proven. Now matched on `ClientSaleId` and `Amount` too. **A mismatch records the tender
    unverified and never refuses the sale** — `RejectAsync` is terminal and the customer has paid.
  - ~~**Nothing ever writes `Expired`.**~~ **Closed 2026-09-01**, because it was the same bug as the
    cancel one through a different door: a charge whose window shut stayed Pending, and the fix
    above reuses a Pending charge — so the next attempt on that basket would hand back a QR nobody
    can still scan. Expiry is now written **lazily on the two paths that care** (the till's poll and
    the next attempt on that basket), never by a sweeper: a charge nobody is looking at costs
    nothing by staying Pending. **Expired is our inference, not the provider's verdict**, so a Paid
    event still settles a row we expired — the clocks are not the same and a customer whose PIX
    landed in the last second is paid. Cancelled and Failed stay untouchable: one was agreed with
    the provider before the row moved, the other is the provider's own answer.
  - **Left open, deliberately.** `PdvCharge.EndToEndId` is read off `intent.LatestCharge`, which a
    webhook payload does not expand, so **it is null in practice** — the PIX e2e id costs an API
    call per settlement and belongs in the same batched fetch as `FeeAmount` (already `ponytail:`),
    worth doing when the account exists (A9).
- **M29b credential chain, reviewed 2026-09-01.** The till's auth surface — enrollment credential,
  anonymous login, PIN verifiers, override audit, and the M29f backoffice reads — read end to end.
  Sound: PBKDF2 at 210k iterations with `FixedTimeEquals`; login answers one 401 whatever failed and
  counts the attempt against both a per-terminal and a per-terminal-and-operator window; the
  credential picks the tenant, so an operator from another company cannot log in through an enrolled
  till; the terminal list is a projection, never the row with the verifier on it.
  **One real hole, fixed.** `SetPdvPin` was **the only authenticated action in the API carrying no
  rbac policy** — and `PdvTokenScope.Grants` runs *inside* the policy handlers, so a pdv-scoped token
  reached it unchecked. A credential lifted off a shop-floor machine (the exact threat `PdvTokenScope`
  is written against) could set the override PIN of the user it belongs to; where that user is
  supervisor tier, the thief then authorises their own overrides with a PIN they chose, **recorded
  against the supervisor** — the attributability the design exists to keep. It now refuses a scoped
  caller by hand, and `RbacControllerPolicyCoverageTests.EveryAuthenticatedAction_CarriesAPolicy`
  fails the day a second policy-less action appears (verified by removing the exemption and watching
  it go red).
  **Residual, by design, not a finding:** every supervisor's PIN verifier is cached on every till, so
  a stolen terminal is an offline six-digit guess at 210k iterations. That is the stated trade for an
  override that still works with the network down.
- **M29a sync path, reviewed 2026-09-01.** Session open/close, cash movements and
  `SyncSale`/`SyncSaleBatch` read end to end against the till's own outbox in the PDV repo. Sound:
  the claim on `ClientSaleId` and its unique index; the tender total asserted against the basket
  before anything is written; a card tender refused without its authorization code at the server as
  well as the till; only the cash leg reaching the drawer; `RequireSessionAsync` rather than
  `RequireOpenSessionAsync` on a sale, so an offline shift still syncs after its close; the till's
  queue draining in order and stopping at the first retry.
  **Three holes, all fixed (`430e2f2`, PDV `046e573`).**
  1. **A crash between the order and the payments sold the basket twice.** `OrderService.CreateAsync`
     commits in its own transaction; the sale's `OrderId` was written only by the *later* completion
     commit, so a failure in between left a bare Pending claim and the retry rang the whole basket up
     again — second order, second stock movement, second cash movement, and a drawer expecting money
     nobody put in it. The order id is now committed on its own the moment it exists, and a claim
     that already carries one resumes instead of re-creating. Covered by
     `AFailureAfterTheOrderWasCreated_ResumesInsteadOfSellingTheBasketTwice`, verified red without
     the guard.
  2. **The till read 204 as an acknowledgement.** `NotFoundDatabaseException` maps to 204 across this
     API, and `InterpretAsync` treated any success status as accepted — so a sale for a session the
     server had never heard of was dropped from the outbox and marked Sent, with the money recorded
     on the terminal only. 204 now retries: the queue stops where a human can see it. The old
     `A204IsStillAccepted` test asserted the bug and is now its opposite.
  3. **A resent close came back as a refusal, and a second shift as a 500.** Closing is idempotent
     now — the stored reading stands, the count is not overwritten and no second Closing movement is
     written — and opening a shift on a terminal that already has one is named as a 400 the operator
     can act on, instead of the filtered unique index throwing a `DbUpdateException` the outbox
     retries forever without saying why.
  **Reviewed again the same day, and one of its own claims was false.** The commit said the refused
  second shift was "a 400 the till reports as a refusal, saying which shift is in the way". Neither
  half held: `ExceptionHandlingMiddleware.GetSafeBadRequestDetail` replaces every 400 body with a
  fixed string, so the message never leaves the server; and a 400 is *final* at the till —
  `SyncEngine` marks it Rejected and discards the outbox entry, and `MarkSaleRejected` returns early
  for a non-Sale kind, so nothing is recorded at all. The shift would then never open, even after a
  human closed the stale one, and every sale behind it would jam. Corrected in `70361bd`: the
  refusal is an `InsertDatabaseException` (a 500 the outbox keeps and retries, so it heals itself
  once the stale shift closes) plus a `Warning` naming the terminal, the blocking session and the
  one being refused — the log is the only channel that carries a reason, since the middleware
  sanitises 4xx bodies and logs 5xx by type alone.
  **A second residual the review named, and it is inherent:** the double-sale window is narrowed,
  not closed. If `OrderService.CreateAsync` commits and the `OrderId` commit that follows it fails,
  the retry still finds a bare `Pending` claim and rings the basket up again. Nothing on `Order`
  carries the `ClientSaleId`, so there is nothing for the retry to recognise; closing it needs an
  `Order.Id` derived from the client id, which is a redesign rather than a fix.
  **Not a defect, worth knowing:** `NFeService.GetTillTendersAsync` matches a `PdvSale` by `OrderId`
  with no status filter, and `Pending`-with-`OrderId` is a state that did not exist before. It
  degrades correctly — no tender rows, a warning, and tPag 99 rather than an invented payment — and
  the till's own issue path is gated on `Status == Synced`.
  **Left open, deliberately:** a stale open shift still blocks its terminal until somebody closes it,
  and the backoffice can only *list* open sessions. Forcing a close is a Z reading nobody counted, so
  it is a decision rather than a fix.
- **NFC-e**: `EnterpriseFiscalSettings.Enabled` and `.NFCeEnabled` were stored, editable and
  **enforced nowhere**. `6264ab5` gated the till's `IssueFiscalDocument` on both; since 2026-08-31
  `NFeService.IssueAsync` reads them too, before it claims a document, so a certificate removal
  (`NFeCertificateService` sets `Enabled = false`) now actually stops issuance. **The earlier note
  here was wrong about the cost**: a tenant with no profile could never issue anyway — the CNPJ
  guard in `AssignNumberAndAccessKeyAsync` already refused it — so the only changed behaviour is a
  configured tenant whose switch is off. A refusal answers null, i.e. 204 and no document row,
  exactly as the global kill switch already did. **Release note**: a tenant that saved a fiscal
  profile without ticking the switch stops issuing.
- **NFC-e, three defects found reviewing the slice on 2026-08-31** and fixed the same day; the table
  is in `docs/fiscal/PLAN-nfce.md` §9. In short: the issue path carried a hand-copied `ApplyOutcome`
  that dropped the consumer QR (**an NFC-e authorised at a counter had nothing to scan**, and the
  reconciler never revisits an authorised document to repair it) and ignored `NeverTransmitted`
  (**the till's contingency fallback was dead code**, and every retry burned a fiscal number), while
  `CanReuseAllocation` would have handed a contingency retry its online allocation — tpEmis 9 in the
  XML, tpEmis 1 in the chave on the customer's receipt. **Lesson: an adoption path with two
  implementations is the one that drifts**; the whole point of `ApplyOutcome` being `internal` was
  that two copies had drifted once already, and a third copy was sitting inside `IssueAsync`.

## What we could build next — candidates, not commitments

Nothing here is scheduled and nothing here contradicts a cut list; where a cut *would* be reopened it
says so and why the facts changed. Ordered by value per hour, not by size.

### Robustness — small, and each one closes a failure that has already happened here

| | What | Why now | Size |
|---|---|---|---|
| R1 | ~~A pending-migration check at startup.~~ **Done 2026-08-28.** `LogPendingMigrationsAsync` in `Program.cs` names every pending id at `Fatal` at boot; `DatabaseHealthCheck` now answers **Degraded** (not Unhealthy — an environment that is behind must still serve while somebody applies them, and `MapHealthChecks` returns 200 for Degraded) and lists the ids in the body. Neither path can fail startup | Nothing calls `Database.Migrate()`, by decision, and nothing *told you* either. It bit twice — nine migrations behind on 2026-08-20, three on 2026-08-25 — and both times the symptom was every request to the new tables answering 500 with `Invalid column name` | Done |
| R2 | ~~Health-check Redis and RabbitMQ.~~ **Done 2026-08-28.** `RedisHealthCheck` reads a probe key (a read, so it leaves nothing in a shared cache) and `RabbitMqHealthCheck` reports the broker connection through a new `IRabbitMQManagerBase.IsConnected`. Both bounded at 5s like the database probe, and neither echoes the exception message — `/health` is anonymous and those messages carry the host and the password | Both are load-bearing — the read caches, and the `RedisLease` that keeps `DailySweepWorker` to one runner. The Redis probe also forces the secret resolution that used to happen lazily inside the cache options callback | Done |
| R3 | ~~Make the swallowed PDF failure loud.~~ **Done 2026-08-28.** `BuildBillAttachmentsAsync` still sends the mail — the digitable line is payable on its own — but now at `Error` with an `evangelion_bill_email_missing_pdf_total{reason}` counter. **The `pdf.Length == 0` branch was entirely silent** and is counted too: a renderer that returns nothing without throwing is the same outage wearing a different hat | It was the only silent data-loss path left in the system, and M23's quote PDF would have inherited it | Done |
| R4 | ~~Delete the two dead `IDistributedCache` registrations.~~ **Done 2026-08-28.** All three module copies (Order, Product, Reports) removed; `SharedIoC` — which already ran last, so resolution is unchanged — now owns the single registration and reads its secrets through `Configure<AWSKMSKeyProvider>` instead of a captured instance. `SharedIoCCacheRegistrationTests` fails if a module grows its own copy again | Two of the three were dead weight that read like configuration | Done |
| R5 | ~~Drop the duplicated `.claude/worktrees/` copies.~~ **Done 2026-08-31.** Six of them, not three, **6.8 GB** total, untouched since 2026-08-19. Four carried commits unreachable from the branch — M1, M2, M3, M5 and M8 — but every one is a **superseded duplicate**: the same milestones landed on `feature/33-pdv-terminal-login` under different shas, and the Supplier projects, `RedisLease`, `StockMovement` and the bills services were all confirmed present before anything was removed. Removed with `git worktree remove`, **keeping all six `worktree-agent-*` branch refs**, so no commit left the object store and `git worktree add` restores any of them. `.claude/` is now 28 KB | Done |

### Useful — each finishes something already paid for

| | What | Why now | Size |
|---|---|---|---|
| U1 | ~~M29f: the backoffice half of the till.~~ **Done 2026-08-31.** **This entry was wrong about the premise** — it said "every endpoint exists", and four of them did not: only `EnrollTerminal`, `GetSessionSummary` and `GetOpenSession/{terminalId}` were there, so it was never pure FrontV2. Added `GetTerminals`, `RevokeTerminal`, `GetOpenSessions` and `GetOverrideAudit`, all Supervisor tier. **The terminal list is a projection, never the entity**: `PdvTerminal` carries a PBKDF2 verifier, and a test fails the day anyone returns the row instead. Revoke is idempotent and answers 204 for another tenant's id, so it is not an enumeration oracle. FrontV2 gained a *Tills* screen: enrol (credential shown once), revoke, the sessions open now, their X/Z reading, and the override log with a refused-only filter | Done |
| U2 | ~~Backfill `Product.CostPrice` from the supplier NF-e.~~ **Done 2026-08-28.** `TryBackfillCost` writes **only where `CostPrice` is still null** — a backfill, not a repricing, because the line might be a return, a sample or another pack size, and overwriting would silently replace a figure a human chose. A line at vUnCom 0 (bonificação, amostra) is skipped: a product costed at zero does not read as unknown, it reads as 100% margin. Past orders are untouched either way — `OrderedProduct.UnitCost` is a snapshot. Reported as `CostedProductCount`. **Ceiling, marked `ponytail:`** — it takes vUnCom at face value, and the nota's commercial unit is not necessarily the selling unit (a box of twelve costed as one) | M24 shipped margin and commission and every product was uncosted until somebody typed a number, so every margin column read "—" | Done |
| U3 | ~~The quote PDF~~ **Done 2026-08-28.** `QuoteDocumentBuilder` builds the document — styles inlined, because `HtmlPdfRenderer` blocks every outbound request the page makes, so a CDN stylesheet would silently not arrive — and `QuoteController.GetQuotePdf` serves it under the same `quotes.read` policy as the quote itself. **A renderer failure is deliberately not swallowed here**, unlike the bill mailer: there the digitable line in the body is payable without the attachment, here the PDF is the whole response, and a 200 with nothing is a quote the salesperson believes was sent. An expired quote still renders but says so on its face | M23 shipped without it, and a quote you cannot send is half a feature | Done |
| U4 | ~~Let a seller see their own row.~~ **Done 2026-08-31.** A new `sales.performance.read.self` permission starts at the Employee tier and gates `GetSellerPerformance/{id}` through the existing `SelfOrPermission` handler, so a seller reaches only the row whose id is their own while `sales.performance.read` still reaches any. **The response deliberately omits the enterprise row** — that is the company's whole month, and the point was the person being measured seeing the measurement, not the company total arriving at the base role tier. The service computes the whole month and takes one row, marked `ponytail:`, precisely so the seller's screen and their manager's cannot disagree to the cent. FrontV2 turns the old "restricted to managers" banner into a one-row *My sales* view; setting a target stays `sales.goals.manage` | Done |
| U5 | ~~Make Ctrl+K open the record.~~ **Done 2026-08-31.** Selecting a hit switches module *and* hands that screen a `FocusedRecord`, which opens the ficha (customers), the order details or the product details. **Fetched by id, not looked up in the loaded list** — `Customer/GetCustomer`, `Order/GetOrder` and a new `fetchProductById` over the existing `Product/GetProduct` — because the lists are paginated and filtered, so the record the palette found is usually not on the page in memory. The focus prop is a fresh object per selection, so picking the same record twice reopens it. FrontV2 only, no new endpoint | Done |
| U6 | ~~Card authorization code + PIX manual at the till.~~ **Done 2026-08-31** — plan tasks 1–4 of `EvangelionERPV2.PDV/docs/superpowers/plans/2026-08-28-pdv-payments.md`. A card tender is refused without its authorization code in `TenderSheet.Take` — **the one method every caller routes through** — and `SyncSaleAsync` *rejects* rather than throws for the same payload, because a resend will never make it acceptable. PIX carries `PdvChargeId` + `IsVerified`: **`IsVerified` is derived from naming a charge, never assigned**, at the till (a computed property) and at the server (`payment.IsVerified && payment.ChargeId != null`), so a payload cannot claim proof it does not have. The local SQLite went to schema 4 with an idempotent `ALTER TABLE` repair, because `CREATE TABLE IF NOT EXISTS` never adds a column to a till that already has the table. **Deploy order is load-bearing**: `RejectAsync` is terminal, so a till predating task 1 would have every card sale permanently rejected — tills update first | Done |

### Re-decisions, because the facts changed — not re-litigation

| | What | What changed |
|---|---|---|
| D1 | ~~**NFC-e (modelo 65)** — cut from M0–M13~~ **Reopened and accepted 2026-08-31.** The cut was correct when there was no counter; M29 changed the fact. Now tracked in `docs/fiscal/PLAN-nfce.md`, not here |
| D2 | **PIX** — cut from M0–M13 | Same reason: cut as a *receivable* channel for boletos, before there was a till taking payments across a counter. The M29e plan splits it into a manual QR the operator confirms (no PSP, ships now) and a proven one (gated on an invite-only Stripe BR account) |

### Still deliberately not built

Warehouses, double-entry accounting, a CRM pipeline, `PurchaseOrder`, Quartz/Hangfire, multi-currency,
a v2 API, employee self-service, a global-search backend, price lists, a `Notification` table, XLSX.
Nothing in M29 changed the facts behind any of these.

**NFC-e has left this list** — accepted 2026-08-31 (§A8/§D1). SAT-CF-e and NFS-e have not: SAT is
SP-only and needs certified hardware, and NFS-e is hundreds of incompatible municipal webservices
(§A6, still unanswered).

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
