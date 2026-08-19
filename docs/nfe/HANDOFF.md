# NFe/NFCe — handoff

Written 2026-07-29. Read this first, then [`README.md`](./README.md) for the decisions and the two specs for detail.

---

## Where we are

Phases 1–3 are implemented and green: **669 tests pass, 0 failures**, NFe module 123. Phases 1–3 were all independent of which SEFAZ library we pick, which is why they shipped ahead of the feasibility gate.

```
Phase 0  Unimake feasibility gate    3 of 4 checks GREEN — only cStat 107 still needs an A1 cert
Phase 1  Fiscal settings CRUD        done
Phase 2  Certificate lifecycle       done
Phase 3  Document schema + numbering done
Phase 4  Real NFe 55 issuance        started — library-agnostic slice done (see §7)
Phase 5  Cancel / consult            not started
Phase 6  NFCe 65                     not started
Phase 7  DANFE PDF + XML download    not started
Phase 8  Email with attachments      not started
Phase 9  Retire the flag, delete stub not started
```

Nothing issues a real note yet. `StubNFeProvider` is still the registered `INFeProvider` and `NFeSettings.Enabled` is still `false` everywhere.

**The verdict on Unimake.DFe is SUITABLE on Linux/net10.0, and that is now measured rather than inferred.** See §7 for the gate results and the five findings that change the plan.

---

## 1. Review queue — before any of this is committed

These are the judgement calls made during implementation. Each is defensible, none is obvious, and all are cheap to reverse now and expensive later.

### 1.1 Snapshot regeneration touched pre-existing work — look at this first

`dotnet ef migrations add` rewrites `EvangelionERPV2.Shared/Migrations/AppDbContextModelSnapshot.cs` wholesale. Running it absorbed three entities that were **missing from the committed snapshot**: `Employee`, `EnterprisePayrollSettings`, `PayrollBillDetail`. That is the known "migrations invisible to EF" drift in this repo, not something this work introduced — but the fix landed here, inside a diff that is otherwise about NFe.

Verified after the fact: only additions, zero entity removals, and `AppDbContextModelSnapshotSyncTests` is green. Still worth eyeballing the diff before committing.

```bash
git diff EvangelionERPV2.Shared/Migrations/AppDbContextModelSnapshot.cs
```

### 1.2 `NFeDocument.EnterpriseId` has no foreign key

EF's generated migration wanted a **cascade delete from `Enterprise` onto fiscal documents**. That contradicts the five-year retention obligation: an authorized NFe cannot vanish because a tenant row was deleted. It is now a plain indexed column with no navigation property. The `Order` FK still carries the referential relationship.

Decide whether you accept a denormalised column with no database-level referential integrity. The alternative is a FK with `OnDelete(Restrict)`, which keeps integrity but blocks hard-deleting an `Enterprise` forever.

### 1.3 `nfe.cancel` moves Supervisor → Manager

**This is a privilege reduction against current behaviour** (`RbacRolePermissionMap.cs`, previously in `SupervisorPermissions`). Anyone who is Supervisor today and can cancel will get a 403 after this ships. Needs release notes and a heads-up to affected tenants. **Not yet applied** — the permission constant is unchanged so far; this is planned for phase 5 when cancellation becomes real. Confirm you still want it.

### 1.4 `DocumentValidationHelper` moved out of the Web project

`Web/FluentValidator/DocumentValidationHelper.cs` → `Shared/Utils/DocumentValidationHelper.cs`, now `public`, plus a new `BeValidCnpj` and a public `NormalizeDocument`. Four validators gained a `using`. Done so the fiscal service could reuse the existing mod-11 implementation rather than duplicate it. Behaviour unchanged; `git log --follow` still works because it was moved with `git mv`.

### 1.5 `ResetErroredDocumentForRetry` now throws

It refuses anything whose status is not `Error`. Resetting a document with an unknown outcome would erase the access key and the `TransmittedAtUtc` write-ahead marker — the two things that make a duplicate emission detectable. Today it is only reached from the `Status == Error` branch, so the throw is unreachable in current code and exists to stay that way.

### 1.6 Inert columns

`ContingencyEnabled` / `ContingencyEnteredAt` / `ContingencyReason` on `EnterpriseFiscalSettings` are placeholders carrying a `ponytail:` comment. Nothing reads them in v1. If you would rather add them when contingency is actually built, delete them now — the migration has not been applied anywhere yet.

### 1.7 Commit hygiene

The working tree mixes this NFe work with substantial **pre-existing uncommitted work** (Employee module, Payroll, seven migrations dated July). Several files contain both, e.g. `RbacPermissions.cs`, `Program.cs`, `EvangelionERPV2.sln`, `AppDbContext.cs`. Splitting into separate commits will need `git add -p`. Worth doing — a reviewer cannot otherwise tell fiscal changes from payroll changes.

---

## 2. Blockers and open questions

| # | Item | Blocks | Who decides |
|---|---|---|---|
| 1 | **An A1 certificate for a real CNPJ.** No SEFAZ sandbox certificate exists. e-CNPJ A1, roughly R$150–250/yr. | Phase 0, therefore 4–9 | You / finance |
| 2 | **Which UF is the pilot in?** End-to-end issuance needs an active IE in that state, so realistically one UF gets true coverage and the other three get `StatusServico` + schema validation only. | Phase 4 test plan | You |
| 3 | **Simples Nacional (CRT 1/2 ⇒ CSOSN) or Regime Normal (CRT 3 ⇒ CST)?** Both are supported; it decides which default set is exercised first. | Phase 4 fixtures | Pilot tenant's accountant |
| 4 | **Does Chromium resolve in the runtime image?** `mcr.microsoft.com/dotnet/aspnet:10.0` ships none of its shared libraries. If the Bills boleto PDF works in production this is a non-issue; if it only works locally, phase 7 inherits a pre-existing bug. Five-minute check that can cost a sprint. | Phase 7 | Check before phase 7 |
| 5 | **Cancellation windows per UF.** NFe 24 h is reliable. NFCe 30 min is the national reference and **UNVERIFIED per UF** — RS and PR have practised different values. Already modelled as configuration, not constants. | Production go-live | Pilot tenant's accountant |
| 6 | **NFCe consumer-identification threshold.** R$ 10.000 reference, **UNVERIFIED per UF**. Also configuration. | Phase 6 | Same |

Items 5 and 6 do not block development — the columns exist and are defaulted. They block a tenant flipping to produção.

---

## 3. Next session: Phase 0, then Phase 4

### Phase 0 — feasibility gate (do this first, it is a hard gate)

Throwaway `net10.0` console referencing `Unimake.DFe 20260722.1716.27`, run inside `mcr.microsoft.com/dotnet/sdk:10.0` on Linux. Prove all four:

- [ ] Package restores and the assembly loads on Linux. The package is `netstandard2.0`/`net472` only, with no public statement of .NET Core support — all its docs are Windows/Visual Studio, and its own end-user product is .NET Framework 4.8.
- [ ] `X509CertificateLoader.LoadPkcs12(bytes, pwd, EphemeralKeySet)` yields a key that **`SignedXml` can actually sign with**. This is the single most common Linux failure mode for Brazilian fiscal libraries. `EnterpriseCertificateProviderTests.GetAsync_WithStoredCertificate_ReturnsUsablePrivateKey` already asserts the signing half against a generated PKCS#12, so if it fails it will be inside Unimake's own signing, not ours.
- [ ] `StatusServico` returns cStat 107 in homologação for `CodigoUF` 35, 43, 42, 41, with `TipoDFe.NFe` **and** `TipoDFe.NFCe`.
- [ ] A signed `enviNFe` passes the library's own schema validation.

**Exit:** all four green, or the spec is amended to `zeus.nfe` (MIT, `net8.0`, native .NET Core, same coverage). Do not start phase 4 first — the whole point of the gate is that phase 4 is wasted if it fails.

### Phase 4 — real NFe model 55, homologação, issue only

Files to create in `EvangelionERPV2.NFe.Infra`:

```
Providers/INFeProvider.cs          MOVED down from NFe.Application, + Cancel/Consult/Status/Inutilizar
Providers/NFeProviderResult.cs     MOVED down, + Cstat/XMotivo/ReceiptNumber/IsTransient/NeverTransmitted
Providers/NFeIssueContext.cs       new
Sefaz/UnimakeConfiguracaoFactory.cs  EnterpriseFiscalSettings -> Unimake Configuracao
Sefaz/SefazNFeProvider.cs          the ONLY file that touches Unimake types
Xml/NFeAccessKeyBuilder.cs         44-digit key + mod-11 DV, pure, unit-testable without SEFAZ
Xml/NFeItemFiscalResolver.cs       Product -> enterprise default -> hard error
Xml/NFeXmlBuilder.cs               order + items + settings -> Unimake NFe object
```

Moving `INFeProvider`/`NFeProviderResult` down changes four `using` lines: `NFeService.cs`, `NFeIoC.cs`, `StubNFeProvider.cs`, `EvangelionERPV2.Test/Resilience/NFeServiceIdempotencyTests.cs`. Infra is the **lowest** layer here (`Domain → Infra`, `Application → Domain + Infra`), so the real provider cannot live in Application.

Also in phase 4:

- Add `Product` fiscal columns (`Ncm`, `Cest`, `Cean`, `CfopIntra`, `CfopInter`, `Origem`, `UnidadeTributavel`, `Cst`, `Csosn`, `AliquotaIcms`, `CstPis`, `AliquotaPis`, `CstCofins`, `AliquotaCofins`, `CodigoBeneficioFiscal`) + `ProductDTO` + migration `20260801140000_AddProductFiscalColumns`.
- Rewrite `NFeService.IssueAsync` around the transmit-safe ordering (see §4).
- `NFeReconciliationService` + a hosted sweeper over `GetStalePendingAsync` — already on the repository, unused so far.
- `NFeIoC`: swap `INFeProvider` to `SefazNFeProvider` as **`AddScoped`, not `AddSingleton`** — it now touches per-request state.

Tests: access-key DV against known vectors, resolver fallback at each level, a golden-file snapshot of `infNFe` for a fixed order, and `TransmitTimeout_LeavesPendingAndDoesNotReallocateNumber`.

### Phases 5–9

Unchanged from the plan; see [`SPEC-engineering.md`](./SPEC-engineering.md) §G for the per-phase file lists. Short version: 5 = cancel event 110111 + real consult; 6 = NFCe with CSC and QR; 7 = DANFE PDF (lift `RenderPdfAsync` out of `BillsService` into `Shared/Utils/HeadlessPdfRenderer.cs` first) + XML download; 8 = email attachments via `BodyBuilder`; 9 = per-tenant flag becomes authoritative and `StubNFeProvider.cs` is deleted.

---

## 4. Traps — re-read before writing phase 4

**The duplicate-emission trap is the whole ballgame.** A SEFAZ timeout leaves three possible truths: authorized, rejected, or never arrived. Retrying blind either double-emits — real tax liability, real money — or burns a number.

The ordering *is* the mechanism, and the schema for it already exists:

1. Claim the row (`Pending`, `TransmittedAtUtc = NULL`) — existing unique-index dance in `NFeService`.
2. `AllocateNextNumberAsync` — already implemented, atomic, cross-instance.
3. Build the access key **locally** and sign.
4. Persist key/number/series/XML, still `Pending`, `TransmittedAtUtc` still `NULL`. **Commit.**
5. Set `TransmittedAtUtc = UtcNow`. **Commit.** ← the write-ahead marker.
6. Only now transmit.

Recovery, because the key is known before the first byte leaves:

- `Pending ∧ TransmittedAtUtc IS NULL` → provably never sent → retransmit the same number.
- `Pending ∧ TransmittedAtUtc IS NOT NULL` → **unknown; refuse to retransmit.** Consult SEFAZ and adopt the answer: 100 → Authorized, 110/301/302/303 → Denied, **217 (not in SEFAZ's base) → genuinely never landed, safe to resend the same number**, anything else → Rejected.

`NFeDocument.IsOutcomeUnknown` already encodes that predicate. Use it; do not re-derive it.

Other things that will bite:

- **Hand-written migrations need both `[DbContext(typeof(AppDbContext))]` and `[Migration("<full_timestamp_and_name>")]`.** Without them `Database.Migrate()` silently skips the file **and the snapshot test still passes green** — the test explicitly says it does not guard this. Verify with `dotnet ef migrations list`.
- **Do not `git checkout` the model snapshot to redo a migration.** EF computes the next migration by diffing against that file, so reverting it makes EF re-emit tables that earlier migrations already create. (This happened during phase 3; recovery was a throwaway `migrations add` to rebuild the snapshot, then hand-writing the real migration.)
- **`internal` does not cross assembly boundaries.** Helpers used by the test project or by `NFe.Application` must be `public`. Bit us twice.
- `WaitForFinalizedIssuanceAsync` must **not** throw for a document whose outcome is unknown — that maps to a 500. Return the document and answer **202**.
- `RbacPolicies.ForPermission` throws for any permission missing from `RbacPermissions.All`, so a new permission must be added in **four** places or startup fails.
- Every new controller action needs an `[InlineData]` row in `RbacControllerPolicyCoverageTests`.

---

## 5. Deferred and explicitly not done

- `UpdateCsc` endpoint and CSC secret storage — phase 6, with NFCe.
- `GetServiceStatus` endpoint — phase 5, needs a real provider.
- Rate-limit rules for `Issue` / `Cancel` / `SendByEmail` / `UploadCertificate` in `appsettings.Production.json` — add when those endpoints exist.
- Network egress allowlist for the SEFAZ hostnames — infrastructure, not code. **Corrected against the endpoints the library actually resolved in the phase-0 gate (§7.3); the list previously written here was wrong for PR and SC and would have caused an outage on the first real issuance:** `*.fazenda.sp.gov.br`, `*.nfce.fazenda.sp.gov.br`, `*.sefazrs.rs.gov.br`, `*.svrs.rs.gov.br`, `*.sefa.pr.gov.br` (**not** `*.fazenda.pr.gov.br`), `*.nfe.fazenda.gov.br`. **SC has no endpoint of its own** — it is served entirely by SVRS (`nfe-homologacao.svrs.rs.gov.br`), so `*.sef.sc.gov.br` is not needed at all.
- Log-redaction prefixes for the four new routes (`/NFe/DownloadDanfe/`, `/NFe/DownloadXml/`, `/NFe/SendByEmail/`, `/NFe/Refresh/`) in **both** `RequestLoggingMiddleware` and `ExceptionHandlingMiddleware` — phase 7, when the routes exist. Easy to forget; it leaks a key carrying the CNPJ and note number.
- Removing 204 from `NFeController` — phase 4/5, when the controller is rewritten. Breaking change, needs coordinating with the Expo app release.

---

## 6. Commands

```bash
dotnet build EvangelionERPV2.sln
dotnet test  EvangelionERPV2.sln
dotnet test  EvangelionERPV2.NFeModule.Test/EvangelionERPV2.NFeModule.Test.csproj

# after any migration — the snapshot test does NOT catch a missing [Migration] attribute
dotnet ef migrations list \
  --project EvangelionERPV2.Shared/EvangelionERPV2.Shared.csproj \
  --startup-project EvangelionERPV2.Web/EvangelionERPV2.Web.csproj --no-connect

dotnet test EvangelionERPV2.Test/EvangelionERPV2.Test.csproj \
  --filter "FullyQualifiedName~AppDbContextModelSnapshotSync"
```

Run the `net-review` skill over the fiscal diff before merging — layering, async/EF, security and LGPD.

---

## 7. Phase 0 gate — results (run 2026-07-30)

Throwaway `net10.0` console, `Unimake.DFe 20260722.1716.27`, run on Linux (host SDK 10.0.301, not the
`sdk:10.0` container — the dependency set is fully managed, so the container adds no new risk; note the
distinction if you want the container run for completeness). Self-signed RSA-2048 PKCS#12 generated
in-test, shaped like an e-CNPJ A1 (digitalSignature + nonRepudiation KU, client-auth EKU).

| Check | Result |
|---|---|
| (a) restores and the assembly loads on Linux | **GREEN** — 3181 types reflected, **zero loader exceptions**. `netstandard2.0` asset selected, `ImageRuntimeVersion=v4.0.30319` (cosmetic). |
| (b) `LoadPkcs12(…, EphemeralKeySet)` yields a key `SignedXml` can sign with | **GREEN** — `ComputeSignature` *and* `CheckSignature` both pass. Also proven through Unimake's own signer in (d). **The single most common Linux failure mode for Brazilian fiscal libraries is retired.** |
| (c) `StatusServico` cStat 107, homologação, UF 35/43/42/41 × NFe/NFCe | **STILL BLOCKED** on a real A1 cert — but everything up to mutual TLS is green, see §7.3. |
| (d) a signed `enviNFe` passes the library's own schema validation | **GREEN** — complete modelo-55 note (CRT 3 / ICMS00 / PISAliq / COFINSAliq) signed to 4685 bytes with `<SignatureValue>`, validated against the embedded 4.00 XSD. |

**Exit criterion is effectively met. Do not amend the spec to `zeus.nfe`.** The only open item is (c),
which cannot be closed by any amount of engineering — it needs the certificate.

### 7.1 Five findings that change the plan

1. **The library is 100% synchronous. Zero `*Async` methods in the entire assembly.** `Executar()` returns
   `void` and blocks on HTTP. This solution is async top to bottom, so `SefazNFeProvider` must wrap it:
   `await Task.Run(() => svc.Executar(), ct)`. **A `CancellationToken` cannot cancel a transmission in
   flight** — only `Configuracao.TimeOutWebServiceConnect` bounds it. This *strengthens* the case for the
   write-ahead marker in §4: a cancelled or timed-out request does not stop the bytes, so the
   `TransmittedAtUtc` commit must happen before `Executar()`, exactly as specified. Do not "improve" this
   into a `ct`-cancellable call; the cancellation would be a lie.
2. **The XSDs are embedded resources, not files** — 80 NFe `.xsd` among 1611 resources. Nothing to ship in
   the Docker image, no `SchemaArquivo` path to configure. Removes a deployment concern the spec left open.
3. **Do not call `ValidarEstruturaXML.ValidarServico` directly.** Handed a bare `Configuracao` it swallows an
   `NullReferenceException` into `ResultadoValidacao.MensagemRetorno` and reports `Validado = false` — a
   failure that looks like a schema rejection but is not one. Real validation happens in the service ctor
   (`new Autorizacao(enviNFe, cfg)`), which throws `Unimake.Exceptions.ValidarXMLException` with genuinely
   useful text (element name, line, column, the violated datatype constraint).
4. **The library computes the chave, `cDV` and `cNF` itself** (`InfNFe.Chave`, `Ide.CDV`, `Ide.CNF`). We still
   need our own `NFeAccessKeyBuilder` — the chave must exist *before* transmission for §4 to work — but the
   two are now cross-checked: `NFeAccessKeyBuilderTests.Build_MatchesChaveGeneratedByUnimakeForSameInputs`
   asserts our output equals a chave Unimake generated for the same inputs. Independent agreement on mod-11
   and field ordering, rather than agreement with our own reading of the spec.
5. **Reforma tributária / IBS-CBS is already modelled in the library** — `Imposto.IBSCBS`, `Imposto.IS`,
   `Total.IBSCBSTot`, `Ide.CMunFGIBS`, `Prod.GCred`, `TipoCreditoPresumidoIBSZFM`. R3 in the spec treats
   NT 2025.002 as a looming rewrite; it is closer to a version bump plus field population. Still out of
   scope for v1, but the risk is smaller than written.

### 7.2 Two footguns worth knowing before phase 4

- **`TipoDFe.NFe = 0` and `TipoDFe.NFCe = 1`.** The 55/65 values live on a *different* enum, `ModeloDFe`
  (`NFe = 55`, `NFCe = 65`). Two enums, both needed, easy to cross-wire silently.
- `verProc` maxLength is 20. `"EvangelionERPV2-phase0"` is 22 and was rejected by the XSD. Pick the
  `verProc` string deliberately.
- `Configuracao.CertificadoKeyStorageFlags` exists, so `EphemeralKeySet` can be set through configuration
  rather than only at load time.

### 7.3 Endpoint resolution — verified, and the allowlist was wrong

Resolution from `CodigoUF` alone works for all four UFs and both document types; we never set a URL.
Observed homologação endpoints:

| UF | NFe | NFCe |
|---|---|---|
| SP 35 | `homologacao.nfe.fazenda.sp.gov.br` | `homologacao.nfce.fazenda.sp.gov.br` |
| RS 43 | `nfe-homologacao.sefazrs.rs.gov.br` | `nfce-homologacao.sefazrs.rs.gov.br` |
| SC 42 | `nfe-homologacao.svrs.rs.gov.br` | `nfe-homologacao.svrs.rs.gov.br` (same host — SVRS) |
| PR 41 | `homologacao.nfe.sefa.pr.gov.br` | `homologacao.nfce.sefa.pr.gov.br` |

Egress reached SEFAZ in all eight cases. SP, RS and SC completed the TLS handshake and returned an
application-layer response (the library reported a missing `<nfeResultMsg>` tag, i.e. a SOAP fault body).
**PR rejected the client certificate at the TLS layer** — `OpenSslCryptographicException: ssl/tls alert
certificate unknown` — which is the correct behaviour for a self-signed cert and independently proves the
certificate *is* being presented to SEFAZ. That is why (c) needs the real thing and nothing else does.

This is where the §5 allowlist correction comes from: PR is `sefa.pr.gov.br`, not `fazenda.pr.gov.br`, and
SC needs no domain of its own.

### 7.4 What landed in phase 4 already

The library-agnostic slice, deliberately built with no `Unimake` reference so it was not gated on (c):

- `NFe.Infra/Xml/NFeAccessKeyBuilder.cs` — 44-digit chave + mod-11 DV, pure, cross-checked against Unimake.
- `NFe.Infra/Xml/NFeItemFiscalResolver.cs` — product → enterprise-default → hard error, per §B.2.
- `Product` fiscal columns (15, all nullable) + `ProductDTO` + migration `20260801140000_AddProductFiscalColumns`
  + hand-edited snapshot. `MapperConfig` needed no change (existing `ReverseMap` covers it).

Suite: **719 passed / 0 failed** (baseline 669), NFe module 174. `dotnet ef migrations list` shows the new
migration, so the `[Migration]` attribute is really there.

Still gated on (c): `NFeXmlBuilder`, `SefazNFeProvider`, `UnimakeConfiguracaoFactory`, the
`INFeProvider`/`NFeProviderResult` move down to Infra, and the `NFeService.IssueAsync` rewrite.
`CreateProductRequestDTO`/`UpdateProductRequestDTO` were left alone on purpose — they are hand-mapped in
`ProductController`, so mirroring 15 fields there means 15 validator rules too, and nothing writes fiscal
data through the API yet.

The gate console is throwaway and lives outside the repo; `CheckD.cs` in it is a working reference for the
`NFeXmlBuilder` object graph if you want it before it is deleted.

---

## 8. Review findings (2026-07-30) — two fixed, the rest open

Two were fixed in place because they are security- or money-critical and cheap:

- **`AWSKMSKeyProvider` retained the certificate PFX and password in the static cache even when
  `bypassCache: true`.** The bypass skipped the cache *read* but the write at the end of
  `GetKMSKeyInternalAsync` was unconditional, so a tenant's signing identity stayed in a
  process-static, never-evicted dictionary for the container's lifetime — falsifying §F.1. A bypassing
  read now writes nothing *and* evicts any entry an earlier ordinary read left, so a stale pre-rotation
  value cannot outlive the rotation. Two tests in `AWSKMSKeyProviderSecretResolutionTests`.
- **`NFeService.IssueAsync`'s blanket `catch` set `Status = Error` on any exception, including a
  transmit timeout.** That made `ResetErroredDocumentForRetry` legal on the next attempt, which wipes
  `AccessKey` and `TransmittedAtUtc` — the two things that make a duplicate emission detectable. So the
  §1.5 guard could never fire: it was the right invariant at the wrong control point. The downgrade is
  now gated on `!pendingDocument.IsOutcomeUnknown`, and an unknown outcome stays `Pending` for the
  reconciler with an error log. (`IsOutcomeUnknown` had **zero** callers before this — §4's
  "use it, do not re-derive it" was not yet being honoured anywhere.)

All 14 remaining findings were then worked through. **13 fixed, 1 rejected on evidence.** Suite is
**737 passed / 0 failed**.

1. **HIGH — audit recorded no EDIT rows. FIXED, and it was far wider than the fiscal module.** Every
   read in `RepositoryBase` is `AsNoTracking`, so *every* update flow in the solution is
   read-detached → mutate → `Update`, which attaches with OriginalValues copied from the instance being
   saved: all properties marked modified, `OriginalValue == CurrentValue` for each, empty change set, no
   row. This suppressed EDIT rows for **Customer, Product, Order, Bill and PayableBill too**, not just
   `NFeDocument`/`EnterpriseFiscalSettings` — and nothing in the suite asserted an EDIT row anywhere, which
   is why it was never noticed. Fixed once in `AppDbContext`: `RestoreOriginalValuesForAudit[Async]` reloads
   database originals for audited modified entries that show the degenerate attach signature, before the
   audit factory runs. Non-audited entities cost nothing. `AuditTrailEditRowTests` covers the real
   transition and the no-op case (a no-op must still write nothing).
2. **MEDIUM — `GetStalePendingAsync` skipped the never-transmitted half. FIXED.** Predicate is now
   `Pending ∧ (TransmittedAtUtc ?? CreatedAt) <= cutoff ∧ AttemptCount < max`, so both recoverable states
   have an owner and §4's "provably never sent ⇒ retransmit the same number" finally has a code path.
   `CreatedAt` is the clock for never-transmitted rows; `UpdatedAt` is unusable, it defaults to
   `DateTime.MinValue` and would make every freshly claimed row look instantly stale.
3. **MEDIUM — `AllocateNextNumberAsync` untested. FIXED** via the spec's stated alternative: the statement
   is now `NFeRepository.AllocateNextNumberSql` and `NFeNumberAllocationSqlTests` pins every load-bearing
   clause — `OUTPUT deleted.NextNumber` present, no `SELECT`, exactly one statement, `ROWLOCK`, all four
   key parameters, and no interpolation (it takes a tenant-supplied series string).
4. **MEDIUM — dispose the cached `X509Certificate2`. REJECTED — the suggested fix is a worse bug.**
   Implementing the `PostEvictionCallback` immediately turned
   `GetAsync_AfterInvalidate_ReadsTheRotatedCertificate` red with
   `CryptographicException: m_safeCertContext is an invalid handle`. Instances are shared, so a rotation
   calling `Invalidate()` — or plain 15-minute expiry — would dispose a certificate another thread is
   mid-signature with, i.e. crash a live SEFAZ transmission. The key is held by a `SafeHandle` and
   reclaimed on finalization, so not disposing is a delayed reclaim rather than a real leak. Kept shared,
   documented as "callers must not dispose" on `GetAsync`, with the reasoning recorded at the call site.
5. **MEDIUM — `nfe.certificate.read` leaked emitente identity at Manager. FIXED** by removing `Cnpj` and
   `SubjectCn` from `NFeCertificateStatusDTO` rather than by moving the permission. `CnpjMatchesFiscalProfile`
   already carries the only operational signal derived from them without disclosing the values, and admins
   still get `CertSubjectCn`/`CertThumbprint` from `EnterpriseFiscalSettingsDTO` behind `nfe.settings.read`.
   Managers keep the expiry warning they actually need. No RBAC change, so no release-note item after all.
6. **MEDIUM — tenant scoping for `Consult`/`Cancel`. FIXED.** Both now go through one private
   `GetScopedDocumentByAccessKeyAsync`, which queries the scoped overload first so a foreign row is never
   materialised, and falls back to the unscoped read *only* for pre-denormalisation rows
   (`EnterpriseId == Guid.Empty`), still verifying ownership through the Order. Four tests in
   `NFeServiceTenantScopeTests`: scoped hit, scoped miss, legacy row owned by another tenant, and empty
   enterprise id doing no lookup at all.
7. **MEDIUM — speculative repository surface. FIXED (partially, deliberately).** Deleted
   `GetByOrderAsync` and `GetByEnterprisePagedAsync` (list/paging endpoints that do not exist) and the
   now-unused `MaxPageSize`. **Kept `GetStalePendingAsync` and `AllocateNextNumberAsync`** — both are
   named phase-3 deliverables that phase 4 consumes directly, the numbering SQL is the core of the
   duplicate-emission mechanism and is already correct, and deleting it to rewrite it later is the more
   expensive path. Both now have tests, which was the real gap.
8. LOW — **FIXED.** `X509KeyStorageFlags.Exportable` dropped; `EphemeralKeySet` alone, which the phase-0
   gate proved sufficient for `SignedXml` on Linux (§7).
9. LOW — **FIXED.** `NFeCertificateException` moved to `Shared/Exceptions`; the Web controller no longer
   reaches into `NFeModule.Infra.Certificates`.
10. LOW — **FIXED.** All three dead `catch (NotFoundDatabaseException)` blocks removed, plus the test that
    asserted one of them by mocking a throw `RepositoryBase.GetAllAsync` cannot produce.
11. LOW — **FIXED.** `XMotivo` and `CancelXMotivo` added to the redaction set: rejection motives can echo
    submitted values back.
12. LOW — **FIXED.** `NFeDocument.EnterpriseId` is now added nullable, backfilled, residual NULLs set to
    `Guid.Empty`, then `AlterColumn` to NOT NULL — no lingering DEFAULT constraint, so the snapshot matches
    and the next `migrations add` emits nothing spurious.
13. LOW — **FIXED.** `UpsertSettingsAsync` wraps the profile write and `EnsureNumberSequencesAsync` in one
    `ExecuteInTransactionAsync`, closing the window where a saved profile had no counters.
14. LOW — **partially fixed, rest declined.** `Inspect` now disposes the certificate it loads. The second
    parse inside `StoreAsync` stays: the first `Inspect` in `NFeCertificateService.UploadAsync` is what
    validates the CNPJ against the profile *before* anything is written, so it is not redundant, and one
    extra PKCS#12 load on a rare admin upload is not worth an API change.

---

## 9. Needs a human — nothing here is fixable in code

| # | Item | Blocks | Who |
|---|---|---|---|
| 1 | **Buy an e-CNPJ A1 certificate** (~R$150–250/yr) for a real CNPJ. No SEFAZ sandbox certificate exists. This is the only thing standing between us and closing phase 0 check (c). | End-to-end phases 4–9 | You / finance |
| 2 | **IAM + secret provisioning**: the API role needs `secretsmanager:GetSecretValue`/`PutSecretValue`/`CreateSecret` on `evangelion/{env}/nfe/cert/*`. Nothing works without it and the failure looks like "certificate not configured". | Phase 4 | You / infra |
| 3 | **Egress allowlist** for the SEFAZ hostnames, using the corrected list in §5 — PR is `*.sefa.pr.gov.br`, and SC needs no domain of its own. Infrastructure, not code. | Phase 4 | You / infra |
| 4 | **Which UF is the pilot in?** Full issuance needs an active IE in that state, so one UF gets true coverage and the others get `StatusServico` + schema validation only. | Phase 4 test plan | You |
| 5 | **Simples Nacional (CRT 1/2 ⇒ CSOSN) or Regime Normal (CRT 3 ⇒ CST)?** Both supported; decides which default set is exercised first. | Phase 4 fixtures | Pilot tenant's accountant |
| 6 | **Is NFCe (65) needed at launch?** If NFe 55 alone unblocks the business, phase 6 ships later and the critical path shortens by roughly a third. | Scope | You |
| 7 | **Confirm the DANFE is emailed only on explicit user action**, not automatically on authorization. The spec assumes explicit; the reverse means un-sending emails. | Phase 8 | You |
| 8 | **Cancellation windows per UF.** NFe 24 h is reliable. NFCe 30 min is the national reference and is UNVERIFIED per UF — RS and PR have practised different values. Already configuration, not constants. | Production go-live | Pilot tenant's accountant |
| 9 | **NFCe consumer-identification threshold.** R$ 10.000 reference, UNVERIFIED per UF. Also configuration. | Phase 6 | Same |
| 10 | **Does Chromium resolve in the production runtime image?** `mcr.microsoft.com/dotnet/aspnet:10.0` ships none of its shared libraries. If the Bills boleto PDF works in *production* this is a non-issue; if it only works locally, phase 7 inherits a pre-existing bug. Five-minute check that can cost a sprint — needs production access. | Phase 7 | Check before phase 7 |
| 11 | **FK decision on `NFeDocument.EnterpriseId`** — see the §1.2 correction below. The original justification does not survive scrutiny, so this is now a live choice rather than a settled one. | Before commit | You |
| 12 | **Split the working tree into separate commits** (§1.7). Fiscal work is interleaved with Employee/Payroll in `RbacPermissions.cs`, `Program.cs`, `AppDbContext.cs`, `MapperConfig.cs` and the snapshot; needs `git add -p`. I have not committed anything. | Before merge | You |
| 13 | **Subscribe someone to the SEFAZ Notas Técnicas mailing list.** NTs land 2–4×/year and can add mandatory fields with a hard cutover date. This is an operational commitment, not a technical one (R3). | Ongoing | You |

### Correction to §1.2

The *conclusion* (no FK) is defensible, but **the stated reason does not hold**: `NFeNumberSequence`
already carries `OnDelete(Restrict)` to `Enterprise`, so hard-deleting a tenant is *already* blocked
forever. A `Restrict` FK on `NFeDocument.EnterpriseId` would cost nothing new and buy database-level
integrity. Either add it or stop citing the cascade argument.

§1.4 was verified genuinely behaviour-preserving. §1.1's snapshot region matches both migrations,
additions only. §1.5 is correct and genuinely unreachable — keep it as a tripwire, but note it is not
what prevents duplicate emission. §1.6: keep the inert columns. §1.7 still stands and is material.
