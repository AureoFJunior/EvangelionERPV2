# Fiscal module — real NFe (55) / NFCe (65)

Specs for replacing the NFe stub with real SEFAZ issuance for **SP, RS, SC, PR**.

| Document | Audience | Contents |
|---|---|---|
| [`HANDOFF.md`](./HANDOFF.md) | whoever picks this up next | **Start here.** Current state, review queue before committing, blockers, next steps, and the traps worth re-reading before phase 4. |
| [`SPEC-engineering.md`](./SPEC-engineering.md) | backend | Library evaluation, schema, layering, provider contracts, migrations, phase-by-phase file list, risks |
| [`SPEC-product-qa.md`](./SPEC-product-qa.md) | product, QA, frontend | Permission matrix, Gherkin acceptance criteria, fiscal business rules, state machine, Expo screen spec, **frozen API contract**, test plan, rollout + support runbook. Written in pt-BR — the fiscal domain is Brazilian and the tenant's accountant is a reader. |

The two were written independently and disagree in places. **This file records the resolution; where the specs conflict, this file wins.**

## What is broken today

`EvangelionERPV2.NFe.Application/Providers/StubNFeProvider.cs:11-42` fabricates a 44-digit access key with `RandomNumberGenerator`, emits a 6-line `<nfeStub>` document, and hardcodes `Status = Authorized`. It contacts nobody. `NFeSettings.Enabled` is `false` in `appsettings.Production.json:66-72`, so `IssueAsync` returns `null` (`NFeService.cs:60-64`) and the endpoint answers 204 — the button does nothing and says nothing. `EvangelionERPV2.NFe.Infra` is an empty csproj.

The stub's worst property is that it reports success. A user believes they issued a note that does not exist.

## Locked decisions

1. **SEFAZ via `Unimake.DFe`**, pinned to `20260722.1716.27`. MIT, `netstandard2.0`, fully managed dependencies. Covers `Autorizacao`, `RetAutorizacao`, `ConsultaProtocolo`, `StatusServico`, `RecepcaoEvento` (110111), `Inutilizacao`, and resolves UF endpoints itself from `CodigoUF` + `TipoAmbiente` (SP=35, PR=41, SC=42, RS=43). Package reference goes in **`EvangelionERPV2.NFe.Infra` only** — Infra is the lowest layer here (`Domain → Infra`, `Application → Domain + Infra`).
2. **A1 certificate in AWS Secrets Manager, one secret per enterprise** — `evangelion/{env}/nfe/cert/{enterpriseId}`. CSC token likewise at `evangelion/{env}/nfe/csc/{enterpriseId}`. The database stores **metadata only**: thumbprint, subject CN, NotAfter, uploadedAt.
3. **Fiscal data** = nullable fiscal columns on `Product` + `EnterpriseFiscalSettings` defaults, resolved Product → enterprise default → **hard validation error**. Never a guessed value.
4. **No frontend code in this repo.** The Expo app is separate and consumes the frozen contract in `SPEC-product-qa.md` §7.

## Conflict resolutions

| Topic | Resolution |
|---|---|
| Permission naming (`nfe.settings.*` vs `fiscal.*`) | **`nfe.*`** — one namespace, matches the existing `RbacPermissions.NFe` block at `Web/Security/RbacPermissions.cs:148-153`. |
| Status enum size (6 values vs 9) | **Persist 6**: `Pending, Authorized, Cancelled, Error, Rejected, Denied`. Append-only, so existing wire values 1–4 are unchanged and no data migration is needed. The "Processing / Awaiting SEFAZ / Cancellation requested" distinctions are **derived** from `TransmittedAtUtc` and `CancelRequestedAt`, and surfaced as a computed `DisplayStatus` string on the DTO. One source of truth, nine labels for the UI. |
| `nfe.cancel` tier | **Supervisor → Manager.** Cancellation is irreversible with a short legal window; aligns with `Orders.Delete`. This is a **privilege reduction versus current behaviour** (`RbacRolePermissionMap.cs:40`) and must appear in the release notes. |
| 204 semantics | **204 disappears from `NFeController` entirely.** Today it means three incompatible things: order missing, issuance disabled, no note yet. Breaking change accepted in v1 rather than minting a v2 — one internal consumer, and the endpoint is inert in production. |
| NFe test location | **New `EvangelionERPV2.NFeModule.Test` project**, matching every other module. Existing tests in `EvangelionERPV2.Test/{Resilience,Security}` stay put. |

## The one thing that must not be got wrong

A POST to SEFAZ times out. The note may be authorized, rejected, or may never have arrived. Retrying blind either double-emits — real tax liability, real money — or burns a number.

The ordering **is** the mechanism: claim the row → allocate the number atomically → build the access key **locally** and sign → persist key/number/XML and **commit** → set `TransmittedAtUtc` and **commit** → only then transmit.

Because the key is known before the first byte goes out:

- `Pending ∧ TransmittedAtUtc IS NULL` → provably never sent → retransmit the same number.
- `Pending ∧ TransmittedAtUtc IS NOT NULL` → **unknown; refuse to retransmit.** Resolve by `ConsultaProtocolo`: cStat 100 → Authorized, 110/301/302/303 → Denied, **217 (not in SEFAZ's base) → genuinely never landed, safe to retransmit the same number**, anything else → Rejected.

`NFeReconciliationService` sweeps stragglers so a crashed request thread never strands a row. Full detail in `SPEC-engineering.md` §H R1 and the state table in `SPEC-product-qa.md` §5.2.

## Implementation status

| Phase | State | Notes |
|---|---|---|
| 0 — Unimake feasibility gate | **Blocked** | Needs a real A1 certificate; see prerequisites below. Nothing in phases 4+ is proven until this closes. |
| 1 — Fiscal settings (schema + admin CRUD) | Done | `EnterpriseFiscalSettings`, `NFeNumberSequence`, `NFeFiscalSettingsService`, `NFeFiscalSettingsController`, migration `20260801120000`. |
| 2 — Certificate lifecycle | Done | `EnterpriseCertificateProvider` (Secrets Manager, `X509CertificateLoader` + `EphemeralKeySet`, 15-min cache), `NFeCertificateService`, upload/status/delete actions. No migration — the metadata columns landed in phase 1. |
| 3 — Document schema, numbering, tenant denormalisation | Done | `NFeDocument` fiscal columns, `Rejected`/`Denied`, `AllocateNextNumberAsync`, audit allowlist and redaction, migration `20260801130000` with backfill. |
| 4–9 | Not started | Gated on phase 0. |

Phases 1–3 are all independent of which SEFAZ library is chosen, which is why they could ship ahead of the gate.

### Decisions taken during implementation

- **`NFeDocument.EnterpriseId` has no foreign key.** It is a plain indexed column. A relationship to `Enterprise` would create a delete cascade onto fiscal documents, and an authorized NFe must survive for five years regardless of what happens to the tenant row. The `Order` FK still carries the referential relationship.
- **`NFeNumberSequence`'s unique index is not filtered on `IsActive`**, unlike the settings index. A soft-deleted counter that let a replacement row restart at 1 would re-issue `nNF` values already transmitted to SEFAZ.
- **`ResetErroredDocumentForRetry` now refuses anything that is not `Error`.** Resetting a document whose outcome is unknown would erase the access key and the `TransmittedAtUtc` write-ahead marker — exactly the two things that make a duplicate emission detectable.
- **`DocumentValidationHelper` moved** from `Web/FluentValidator` to `Shared/Utils` (now public, plus `BeValidCnpj`) so the fiscal service could reuse the existing mod-11 implementation instead of duplicating it.
- **Contingency columns are inert placeholders** carrying a `ponytail:` comment. Nothing reads them in v1.

## Blocking prerequisites

1. **An A1 certificate for a real CNPJ.** There is no SEFAZ sandbox certificate; homologação requires a real one (e-CNPJ A1, ~R$150–250/yr). **Phase 0 cannot start without it.**
2. End-to-end issuance in a UF needs the CNPJ to hold an active IE there. Realistically one UF gets true end-to-end coverage; the other three get `StatusServico` + schema validation. **Which UF is the pilot in?**
3. Are pilot tenants Simples Nacional (CRT 1/2 ⇒ CSOSN) or Regime Normal (CRT 3 ⇒ CST)? Both are supported; this decides which default set is exercised first.
4. **Verify Chromium resolves in the runtime image before Phase 7.** `mcr.microsoft.com/dotnet/aspnet:10.0` ships none of its shared libraries. If the Bills boleto PDF works in production this is a non-issue; if it only works locally, Phase 7 inherits a pre-existing bug.

## Values marked UNVERIFIED

These are **configuration columns, never constants**, and must be confirmed against each UF's RICMS by the pilot tenant's accountant before that tenant goes to produção:

- NFCe cancellation window (30 min is the national reference; RS and PR have practised different values).
- NFe extemporaneous cancellation beyond 24 h — **do not implement**.
- NFCe consumer-identification threshold (R$ 10.000 reference).
- CC-e deadline (30 days) — out of scope v1 regardless.

The mandatory homologação legend string must be checked against the current MOC/NT at implementation time; NFCe uses the `NFC-E` variant.

## Out of scope v1

Offline NFCe contingency · SVC-AN/SVC-RS contingency (**first item of v1.1**) · Carta de Correção · inutilização de numeração · other UFs (**the UI must block them explicitly**) · NFS-e (municipal, thousands of variants, shares zero infrastructure) · CT-e/MDF-e · manifestação do destinatário · NFe complementar/ajuste/devolução · SPED/EFD export · automatic DIFAL/FCP and ST calculation.

Each exclusion carries its re-entry trigger in `SPEC-product-qa.md` §10.
