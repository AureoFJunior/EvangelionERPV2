# Roadmap status — where the work stopped

Written 2026-08-19, updated 2026-08-20, on `feature/33-Boleto-and-NFE-emission`. **Read this first when resuming.**

The plan itself is the Artifact **"Completing Evangelion ERP"** (M0–M13):
<https://claude.ai/code/artifact/2edfd7e3-3715-4a0a-965c-299164b28e4a>. It is not in the repo — fetch it
before planning, because it also lists what deliberately must **not** be built (warehouses,
double-entry accounting, CRM pipeline, `PurchaseOrder`, NFCe, PIX, Quartz/Hangfire, multi-currency,
a v2 API). Proposing any of those is re-litigating a settled decision.

## State of the tree

| | |
|---|---|
| Branch | `feature/33-Boleto-and-NFE-emission`, **not pushed** (no upstream) |
| Head at stop | M6 SEFAZ transport, on top of `03cf60b` |
| Build | 0 errors (20 pre-existing `NU1510` warnings) |
| Tests | **1006 passing / 0 failing** across 12 assemblies (was 916 before M6) |
| Migrations | chain ends `…150000_AddEmployeeAbsence` → `…160000_AddBankReconciliation` → `20260820100000_AddCustomerFiscalAddress` |
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
| M11 | HR — férias ledger and férias pay | Not started; **unblocked**, and the natural next milestone now that M6 is code-blocked on a certificate | |
| M12 | Bank reconciliation | **Done** | `faccd6c` |
| M13 | Admin surfaces + test debt | Not started | |

## Resume here: M11 (férias), or M7 only once a certificate exists

**M6's transport is written.** `NFeXmlBuilder`, `UnimakeConfiguracaoFactory`, `SefazCStatMap` and
`SefazNFeProvider` all exist in `EvangelionERPV2.NFe.Infra`, `NFeIoC` registers the real provider
scoped, and `StubNFeProvider` is orphaned pending its phase-9 deletion. Details and the four
decisions worth not re-deriving are in [`fiscal/SPEC-remaining-code.md`](./fiscal/SPEC-remaining-code.md) §2.1.

**It has never spoken to SEFAZ.** Every cStat mapping is unverified, no note has round-tripped, and
`infRespTec` is deliberately absent (§M13 in `fiscal/TODO-manual.md`) which is a *known* rejection on
the first real transmission. `NFeSettings.Enabled` is still false everywhere, which is what keeps this
safe.

So the honest next step is one of:

1. **M11 — férias.** Pure code, unblocked, no external dependency. The obvious pick.
2. **Buy the A1 certificate (§M1) and answer §M13**, then verify M6 end to end in homologação. This is
   the critical path for anything fiscal, and it is procurement, not engineering.
3. **M13 — admin surfaces + test debt**, including M12's missing resolution endpoint for ambiguous
   statement lines.

Do **not** start M7 (NFe phases 7–9) yet: phases 5–9 all consume SEFAZ's answers, so building them on
an unverified cStat mapping compounds guesswork. Phase 7 (DANFE from an authorized-XML fixture) and
phase 8 (`BodyBuilder` attachments) are the two exceptions that need no autorizador.

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

- **M12**: ambiguous statement lines are visible but have **no resolution endpoint** — a human can see
  them, not clear them. Natural M13 work; no UI contract was invented for it.
- **M3**: the stock ledger covers `Sale`/`Refund` only. Bills' goods receipt and its reversal still
  mutate `StorageQuantity` directly; `Receipt`/`Adjustment` exist in the enum and the shared method is
  ready. Also `PayableBillService._orderRepository` and `_orderedProductRepository` are now dead.
- **M10**: adding an absence after a competência is billed does not retroactively adjust the bill.
  Delete-and-regenerate is the existing escape hatch.
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
