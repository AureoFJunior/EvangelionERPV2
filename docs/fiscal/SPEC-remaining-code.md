# Fiscal — remaining code work (NFe + Boleto)

Written 2026-08-11. Last verified against the working tree on `feature/33-Boleto-and-NFE-emission`,
2026-08-14.

> **Resume point and milestone status live in [`../ROADMAP-STATUS.md`](../ROADMAP-STATUS.md). Read that first.**

This is the **code-only** backlog. Everything that needs a human, a bank or a certificate lives in
[`TODO-manual.md`](./TODO-manual.md). Deep detail for NFe stays in
[`../nfe/HANDOFF.md`](../nfe/HANDOFF.md) and [`../nfe/SPEC-engineering.md`](../nfe/SPEC-engineering.md);
this file does not repeat them, it lists what is still missing and where.

---

## 1. State of play, measured not remembered

| Area | Reality in the tree today |
|---|---|
| NFe phases 1–3 | Done. `EnterpriseFiscalSettings`, `NFeNumberSequence`, certificate lifecycle, `NFeDocument` fiscal columns, numbering SQL. |
| NFe phase 4 | **Most of the way.** Access-key builder, item resolver, `Product` fiscal columns, write-ahead ordering, reconciler, and the provider contract moved down to Infra with `NFeIssueContext`. `Unimake.DFe 20260722.1716.27` is referenced by `NFeModule.Infra` only. **Missing: `NFeXmlBuilder`, `UnimakeConfiguracaoFactory`, `SefazNFeProvider`** — that is the whole remaining gap. |
| Issuance | `NFeIoC.cs:61` still registers `StubNFeProvider` — hardcodes `Status = Authorized` and invents a protocol. It no longer invents an access key: the service owns that now. `NFeSettings.Enabled = false` in all environments. |
| Cancel | **Service side done (2026-08-19).** Refuses anything not `Authorized`, enforces the window from the tenant profile, commits `CancelRequestedAt` before the round trip, records a cancellation only when SEFAZ homologates it. Still no transport behind it, and `StubNFeProvider.CancelAsync` now reports failure rather than faking success. |
| Consult | **Service side done (2026-08-19).** Asks SEFAZ when — and only when — the outcome is unknown, and adopts the answer through the same `ApplyOutcome` the reconciler uses. Still no `ConsultaProtocolo` behind it. |
| Write-ahead ordering | **Done (2026-08-14).** `IssueAsync` allocates the nNF, builds the chave and commits both, then commits `TransmittedAtUtc`, then transmits. `IsOutcomeUnknown` can now be true and its guard fires. Covered by `NFeModule.Test/NFe/NFeIssuanceWriteAheadTests.cs`. |
| Reconciliation | **Done (2026-08-19).** `NFeReconciliationService` + `NFeReconciliationWorker` (hosted, gated on `NFeSettings.Enabled`) sweep `GetStalePendingAsync`: unknown outcomes are consulted through the new `INFeProvider.ConsultAsync`, never-transmitted rows go back to `Error` keeping their allocation. `StubNFeProvider.ConsultAsync` answers "still unknown", so nothing is invented. |
| Boleto payment state | **Done (M2).** `Status/PaidAt/PaidAmount/SettlementProtocol`, `BillNumberSequence` replacing the derived NossoNumero, a filtered unique index so a reissue supersedes, and a forecast that no longer counts an issued boleto as received cash. B2, B4 and half of B5 below are closed by it. |
| Boleto | Generates a real BoletoNetCore barcode + digitable line + HTML/PDF and emails it. **Nothing registers the title with a bank and nothing ever marks it paid.** `BillsSettings.Enabled = false` in production. |

Both features are inert in production, which is the only reason the stubbed cancel and the
unregistered boleto have not caused damage.

**The unknown outcome now has an owner.** A document left `Pending` with `TransmittedAtUtc` set is
picked up by the sweeper, consulted, and either resolved or counted; after
`ReconciliationMaxAttempts` it drops out of the query and waits for a human, which is the honest end
state for a note SEFAZ will not answer for. It resolves nothing real until `SefazNFeProvider` exists —
the stub's consult always answers "still unknown" — but the control flow, the backoff and the
never-retransmit invariant are in place and tested.

**Number reuse landed with it.** A document that provably never left keeps its allocated nNF and chave
through the `Error` → retry cycle: `ResetErroredDocumentForRetry` no longer clears them when
`TransmittedAtUtc` is NULL, and `AssignNumberAndAccessKeyAsync` reuses the allocation when the chave
still carries this document's number **and** the current AAMM. Anything else re-allocates. Without
this, every crashed issuance burned a number that only inutilização can account for.

---

## 2. NFe — ordered backlog

### 2.1 Phase 4 remainder — real modelo 55 issuance

**The certificate gates transmission, not development.** Phase-0 check (d) signed a complete modelo-55
note (CRT 3 / ICMS00 / PISAliq / COFINSAliq) to 4685 bytes and passed the embedded 4.00 XSD using a
**self-signed** PKCS#12 generated in-test — that is `NFeXmlBuilder`'s whole job, already done once in
throwaway code (`CheckD.cs` in the gate console is the working reference for the object graph). The real
A1 cert (§M1) is only needed for the mutual-TLS handshake with SEFAZ: check (c). Everything upstream of
the socket can be written and tested today.

| Slice | Cert needed? |
|---|---|
| `NFeXmlBuilder` | No — XSD validation is local, schemas are embedded resources |
| `UnimakeConfiguracaoFactory` | No — pure `EnterpriseFiscalSettings` → `Configuracao` mapping |
| `SefazNFeProvider` | No to write; drive it with a fake at the boundary |
| `INFeProvider`/`NFeProviderResult` move down to Infra | No — mechanical, four `using` lines |
| ~~**`IssueAsync` write-ahead rewrite**~~ | **DONE 2026-08-14** — see below |
| ~~`INFeProvider`/`NFeProviderResult` move down to Infra~~ | **DONE 2026-08-19** — plus `NFeIssueContext`, and Consult/Cancel/Status on the interface |
| ~~Cancel and consult stop lying~~ | **DONE 2026-08-19** — see §2.2 |
| ~~`NFeReconciliationService` + hosted sweeper~~ | **DONE 2026-08-19** |
| cStat 107 `StatusServico` round trip | **Yes** — PR already rejects a self-signed cert at TLS |
| Real rejection surface | **Yes** — SEFAZ has hundreds of rejection rules that only fire on a real submission, so any cStat mapping written now ships unverified |

Buy the certificate in parallel regardless: credenciamento (§M4) is a separate process and often the
slower of the two.

### Library API, verified by reflection on 2026-08-19 (do not re-derive)

`Unimake.DFe 20260722.1716.27` is now referenced by `EvangelionERPV2.NFeModule.Infra` and nothing
else. These were read off the assembly, not guessed:

- **Services live in `Unimake.Business.DFe.Servicos.NFe`**: `Autorizacao`, `ConsultaProtocolo`,
  `RecepcaoEvento`, `StatusServico`, `RetAutorizacao`, `Inutilizacao`, `DistribuicaoDFe`,
  `ConsultaCadastro`. **Every one of these type names also exists under `Servicos.NFGas`,
  `Servicos.NFSe.*` and others** — a bare `using` plus a short name binds to the wrong document type
  and still compiles. Always qualify or import the exact namespace.
- `Autorizacao` ctors: `(EnviNFe, Configuracao)` and `(string, Configuracao)`. Results are read from
  `Result`, `RetConsReciNFe`, `NfeProcResult(s)`, `ConteudoXMLAssinado`, `RetornoWSXML`,
  `HttpStatusCode`. Schema validation happens **in the constructor**, throwing
  `Unimake.Exceptions.ValidarXMLException` with element/line/column — do not call
  `ValidarEstruturaXML.ValidarServico` yourself (§7.1.3).
- `Configuracao` carries `CertificadoDigital`, `CertificadoKeyStorageFlags`, `CodigoUF`, `TipoAmbiente`,
  `TipoDFe`, `Modelo`, `SchemaVersao`, `CSC`, `CSCIDToken`, `TipoEmissao`-adjacent fields and the
  proxy/host settings. No endpoint URL is needed: resolution from `CodigoUF` alone was verified for
  SP/RS/SC/PR in §7.3.
- Enum values, confirmed: `TipoDFe.NFe = 0`, `NFCe = 1`; `ModeloDFe.NFe = 55`, `NFCe = 65`;
  `TipoAmbiente.Producao = 1`, `Homologacao = 2`; `TipoEmissao.Normal = 1`; `UFBrasil` is the IBGE
  code (`SP = 35`, `PR = 41`, `SC = 42`, `RS = 43`), plus `SVRS = 92` and the contingency authorizers.
- Payload graph in `Unimake.Business.DFe.Xml.NFe`: `EnviNFe { Versao, IdLote, IndSinc, NFe }` →
  `InfNFe { Versao, Ide, Emit, Dest, Det, Total, Transp, Cobr, Pag, InfAdic, InfRespTec, Id, Chave }`.
  `ICMS` is a container of the CST-specific classes (`ICMS00`, `ICMS10`, … `ICMSSN101` … `ICMSSN900`),
  and `PISAliq` / `COFINSAliq` carry `CST/VBC/PPIS/VPIS` and `CST/VBC/PCOFINS/VCOFINS`.
  `RetConsSitNFe` exposes `CStat`, `XMotivo`, `DhRecbto`, `ChNFe`, `ProtNFe`, `ProcEventoNFe`.
- The `*Field` properties (`VBCField`, `PPISField`, …) are the library's string/serialisation
  companions to the typed ones. Set the typed property; the `Field` twin is what lands in the XML.

**How to re-check any of this cheaply:** drop a temporary xUnit test in `NFeModule.Test` that reflects
over `typeof(Unimake.Business.DFe.Servicos.Configuracao).Assembly` and `Assert.Fail`s with the names.
The assembly loads on Linux with zero loader exceptions (§7 check (a)), so reflection is instant and
needs no certificate.

New files, all in `EvangelionERPV2.NFe.Infra` (the lowest layer; `Application → Domain + Infra`):

```
Providers/INFeProvider.cs           MOVED down from Application, + Cancel/Consult/Status
Providers/NFeProviderResult.cs      MOVED down, + Cstat/XMotivo/ReceiptNumber/IsTransient/NeverTransmitted
Providers/NFeIssueContext.cs        new
Sefaz/UnimakeConfiguracaoFactory.cs EnterpriseFiscalSettings -> Unimake Configuracao
Sefaz/SefazNFeProvider.cs           the ONLY file allowed to name a Unimake type
Xml/NFeXmlBuilder.cs                order + items + settings -> Unimake NFe object
```

`Unimake.DFe 20260722.1716.27` goes in **`EvangelionERPV2.NFeModule.Infra.csproj` only**. Moving the two
interfaces down changes five `using` lines: `NFeService.cs`, `NFeIoC.cs`, `StubNFeProvider.cs`,
`EvangelionERPV2.Test/Resilience/NFeServiceIdempotencyTests.cs` and
`EvangelionERPV2.NFeModule.Test/NFe/NFeIssuanceWriteAheadTests.cs`.

**`NFeService.IssueAsync` now runs the transmit-safe ordering** (done 2026-08-14):

1. claim the row (unchanged)
2. `AllocateNextNumberAsync` — called from `AssignNumberAndAccessKeyAsync`
3. build the chave locally (`NFeAccessKeyBuilder`); **signing still belongs to `NFeXmlBuilder`**, which
   does not exist yet, so `XmlContent` is still whatever the provider returns
4. persist key/number/series/environment, still `Pending`, `TransmittedAtUtc` still `NULL` → **commit**
5. `TransmittedAtUtc = UtcNow` → **commit** (the write-ahead marker, `MarkTransmittedAsync`)
6. only now transmit

Two consequences worth knowing before touching this again:

- The document's `AccessKey`, `Number`, `Series` and `Environment` are **no longer adopted from
  `NFeProviderResult`**. They are allocated before transmission precisely so a timed-out request can be
  reconciled against them, so `StubNFeProvider` stopped fabricating them and `SefazNFeProvider` must
  receive them rather than invent them — that is what `NFeIssueContext` is for.
- `EmitterNow()` fixes AAMM in the chave to `America/Sao_Paulo`, correct for the v1 UF allowlist
  (SP/PR/SC/RS, all UTC-3 with no DST since 2019). Marked `ponytail:` in the source; revisit if the
  allowlist grows westward.

The library is 100% synchronous, so the call is `await Task.Run(() => svc.Executar(), ct)` and the
`CancellationToken` cannot stop bytes already in flight — which is exactly why step 5 commits before
step 6. Do not "improve" it into a `ct`-cancellable call.

Also in this slice:
- ~~**`NFeReconciliationService` + a hosted sweeper over `GetStalePendingAsync`**~~ — **done 2026-08-19.**
  Both cases are kept apart exactly as specified: `IsOutcomeUnknown` is consulted and never
  retransmitted; `TransmittedAtUtc == null` goes back to `Error` with its number intact, and the ordinary
  issue path sends the same number again. `SefazNFeProvider` only has to make `ConsultAsync` return a real
  cStat mapping — the service adopts `Status`/`Cstat`/`XMotivo`/`Protocol`/`XmlContent`/`AuthorizedAtUtc`
  and needs no further change. Knobs: `ReconciliationIntervalMinutes` (5), `ReconciliationStaleAfterMinutes`
  (10, must stay above the SEFAZ timeout) and `ReconciliationMaxAttempts` (5) on `NFeSettings`.
  Tests: `NFeModule.Test/NFe/NFeReconciliationServiceTests.cs`, plus number reuse in
  `NFeIssuanceWriteAheadTests`.
- `NFeIoC`: `INFeProvider` becomes `AddScoped<SefazNFeProvider>`, **not** `AddSingleton` — it carries per-request state.
- `verProc` string must be ≤ 20 chars (XSD maxLength; `"EvangelionERPV2-phase0"` was rejected at 22).
- `TipoDFe.NFe = 0 / NFCe = 1` is a different enum from `ModeloDFe.NFe = 55 / NFCe = 65`. Both are needed; cross-wiring them is silent.

Tests: access-key DV vectors (exists), resolver fallback per level (exists),
`TransmitTimeout_LeavesPendingAndDoesNotReallocateNumber` (exists, `NFeIssuanceWriteAheadTests`), still
missing a golden-file `infNFe` snapshot for a fixed order — that one waits on `NFeXmlBuilder`.

### 2.2 Phase 5 — cancel and consult stop lying

**Service side done 2026-08-19; the transport behind it is not.** `NFeService.CancelAsync` now
refuses any document that is not `Authorized`, enforces the cancellation window from the tenant's
profile (NFe and NFCe differ), commits `CancelRequestedAt` before the round trip for the same reason
`TransmittedAtUtc` exists, and records a cancellation only when SEFAZ homologates it (135/155).
`ConsultAsync` asks SEFAZ when — and only when — the outcome is unknown, and adopts the answer through
the same `ApplyOutcome` the reconciler uses. `StubNFeProvider.CancelAsync` now reports failure rather
than success. What remains is `SefazNFeProvider` actually sending the event and the cStat mapping
below.

`CancelAsync` must send event 110111 and adopt SEFAZ's answer; the fabricated `CANCEL-{timestamp}`
protocol has to go. `ConsultAsync` must call `ConsultaProtocolo` and map cStat: `100 → Authorized`,
`110/301/302/303 → Denied`, **`217 → never landed, safe to retransmit the same number`**, else `Rejected`.
Add `GetServiceStatus` (`StatusServico`). Enforce the cancellation window from configuration, never a
constant.

**Until this ships, `nfe.cancel` on a real note is a lie in the database** — the note stays valid at
SEFAZ. If the flag is ever flipped before phase 5, remove the cancel endpoint first.

Open decision carried from HANDOFF §1.3: moving `nfe.cancel` from Supervisor to Manager is a privilege
**reduction** and needs release notes. Not yet applied.

### 2.3 Phase 6 — NFCe (65)

CSC secret at `evangelion/{env}/nfe/csc/{enterpriseId}`, the `UpdateCsc` endpoint, QR code, consumer
identification threshold as configuration. Skip entirely if the answer to §M6 in the manual list is "NFe only".

### 2.4 Phase 7 — DANFE PDF + XML download

**Lift `RenderPdfAsync` out of `BillsService` into `Shared/Utils/HeadlessPdfRenderer.cs` first** — it is
currently a private static in `BillsService.cs:316` holding a `static IBrowser?` and a `SemaphoreSlim`.
Two modules sharing one Chromium process is fine; two modules each launching their own is not.

New routes need log-redaction prefixes in **both** `RequestLoggingMiddleware` and
`ExceptionHandlingMiddleware`: `/NFe/DownloadDanfe/`, `/NFe/DownloadXml/`, `/NFe/SendByEmail/`,
`/NFe/Refresh/`. The access key embeds the CNPJ and the note number — this leaks if forgotten.

### 2.5 Phase 8 — email with attachments

DANFE PDF + XML through `BodyBuilder`. Reuse the `EmailAttachment` path `BillsService` already uses.

### 2.6 Phase 9 — retire the stub

Delete `StubNFeProvider.cs`, make `EnterpriseFiscalSettings.Enabled` authoritative, drop the global
`NFeSettings.Enabled` kill switch or keep it explicitly as an ops brake.

### 2.7 Loose ends, any phase

| Item | Where |
|---|---|
| Remove 204 from `NFeController` — today it means "order missing", "issuance disabled" and "no note yet" interchangeably. Breaking change, coordinate with the Expo release. | `Web/Controllers/NFeController.cs` (6 sites) |
| Rate-limit rules for `Issue` / `Cancel` / `SendByEmail` / `UploadCertificate` | `appsettings.Production.json` |
| Every new controller action needs an `[InlineData]` row | `RbacControllerPolicyCoverageTests` |
| A new permission must be added in **four** places or startup throws | `RbacPermissions`, `RbacPolicies`, `RbacRolePermissionMap`, `.All` |
| `NFeDocument.EnterpriseId` FK — decide. The cascade argument does not hold (`NFeNumberSequence` already `Restrict`s the tenant delete), so a `Restrict` FK is free integrity. | `AppDbContext` + migration |
| `CreateProductRequestDTO`/`UpdateProductRequestDTO` still expose no fiscal fields — 15 fields plus 15 validator rules; nothing writes fiscal data through the API yet. | `Shared/DTOs`, `ProductController` |

---

## 3. Boleto — what the code is missing

The boleto module is **further from production than it looks**. It produces a correct-looking document;
it has no relationship with a bank.

### B1. Settings are global, so every tenant emits the platform's boleto — HIGH

`BillsSettings` is a single `IOptions<BillsSettings>` bound from `appsettings.*.json`
(`BillsIoC` → `BillsService.cs:40`). Beneficiary CNPJ, agency, account, wallet and transmission code are
**one set for the whole installation**. In a multi-tenant ERP that means tenant B's customer pays into
tenant A's bank account.

NFe already solved this: `EnterpriseFiscalSettings` + per-enterprise secrets. Boleto needs the same shape —
`EnterpriseBillingSettings` (per `EnterpriseId`, admin CRUD, account digits in Secrets Manager not in the
row), and `BillsSettings` demoted to defaults + kill switch. Do not build a second mechanism; mirror
`NFeFiscalSettingsService`.

Blocked on the ownership decision in §M-B1 of the manual list — if the platform is legally the sole
beneficiary, this is not needed and should be written down rather than left ambiguous.

### B2. `NossoNumero` is derived, not allocated — ~~HIGH~~ **DONE (M2, 2026-08-19)**

`BillNumberSequence` per (enterprise, bank, wallet) with a configurable start and ceiling, allocated
through the same `UPDATE ... OUTPUT deleted.NextNumber WITH (ROWLOCK)` statement NFe uses, plus
`AND NextNumber <= Ceiling` inside the same atomic statement so an exhausted convênio range hands out
nothing. The original problem, for the record:

```csharp
// BillsService.cs:525
var raw = $"{order.Id:N}{order.CreatedAt:yyyyMMddHHmmss}";
var digits = new string(raw.Where(char.IsDigit).ToArray());
return digits.Substring(digits.Length - length, length);   // last 8 digits
```

Banks assign a **range** per convênio and require uniqueness inside it, usually sequential. Truncated GUID
digits are neither: they can collide (8 digits from an arbitrary hex-derived string), they can fall outside
the contracted range, and a collision at the bank rejects the title or, worse, credits the wrong one.

Fix: an allocated counter, exactly like `NFeNumberSequence`. `AllocateNextNumberAsync`'s
`UPDATE … OUTPUT deleted.NextNumber` (`NFeRepository.AllocateNextNumberSql`) is the pattern — atomic at any
isolation level, correct across instances. Add `BillNumberSequence` per (enterprise, bank, wallet) with a
configurable start and ceiling from the bank contract.

### B3. No CNAB remessa/retorno — the boletos are not registered — HIGH

`grep -ri "cnab\|remessa\|retorno"` over the solution returns **nothing**. `BillsSettings.RegistrationType`
is bound to `TipoFormaCadastramento` and passed to BoletoNetCore, but nothing ever produces a remessa file.
With a *com registro* wallet — which is what most banks now require — the bank has never heard of the title,
so it cannot be paid at another bank, cannot be protested, and cannot be reconciled.

Work: remessa generation (BoletoNetCore ships CNAB 240/400 writers, use them — do not hand-roll a layout),
retorno parsing, and the transport. Bank layout manual and SFTP/API credentials are §M-B4/§M-B5.

### B4. No payment state — ~~HIGH~~ **DONE (M2, 2026-08-19)**

`Status` (`Registered/Paid/Overdue/Cancelled/Rejected`, with `Overdue` derived rather than stored),
`PaidAt`, `PaidAmount`, `SettlementProtocol`, a manual settle endpoint at Manager, and the cash-flow
forecast wired to it. Nothing writes `Rejected` yet — the value exists so M9's retorno mapping does not
have to migrate the column. The original problem:

`Bill` (`Shared/Entities/Bill.cs`) has `OrderId, BankCode, OurNumber, DocumentNumber, IssueDate, DueDate,
Amount, DigitableLine, BarCode, HtmlContent`. There is no status, no `PaidAt`, no `PaidAmount`, no
`SettlementProtocol`. `IBillsService` is three methods: get, generate, PDF.

So: a customer pays, and the ERP never knows. Accounts receivable, the cash-flow forecast and the order
lifecycle all read a boleto that is permanently in an unknown state.

Minimum: `Status` (`Registered / Paid / Overdue / Cancelled / Rejected`), `PaidAt`, `PaidAmount`, plus
whatever writes them (retorno processing from B3, or a bank webhook/API if the chosen bank offers one).
Then wire it into `CashFlowForecastService`, which currently cannot distinguish a paid boleto from an
issued one.

### B5. No cancel / reissue — **half done (M2)**

Reissue exists: the unique index is filtered on non-cancelled rows, so a reissue supersedes rather
than mutates, and it regenerates from the order's current dates rather than taking a due date of its
own. A standalone cancel (baixa) is still missing on purpose — it belongs with the remessa instruction
it has to send, which is M9. The original problem:

There is no way to cancel a boleto (baixa) or reissue one with a new due date. Today the unique index on
`Bill.OrderId` means one order gets exactly one boleto forever; a wrong due date is unrecoverable through
the API. Needs a `Cancel` (with remessa instruction when registered) and a reissue that supersedes rather
than mutates.

### B6. No PIX / QR on the boleto — MEDIUM, decide before B3

Brazilian payers increasingly expect the híbrido boleto. It is a separate bank agreement (§M-B6) and a
different field set. Decide before building the remessa layout, because adding PIX afterwards means
revisiting it.

### B7. Real bank credentials must not land in `appsettings.Production.json` — HIGH when B1 ships

The production file currently carries **placeholder** account data (`"BeneficiaryNotes": "Bill
demonstrativo - configurar dados reais"`). Harmless today, but the real agency/account/transmission code
must go to Secrets Manager like `EVA_TOKEN_KEY` does, never into a committed file. Decide the storage
shape when B1 is built, not after.

### B8. Chromium in the production image — verify, do not assume

`Web/Dockerfile:11-28` installs the Chromium shared libraries and `libfontconfig1`, and PuppeteerSharp
downloads the browser at runtime. The Dockerfile looks right; nobody has confirmed the download succeeds
in the production network (egress may be restricted) or that the writable download path survives a
read-only filesystem. `BillsService.BuildBillAttachmentsAsync` swallows the failure and sends the mail
without the PDF, so this fails **silently**. Phase 7 inherits the same renderer — a five-minute check that
can cost a sprint (§M-B8).

---

## 4. Suggested order

1. **NFe phase 4 remainder** — start now, do not wait for the certificate (§2.1). The write-ahead
   rewrite and the reconciler are both done; next is `NFeXmlBuilder` (signing, so `XmlContent` stops
   coming from the transport), then `UnimakeConfiguracaoFactory` + `SefazNFeProvider`, which is what
   finally makes the reconciler's consult say something.
2. **B4 + B2** — payment state and allocated `NossoNumero`. Independent of every external decision, and B4 is the gap that makes the current boleto commercially useless.
3. **B1** once ownership is decided; it changes the shape of everything boleto after it.
4. **B3** once the bank and layout are in hand.
5. NFe 5 → 7 (with the `HeadlessPdfRenderer` lift) → 8 → 9. Phase 6 only if NFCe is in scope.

Then **stop and reassess** once phase 4 is written but unverified. Phases 5–9 all consume SEFAZ's answers,
so building them before one real note has round-tripped means the cStat mapping is guesswork compounding
on guesswork. Phase 7 (DANFE from an authorized-XML fixture) and phase 8 (`BodyBuilder` attachments) are
the exceptions — neither needs SEFAZ at all.

## 5. Commands

```bash
dotnet build EvangelionERPV2.sln
dotnet test  EvangelionERPV2.sln
dotnet test  EvangelionERPV2.NFeModule.Test/EvangelionERPV2.NFeModule.Test.csproj
dotnet test  EvangelionERPV2.BillsModule.Test/EvangelionERPV2.BillsModule.Test.csproj

# after any migration — the snapshot test does NOT catch a missing [Migration] attribute
dotnet ef migrations list \
  --project EvangelionERPV2.Shared/EvangelionERPV2.Shared.csproj \
  --startup-project EvangelionERPV2.Web/EvangelionERPV2.Web.csproj --no-connect
```

Run the `net-review` skill over the diff before merging.
