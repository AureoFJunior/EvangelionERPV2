# SPEC — Real NFe (55) / NFCe (65) issuance, cancellation and consulta

Author: backend. Status: draft for review. Repo: `/home/peanuut/CodeProjects/EvangelionERPV2` @ net10.0, Linux containers (`EvangelionERPV2.Web/Dockerfile:4` → `mcr.microsoft.com/dotnet/aspnet:10.0`).

---

## A. VERIFY FIRST — Unimake.DFe

**Verified findings (fetched 2026-07-29):**

| Item | Finding |
|---|---|
| Package id | `Unimake.DFe` (exact, case as shown) |
| Latest version | **20260722.1716.27**, published 2026-07-22, 6.54 MB, 424.2K total downloads |
| Target frameworks | **`netstandard2.0`** and `net472` only. No `net10.0` TFM. |
| net10.0 usable? | **Yes.** NuGet's computed-compatibility list on the package page explicitly includes .NET 5.0–**10.0**. A `netstandard2.0` assembly is consumable from `net10.0` with no shim beyond what the SDK emits. |
| Dependencies | `Microsoft.CSharp ≥4.7.0`, `System.Security.Cryptography.Xml ≥10.0.10`, `Unimake.Cryptography`, `Unimake.Extensions`, `Unimake.Utils` (all ≥20260720.1548.24). **All cross-platform**; no `System.ServiceModel.*`, no `System.Drawing.Common`, no WCF, no P/Invoke package. `System.Security.Cryptography.Xml` at 10.0.x is the .NET-10-aligned build — strong signal the maintainers track current .NET. |
| License | **MIT** (stated on the GitHub repo `Unimake/DFe`). The NuGet page does not render a license expression — treat as "MIT per repo LICENSE", record the repo LICENSE hash in the ADR. |
| NFe 55 + NFCe 65 | Yes — package description lists NFe and NFCe as first-class document types; `TipoDFe` enum selects between them. |
| Services present | Namespace `Unimake.Business.DFe.Servicos.NFe` contains, verified from the official API doc: `Autorizacao`, `RetAutorizacao` (consulta recibo do lote), `ConsultaProtocolo`, `ConsultaCadastro`, `StatusServico`, `RecepcaoEvento`, `Inutilizacao`, `DistribuicaoDFe`, and base `ServicoBase`. **Everything you asked for is covered.** Eventos 110111 (cancelamento) and 110110 (CC-e) go through `RecepcaoEvento` with the corresponding `EventoNFe`/`detEvento` XML classes. |
| UF endpoint resolution | **Yes, self-resolving.** `Unimake.Business.DFe.Servicos.Configuracao` exposes `CodigoUF`, `TipoAmbiente` (1-Produção / 2-Homologação), `TipoDFe`, `Servico`, `SchemaVersao`, `TipoEmissao` (1-Normal, 2-Contingência, 6/7/8-SVC-AN/SVC-RS/…), and the library "loads environment-specific endpoints based on `TipoAmbiente` and `CodigoUF`" from an embedded config. `WebEnderecoProducao` / `WebEnderecoHomologacao` / `WebActionProducao` / `WebActionHomologacao` exist as **overrides only**. SP (own autorizador), RS (own), PR (own) and SC (via SVRS) are all in the embedded table — we set `CodigoUF` (35/43/41/42) and never touch the URL properties. |
| NFCe QR | `Configuracao.CSC` ("Código de segurança do contribuinte") and `Configuracao.CSCIDToken` (int) are first-class properties; the library builds `qrCode`/`urlChave` in `infNFeSupl`. QR versions 2 and 3 supported. |
| Certificate | `Configuracao.CertificadoDigital` (`X509Certificate2`), or `CertificadoArquivo` + `CertificadoSenha`. **We use `CertificadoDigital` only** — never write the .pfx to disk. |

**Verdict: SUITABLE. No fallback needed.** Two residual risks, both cheap to retire:

1. **No public statement of Linux/.NET-Core support.** The GitHub README, the wiki and the issue tracker are silent; all installation docs are Windows/Visual Studio. Their own end-user product (UniNFe) is .NET Framework 4.8. The `netstandard2.0` TFM plus a fully managed, cross-platform dependency set means it *should* work, but **this is unproven for our target (Linux container, net10.0)**. → **Phase 0 is a hard gate**: a throwaway console that calls `StatusServico` against homologação for UF 35/43/41/42 from inside `mcr.microsoft.com/dotnet/sdk:10.0` on Linux, with a real A1 .pfx loaded from bytes. If that fails, the fallback is **`zeus.nfe`** (MIT, `net8.0`, actively maintained, native .NET Core, covers 55/65 + eventos + inutilização) — but do not plan for it until Phase 0 fails.
2. **`X509Certificate2` construction.** `new X509Certificate2(byte[], string)` is `[Obsolete] SYSLIB0057` since .NET 9 and will warn/error under our build. Use `X509CertificateLoader.LoadPkcs12(pfxBytes, password, X509KeyStorageFlags.EphemeralKeySet)`. Ephemeral keys work on Linux with OpenSSL and keep nothing on disk; verify in Phase 0 that `SignedXml` can use the resulting `RSA` handle (this is the single most common Linux failure mode for Brazilian fiscal libs).

**Version pin:** exact version, no floating range. Add the `PackageReference` to `EvangelionERPV2.NFe.Infra/EvangelionERPV2.NFeModule.Infra.csproj` **only**. The layer graph in this repo is `Domain → Infra` and `Application → Domain + Infra` (see `EvangelionERPV2.NFe.Domain/EvangelionERPV2.NFeModule.Domain.csproj` `<ProjectReference>` and `EvangelionERPV2.NFe.Application/…Application.csproj`), so Infra is the lowest layer and is the correct home.

Sources: [NuGet Unimake.DFe](https://www.nuget.org/packages/Unimake.DFe/), [v20260722.1716.27](https://www.nuget.org/packages/Unimake.DFe/20260722.1716.27), [GitHub Unimake/DFe](https://github.com/Unimake/DFe), [API doc — Servicos.NFe](https://unimake.com.br/uninfe/docdll/api/Unimake.Business.DFe.Servicos.NFe.html), [API doc — Configuracao](https://unimake.com.br/uninfe/docdll/api/Unimake.Business.DFe.Servicos.Configuracao.html)

---

## B. DOMAIN MODEL / SCHEMA

### B.1 `EnterpriseFiscalSettings` (new, `EvangelionERPV2.Shared/Entities/EnterpriseFiscalSettings.cs`)

Mirrors `EvangelionERPV2.Shared/Entities/EnterprisePayrollSettings.cs:8-33` exactly: `[Table("EnterpriseFiscalSettings")]`, `: BaseEntity`, `[ForeignKey(nameof(Enterprise))] Guid EnterpriseId`, unique **filtered** index declared fluently in `AppDbContext` (copy `AppDbContext.cs:116-119`, `[Index]` cannot express `HasFilter`).

```
Emitente identity
  Cnpj                  string(14)   required, digits only
  InscricaoEstadual     string(14)   required
  InscricaoMunicipal    string(15)   nullable (only for ISS)
  RazaoSocial           string(60)   required   -- NOT Enterprise.Name; xNome has its own rules
  NomeFantasia          string(60)   nullable
  Crt                   byte         1=Simples, 2=Simples excesso sublimite, 3=Regime Normal, 4=MEI

Emitente address (structured — Enterprise.Adress at Enterprise.cs:24 is one free-text blob, unusable for enderEmit)
  Logradouro            string(60)
  Numero                string(60)
  Complemento           string(60)   nullable
  Bairro                string(60)
  CodigoMunicipioIbge   string(7)    cMun
  Municipio             string(60)   xMun
  Uf                    string(2)    SP|RS|SC|PR (v1 allowlist)
  CodigoUfIbge          short        35|43|42|41 — derived, persisted for the Unimake Configuracao.CodigoUF
  Cep                   string(8)
  CodigoPais            string(4)    default "1058"
  Pais                  string(60)   default "BRASIL"
  Telefone              string(14)   nullable

Numbering (see B.4 — the counter itself lives in NFeNumberSequence, NOT here)
  NFeSeries             string(3)    default "1"
  NFCeSeries            string(3)    default "1"

Environment & operation
  Environment           byte         1=Producao, 2=Homologacao   -- per tenant, replaces NFeSettings.Environment
  Enabled               bool         default false               -- per tenant, replaces NFeSettings.Enabled
  NFCeEnabled           bool         default false
  CscId                 int?         NOT secret; the CSC token itself lives in Secrets Manager (F)
  SchemaVersion         string(5)    default "4.00"

Certificate metadata ONLY (never the key — see F)
  CertThumbprint        string(64)   nullable
  CertSubjectCn         string(256)  nullable
  CertNotAfterUtc       DateTime?    nullable
  CertUploadedAtUtc     DateTime?    nullable

Fiscal defaults (item-level fallback source — decision 3)
  DefaultCfopIntra      string(4)    default "5102"
  DefaultCfopInter      string(4)    default "6102"
  DefaultCst            string(3)    nullable  (CRT=3)
  DefaultCsosn          string(3)    nullable  (CRT=1/2)
  DefaultOrigem         byte         default 0
  DefaultAliquotaIcms   decimal(5,4) default 0
  DefaultCstPis         string(2)    default "07"
  DefaultAliquotaPis    decimal(5,4) default 0
  DefaultCstCofins      string(2)    default "07"
  DefaultAliquotaCofins decimal(5,4) default 0
  DefaultUnidadeTributavel string(6) default "UN"

Contingency (flags only; behaviour OUT OF SCOPE v1 — see H)
  ContingencyEnabled    bool         default false
  ContingencyEnteredAt  DateTime?    nullable
  ContingencyReason     string(256)  nullable
```

Rate columns use `[Column(TypeName = "decimal(5,4)")]` — mandatory, otherwise the global convention at `AppDbContext.cs:54` (`HavePrecision(18,2)`) truncates a 0.0765 PIS rate to 0.08. Precedent: `EnterprisePayrollSettings.cs:15-25`.

**No `WebEnderecoProducao`/`WebEnderecoHomologacao` columns and no DTO fields for them.** Tenant-controlled SEFAZ URLs = SSRF (see F).

### B.2 `Product` fiscal columns (`EvangelionERPV2.Shared/Entities/Product.cs`)

All nullable → blank means "fall back to `EnterpriseFiscalSettings.Default*`".

```
Ncm                 string(8)?     -- the only one that is effectively mandatory to emit
Cest                string(7)?
Cean                string(14)?    -- GTIN; "SEM GTIN" when null
CfopIntra           string(4)?
CfopInter           string(4)?
Origem              byte?          0..8
UnidadeTributavel   string(6)?
Cst                 string(3)?
Csosn               string(3)?
AliquotaIcms        decimal(5,4)?
CstPis              string(2)?
AliquotaPis         decimal(5,4)?
CstCofins           string(2)?
AliquotaCofins      decimal(5,4)?
CodigoBeneficioFiscal string(10)?  -- cBenef, required by some UFs when CST is exempt
```

`Product.UnitOfMeasure` (`Product.cs:31`) stays as the commercial unit (uCom); `UnidadeTributavel` is uTrib.

Resolution helper lives in Infra: `NFeItemFiscalResolver.Resolve(Product, EnterpriseFiscalSettings, bool isInterstate) → ResolvedItemFiscal`. One place, one test.

### B.3 `NFeDocument` changes (`EvangelionERPV2.Shared/Entities/NFeDocument.cs`)

```
+ EnterpriseId       Guid       DENORMALISED (see F) — index, FK to Enterprise
+ Model              short      55 | 65   (Type stays as the enum for the API contract)
+ Cstat              string(3)  "100", "204", "539", "110"…
+ XMotivo            string(255)
+ ReceiptNumber      string(15) nRec from Autorizacao (async lote) — needed for RetAutorizacao recovery
+ TransmittedAtUtc   DateTime?  NULL = never left this process. NOT NULL + Pending = MUST consulta before retry
+ AuthorizedAtUtc    DateTime?  dhRecbto from protNFe
+ CancelRequestedAt  DateTime?  set before the 110111 round-trip, same trap as issuance
+ CancelledAtUtc     DateTime?
+ CancelCstat        string(3)
+ CancelXMotivo      string(255)
+ QrCode             string(1000)  NFCe only (infNFeSupl/qrCode)
+ UrlChave           string(500)   NFCe only (infNFeSupl/urlChave)
+ DigestValue        string(64)
+ AttemptCount       int        default 0 — reconciler backoff
```

`XmlContent` stays `nvarchar(max)`, keeps `[JsonIgnore]` on the DTO (`NFeDocumentDTO.cs:18-19`). **Do not cache the DANFE PDF** in the DB in v1: it is a pure function of `XmlContent` + settings, PuppeteerSharp is already warm (`BillsService.RenderPdfAsync`), and caching it doubles the LGPD blast radius for zero measured benefit. `// ponytail: DANFE re-rendered per download; cache to blob storage if p95 render > 2s.`

### B.4 `NFeNumberSequence` (new entity + table)

```
Id            Guid  PK
EnterpriseId  Guid
Model         short          55 | 65
Series        string(3)
Environment   byte           1 | 2   -- homolog and prod numbering are independent
NextNumber    int            default 1
UNIQUE (EnterpriseId, Model, Series, Environment)   -- non-filtered, hard unique
```

Not on `EnterpriseFiscalSettings` because a tenant legitimately has multiple series and two environments, and because we want the hottest write in the system on its own narrow row.

**Why the existing lock is insufficient.** `NFeService.cs:15-18,338-343` builds 256 process-static `SemaphoreSlim`s keyed on `HashCode.Combine(orderId, type)`. Three independent failures for numbering:
1. It is **in-process**. `EvangelionERPV2.Web` runs as N containers; two pods issue the same `nNF` simultaneously. SEFAZ rejects the loser with cStat 539 *after* the winner burned the chave.
2. It is keyed by **orderId**, not by `(enterprise, model, series)`. Two *different* orders in the same tenant never contend on the same semaphore, which is exactly the case that must serialise for numbering.
3. 256 buckets means unrelated orders false-share a lock — the only thing it does reliably is add latency.

It is nonetheless **correct for what it was written for** (per-order issuance idempotency, backed by the unique index `(OrderId, Type)` at `NFeDocument.cs:21` and the claim/finalize dance at `NFeService.cs:114-134,156-177`). **Keep it. Do not extend it.**

**Concrete mechanism** — one atomic statement, on `INFeRepository`:

```csharp
// EvangelionERPV2.NFe.Domain/Interface/INFeRepository.cs
Task<int> AllocateNextNumberAsync(
    Guid enterpriseId, short model, string series, byte environment,
    CancellationToken ct = default);
```

```sql
-- NFeRepository.AllocateNextNumberAsync, via _context.Database.SqlQueryRaw<int>
UPDATE dbo.NFeNumberSequence WITH (ROWLOCK)
   SET NextNumber = NextNumber + 1
OUTPUT deleted.NextNumber AS Value
 WHERE EnterpriseId = @e AND Model = @m AND Series = @s AND Environment = @env;
```

Single-statement `UPDATE … OUTPUT deleted` is atomic under any isolation level, works across every API instance, and is a single round-trip. Row is created lazily by the settings upsert (`MERGE` on first save), never by the issuance path. `RepositoryBase` already exposes `protected readonly AppDbContext _context` (`RepositoryBase.cs:10`) and `ExecuteInTransactionAsync` (`:198`), so no new plumbing.

Gaps are **acceptable and expected** — a rejected note burns its number, and SEFAZ's remedy is `Inutilizacao`. A gap-free counter would require holding the number allocation inside the SEFAZ round-trip transaction, which is exactly the deadlock/timeout disaster we are avoiding. `// ponytail: gaps require manual inutilização; automate only if the ops load justifies it.`

### B.5 `NFeStatus` state machine

```csharp
public enum NFeStatus
{
    Pending   = 1,   // claimed; may or may not have been transmitted — disambiguate via TransmittedAtUtc
    Authorized= 2,   // cStat 100 (or 150 authorized-out-of-time)
    Cancelled = 3,   // evento 110111 homologated: cStat 135 (or 155)
    Error     = 4,   // OUR failure: no cert, bad settings, network, unhandled exception. Retryable.
    Rejected  = 5,   // NEW — SEFAZ said no (cStat 2xx/4xx/5xx/6xx/7xx). Number burned. Fix data, re-issue with a NEW number.
    Denied    = 6    // NEW — Denegada: cStat 110/301/302/303. Chave burned, CANNOT be cancelled, CANNOT be reused.
}
```

Only two new members, both appended (existing 1-4 values unchanged → no data migration, `NFeDocumentDTO.Status` wire values stay stable).

Deliberately **not** adding `CancelPending` / `TransmissionUnknown` as statuses. Two nullable timestamps carry that information with less surface:

```
Pending  ∧ TransmittedAtUtc IS NULL      → safe to (re)transmit. No SEFAZ side effect can exist.
Pending  ∧ TransmittedAtUtc IS NOT NULL  → UNKNOWN. MUST ConsultaProtocolo(chave) / RetAutorizacao(nRec)
                                            before any retry. This is the duplicate-emission trap (H).
Authorized ∧ CancelRequestedAt IS NOT NULL ∧ CancelledAtUtc IS NULL → cancel in flight, same rule.
```

Transitions:

```
(none) ──claim──▶ Pending(TransmittedAt=null)
Pending ──allocate nNF, build+sign, persist chave, COMMIT──▶ Pending(chave set, TransmittedAt=null)
Pending ──set TransmittedAt, COMMIT, then POST──▶ Pending(unknown)
Pending(unknown) ──cStat 100──▶ Authorized
Pending(unknown) ──cStat 110/301/302/303──▶ Denied            [terminal]
Pending(unknown) ──any other cStat──▶ Rejected                [re-issue takes a NEW number]
Pending(unknown) ──timeout/exception──▶ stays Pending(unknown); reconciler resolves
Error ──user retries──▶ Pending   (existing reset at NFeService.cs:282-295, must also clear the new columns)
Authorized ──110111 cStat 135/155──▶ Cancelled                [terminal]
Authorized ──110111 rejected──▶ Authorized (CancelCstat/CancelXMotivo recorded, CancelRequestedAt cleared)
Denied ──▶ nothing.  Cancelled ──▶ nothing.
```

`ResetErroredDocumentForRetry` (`NFeService.cs:282-295`) must be extended to clear `Cstat`, `XMotivo`, `ReceiptNumber`, `TransmittedAtUtc`, `QrCode`, `UrlChave`, `DigestValue` — and must **refuse** to reset anything that is not `Error` (today it is only reached from the `Status == Error` branch at `:84-90`, keep that invariant explicit with a guard).

---

## C. LAYERING

The repo's graph is inverted vs. classic DDD: `Domain → Infra`, `Application → Domain + Infra`. **Infra is the lowest layer.** Respect it; do not "fix" it in this change.

### C.1 Move the provider contract down

`INFeProvider` and `NFeProviderResult` currently live in `NFe.Application` (`Providers/INFeProvider.cs:5`, `Models/NFeProviderResult.cs:3`). The real implementation must live in Infra (it owns the Unimake reference), and Infra cannot reference Application. **Move both to `EvangelionERPV2.NFeModule.Infra.Providers`.** Four files change their `using`: `NFeService.cs:3`, `NFeIoC.cs:4`, `StubNFeProvider.cs:3-5`, `EvangelionERPV2.Test/Resilience/NFeServiceIdempotencyTests.cs`. Cheaper than an adapter type in Application that exists only to satisfy the graph.

`NFeSettings` (`Configs/NFeSettings.cs`) stays in Application — it becomes process-level bootstrap only (see G, phase 9).

### C.2 Placement

**`EvangelionERPV2.NFe.Infra`** (currently an empty csproj with an empty `Migrations/` folder — this is its purpose):
```
Providers/INFeProvider.cs                 (moved)
Providers/NFeProviderResult.cs            (moved + expanded)
Providers/NFeIssueContext.cs              (new)
Sefaz/SefazNFeProvider.cs                 INFeProvider impl — the ONLY file touching Unimake types
Sefaz/UnimakeConfiguracaoFactory.cs       EnterpriseFiscalSettings → Unimake Configuracao
Certificates/IEnterpriseCertificateProvider.cs
Certificates/EnterpriseCertificateProvider.cs
Xml/NFeXmlBuilder.cs                      Order+items+settings → Unimake.Business.DFe.Xml.NFe.NFe
Xml/NFeItemFiscalResolver.cs              item-vs-enterprise fallback (B.2)
Xml/NFeAccessKeyBuilder.cs                44-digit chave + DV mod-11 (own impl, unit-testable, no SEFAZ)
Danfe/IDanfeRenderer.cs
Danfe/DanfeRenderer.cs                    XML → HTML → PuppeteerSharp PDF
```

**`EvangelionERPV2.NFe.Domain`**:
```
Interface/INFeRepository.cs               + GetByEnterprisePagedAsync, AllocateNextNumberAsync,
                                            GetStalePendingAsync (reconciler)
Repositories/NFeRepository.cs             impls, incl. the raw-SQL sequence allocation
Interface/INFeNumberSequenceRepository.cs -- NOT created. Fold into INFeRepository; one extra
                                            interface for one method is a file nobody reads.
```

**`EvangelionERPV2.NFe.Application`**:
```
Services/NFeService.cs                    orchestration (claim → allocate → build → transmit → finalize)
Services/NFeReconciliationService.cs      resolves Pending(unknown) (H)
Services/NFeFiscalSettingsService.cs      GetSettingsAsync / UpsertSettingsAsync — copy of
                                            PayrollService.cs:36-83, plus MERGE of the sequence row
Services/NFeCertificateService.cs         upload/rotate/status; delegates to IEnterpriseCertificateProvider
Interface/INFeService.cs                  expanded
Interface/INFeFiscalSettingsService.cs
Interface/INFeCertificateService.cs
DI/NFeIoC.cs                              registrations
```

**PDF renderer duplication.** `RenderPdfAsync` is `private static` inside `BillsService` (`EvangelionERPV2.Bills.Application/Services/BillsService.cs:162-198`), together with `ShouldBlockBrowserRequest` (`:200-…`) and a shared browser singleton. NFe.Infra cannot reach a private method in Bills.Application, and adding a Bills→NFe project reference is wrong. **Lift `RenderPdfAsync` + `ShouldBlockBrowserRequest` + `GetBrowserAsync` verbatim into `EvangelionERPV2.Shared/Utils/HeadlessPdfRenderer.cs` (public static), and have `BillsService` call it.** One browser process for the whole app instead of two, same hardening (JS disabled `:166`, request interception blocking every outbound URL `:167-176`, A4 `:184`). This is a pure move — the security posture must be byte-identical, and `EvangelionERPV2.BillsModule.Test` must stay green. Requires `PuppeteerSharp 25.1.2` to move to `EvangelionERPV2.Shared.csproj`.

### C.3 Signatures

```csharp
// NFe.Infra/Providers/INFeProvider.cs
public interface INFeProvider
{
    Task<NFeProviderResult> IssueAsync(NFeIssueContext context, CancellationToken ct = default);
    Task<NFeProviderResult> CancelAsync(NFeCancelContext context, CancellationToken ct = default);
    Task<NFeProviderResult> ConsultProtocolAsync(string accessKey, NFeSefazContext context, CancellationToken ct = default);
    Task<NFeProviderResult> ConsultReceiptAsync(string receiptNumber, NFeSefazContext context, CancellationToken ct = default);
    Task<NFeServiceStatusResult> ConsultServiceStatusAsync(NFeSefazContext context, CancellationToken ct = default);
    Task<NFeProviderResult> InutilizarAsync(NFeInutilizacaoContext context, CancellationToken ct = default);
}

public sealed class NFeSefazContext
{
    public required EnterpriseFiscalSettings Settings { get; init; }
    public required NFeDocumentType Type { get; init; }   // → Model 55/65, → Unimake TipoDFe
}

public sealed class NFeIssueContext : NFeSefazContext
{
    public required Guid DocumentId { get; init; }
    public required Order Order { get; init; }
    public required Enterprise Enterprise { get; init; }
    public Customer? Customer { get; init; }
    public required IReadOnlyList<NFeIssueItem> Items { get; init; }   // OrderedProduct + Product pairs
    public required int Number { get; init; }        // nNF — pre-allocated (B.4)
    public required string Series { get; init; }     // serie
    public required DateTime IssuedAtUtc { get; init; }
}

public sealed class NFeCancelContext : NFeSefazContext
{
    public required string AccessKey { get; init; }
    public required string Protocol { get; init; }   // nProt of the authorization — required by 110111
    public required string Reason { get; init; }     // xJust, 15..255 chars
    public required int SequenceNumber { get; init; }// nSeqEvento, 1 for the first attempt
}
```

`NFeProviderResult` grows an explicit error channel (replaces "throw and hope"):

```csharp
public sealed class NFeProviderResult
{
    // existing
    public string AccessKey { get; set; } = "";
    public string Protocol  { get; set; } = "";
    public string XmlContent{ get; set; } = "";   // nfeProc when authorized
    public string Number    { get; set; } = "";
    public string Series    { get; set; } = "";
    public string Environment { get; set; } = "";
    public DateTime? IssuedAt { get; set; }
    public decimal TotalValue { get; set; }
    public NFeStatus Status { get; set; } = NFeStatus.Pending;

    // new — error / protocol channel
    public string  Cstat   { get; set; } = "";     // "100", "204", "539", "110"
    public string  XMotivo { get; set; } = "";
    public string  ReceiptNumber { get; set; } = "";   // nRec
    public DateTime? AuthorizedAtUtc { get; set; }
    public string  QrCode  { get; set; } = "";     // NFCe
    public string  UrlChave{ get; set; } = "";     // NFCe
    public string  DigestValue { get; set; } = "";
    public string  CancelProtocol { get; set; } = "";
    public DateTime? CancelledAtUtc { get; set; }

    /// True when the failure is transport-level or a SEFAZ 'try again' (cStat 108/109/999
    /// contingency-ish). The document stays Pending and the reconciler owns it.
    public bool IsTransient { get; set; }

    /// True when the transmission definitely never reached SEFAZ (DNS/TLS/connect refused).
    /// Only this flag permits a same-number retransmission.
    public bool NeverTransmitted { get; set; }
}
```

Certificate provider:

```csharp
// NFe.Infra/Certificates/IEnterpriseCertificateProvider.cs
public interface IEnterpriseCertificateProvider
{
    /// Cached ~15 min. Throws NFeCertificateExpiredException when NotAfter <= now,
    /// and logs a warning when NotAfter is within 30 days.
    Task<X509Certificate2> GetAsync(Guid enterpriseId, CancellationToken ct = default);

    /// Parses + validates a freshly uploaded pfx WITHOUT storing it. Returns metadata only.
    NFeCertificateMetadata Inspect(byte[] pfxBytes, string password);

    /// Writes/overwrites evangelion/{env}/nfe/cert/{enterpriseId}, invalidates caches,
    /// returns the metadata to persist on EnterpriseFiscalSettings.
    Task<NFeCertificateMetadata> StoreAsync(Guid enterpriseId, byte[] pfxBytes, string password, CancellationToken ct = default);

    void Invalidate(Guid enterpriseId);
}

public sealed record NFeCertificateMetadata(
    string Thumbprint, string SubjectCn, DateTime NotAfterUtc, DateTime UploadedAtUtc);
```

**Caching gotcha, must be handled.** `AWSKMSKeyProvider` caches every resolved secret in a **`static ConcurrentDictionary` for the process lifetime** (`EvangelionERPV2.Shared/Utils/AWSKMSKeyProvider.cs:17`, populated at `:73`) with **no invalidation API**. Certificate rotation would therefore require a full restart of every container. Two ways out:

- **(a) recommended, honours "reuse AWSKMSKeyProvider":** add one overload to the shared util —
  `public Task<string> GetKMSKeyAsync(string secretName, bool bypassCache, CancellationToken ct = default)`; when `bypassCache` is true, skip the `TryGetValue` at `:58` and *overwrite* the cache entry at `:73`. ~4 lines, no behaviour change for existing callers (`Program.cs`, `NFeIoC.cs:40`, `EmailService`).
- (b) inject `IAmazonSecretsManager` directly in `EnterpriseCertificateProvider`. Bypasses the shared util entirely — contradicts decision 2.

Take **(a)**. `EnterpriseCertificateProvider` calls `GetKMSKeyAsync($"evangelion/{env}/nfe/cert/{id}:pfxBase64")` and `…:password`; the `secretId:key` suffix form is supported by `SplitSecretReference` (`AWSKMSKeyProvider.cs:78-88`, `LastIndexOf(':')`) and our secret names contain no other colon. The provider keeps its own `IMemoryCache` of the *materialised* `X509Certificate2` (15 min sliding), and `Invalidate` clears both its cache and the underlying entry via the bypass path.

---

## D. ENDPOINTS

Route template is `api/v{version:apiVersion}/[controller]/[action]` (`NFeController.cs:19`), so paths below are literal. All controllers `[Authorize]` + per-action `[Authorize(Policy = "rbac:" + …)]`, tenant from `ClaimTypes.GroupSid` via `TryGetEnterpriseId` (`NFeController.cs:180-184`) — copy that helper, do not invent a new tenant source.

### D.1 New controller `NFeFiscalSettingsController` (admin-only)

| Verb | Route | Policy | Request | Response | Codes |
|---|---|---|---|---|---|
| GET | `/api/v1/NFeFiscalSettings/GetFiscalSettings` | `rbac:nfe.settings.read` | — | `EnterpriseFiscalSettingsDTO` | 200, 401, 403, 500 |
| PUT | `/api/v1/NFeFiscalSettings/UpdateFiscalSettings` | `rbac:nfe.settings.update` | `EnterpriseFiscalSettingsDTO` (`[RequestSizeLimit(64*1024)]`) | `EnterpriseFiscalSettingsDTO` | 200, 400, 401, 403, 500 |
| PUT | `/api/v1/NFeFiscalSettings/UpdateCsc` | `rbac:nfe.settings.update` | `NFeCscRequestDTO { int CscId; string Csc; }` | 204 | 204, 400, 401, 403, 500 |
| POST | `/api/v1/NFeFiscalSettings/UploadCertificate` | `rbac:nfe.certificate.manage` | `multipart/form-data` — `IFormFile file` (.pfx, ≤ 512 KB) + `string password` | `NFeCertificateStatusDTO` | 200, 400, 401, 403, 413, 500 |
| GET | `/api/v1/NFeFiscalSettings/GetCertificateStatus` | `rbac:nfe.settings.read` | — | `NFeCertificateStatusDTO { Thumbprint, SubjectCn, NotAfterUtc, UploadedAtUtc, DaysUntilExpiry, IsExpired }` | 200, 204, 401, 403, 500 |
| DELETE | `/api/v1/NFeFiscalSettings/DeleteCertificate` | `rbac:nfe.certificate.manage` | — | 204 | 204, 401, 403, 500 |
| GET | `/api/v1/NFeFiscalSettings/GetServiceStatus` | `rbac:nfe.settings.read` | `?type=NFe\|NFCe` | `NFeServiceStatusDTO { Cstat, XMotivo, Environment, Uf, CheckedAtUtc }` | 200, 401, 403, 502, 500 |

`EnterpriseFiscalSettingsDTO` is **write-safe**: it never carries the CSC token, the cert password, the pfx, or any SEFAZ URL. `UpdateFiscalSettings` returns the saved row; `UpdateCsc` returns 204 and never echoes.
`UploadCertificate` uses `multipart/form-data` — `SetupRequestLimits` (`Program.cs:307-323`) caps Kestrel at `RequestLimits:MaxRequestBodySizeBytes` (default 256 KB), so a 512 KB `[RequestSizeLimit]` on the action alone is not enough; bump `RequestLimits:MaxRequestBodySizeBytes` to 1 MB in appsettings **or** keep the pfx under 256 KB (real A1 pfx files are 3-8 KB — 256 KB is already 30× headroom, so **do nothing** and cap the action at 256 KB). `// ponytail: no config change; A1 pfx is ~5 KB.`

### D.2 `NFeController` (existing file, extended)

| Verb | Route | Policy | Request | Response | Codes |
|---|---|---|---|---|---|
| GET | `/api/v1/NFe/GetByOrder/{orderId}?type=` | `rbac:nfe.read` (unchanged) | — | `NFeDocumentDTO` | 200, 204, 401, 403, 500 |
| GET | `/api/v1/NFe/GetDocuments?pageNumber=&pageSize=&status=&type=&from=&to=` | `rbac:nfe.read` **(new action)** | — | `PagedResultDTO<NFeDocumentDTO>` | 200, 204, 400, 401, 403, 500 |
| POST | `/api/v1/NFe/Issue/{orderId}?type=` | `rbac:nfe.issue` (unchanged) | — | `NFeDocumentDTO` | 200, 202 (in flight), 204, 400, 401, 403, 409 (already authorized), 422 (rejected — body carries cStat/xMotivo), 502, 500 |
| GET | `/api/v1/NFe/Consult/{accessKey}` | `rbac:nfe.read` (unchanged) | — | `NFeDocumentDTO` | 200, 204, 400, 401, 403, 502, 500 |
| POST | `/api/v1/NFe/Cancel/{accessKey}` | `rbac:nfe.cancel` (unchanged) | `NFeCancelRequestDTO` (16 KB, `NFeController.cs:23,139`) | `NFeDocumentDTO` | 200, 204, 400, 401, 403, 409, 422, 502, 500 |
| GET | `/api/v1/NFe/Danfe/{accessKey}` | `rbac:nfe.pdf.download` **(new)** | — | `application/pdf` (`File(bytes,"application/pdf",$"danfe-{accessKey}.pdf")`, mirrors `BillsController.cs:121`) | 200, 204, 400, 401, 403, 500 |
| GET | `/api/v1/NFe/Xml/{accessKey}` | `rbac:nfe.xml.download` **(new)** | — | `application/xml` | 200, 204, 400, 401, 403, 500 |
| POST | `/api/v1/NFe/EmailToCustomer/{accessKey}` | `rbac:nfe.email` **(new)** | `NFeEmailRequestDTO { string[]? RecipientEmails; }` (null ⇒ `Customer.Email`) — 16 KB | `202 Accepted` | 202, 204, 400, 401, 403, 422 (no recipient / not authorized), 500 |

**Behavioural change on `Consult`:** today it is a pure local DB read (`NFeService.cs:208-221`). It becomes: load local row (tenant-scoped) → if status is non-terminal *or* `?refresh=true`, call `ConsultaProtocolo` and reconcile → return. Terminal rows (`Cancelled`, `Denied`) short-circuit to avoid pointless SEFAZ traffic.

`Cancel` returns **422** with `{ cstat, xMotivo }` when SEFAZ rejects the evento — this is the single biggest UX gap in the current fake (`NFeService.cs:238-240` fabricates `CANCEL-yyyyMMddHHmmss` and always "succeeds").

Access keys **must not** appear in logs: `RequestLoggingMiddleware.SanitizeEndpointPath` (`EvangelionERPV2.Web/Logging/RequestLoggingMiddleware.cs:203-211`) and the identical `ExceptionHandlingMiddleware.SanitizeEndpointPath` (`EvangelionERPV2.Web/Logging/ExceptionHandlingMiddleware.cs:145-153`) currently redact only `/NFe/Consult/` and `/NFe/Cancel/`. **Add `/NFe/Danfe/`, `/NFe/Xml/`, `/NFe/EmailToCustomer/` to both lists.** Missing this leaks the chave (which embeds CNPJ + nNF) into Serilog.

### D.3 New RBAC permissions

Add to **four** places or startup throws / tests fail:
- `RbacPermissions.All` (`EvangelionERPV2.Web/Security/RbacPermissions.cs:5-83`) — `RbacPolicies.AddRbacPolicies` iterates it (`RbacPolicies.cs:98`) and `ForPermission` throws `ArgumentOutOfRangeException` for anything missing (`:88-94`).
- The nested `RbacPermissions.NFe` class (`:148-153`).
- `RbacPolicies.All` (`RbacPolicies.cs:10-83`) and the nested `RbacPolicies.NFe` class (`:192-197`).
- `RbacRolePermissionMap` — **only** for the non-admin tiers.

| Constant | Value | Tier | Rationale |
|---|---|---|---|
| `RbacPermissions.NFe.SettingsRead` | `nfe.settings.read` | **Admin-only by omission** | Exposes CNPJ/IE and cert expiry. Precedent: `payroll.*`, `RbacPermissions.cs:181-189`. |
| `RbacPermissions.NFe.SettingsUpdate` | `nfe.settings.update` | **Admin-only by omission** | A wrong CFOP/CST silently mis-taxes every future note. |
| `RbacPermissions.NFe.CertificateManage` | `nfe.certificate.manage` | **Admin-only by omission** | Holder can replace the company's signing identity. |
| `RbacPermissions.NFe.DownloadPdf` | `nfe.pdf.download` | **Supervisor** (add to `SupervisorPermissions`, `RbacRolePermissionMap.cs:18-54`, next to the existing NFe block at `:38-40`) | Mirrors `bills.pdf.download` at `:28`. |
| `RbacPermissions.NFe.DownloadXml` | `nfe.xml.download` | **Manager** (add to `ManagerPermissions`, `:56-85`) | The XML carries the customer's full CPF/CNPJ **and** address in clear. Same reasoning that puts `Employees.*` at Manager (`:67-72`). |
| `RbacPermissions.NFe.Email` | `nfe.email` | **Supervisor** | Cannot reuse `RbacPermissions.Email.Send` — that one is in `All` but absent from every role array, i.e. **admin-only**, which would break the "users can email the note" requirement. |

`RbacPermissions.Enterprise.Manage` (`:145`) stays unused — a fiscal-specific permission set is clearer than overloading a generic one, and reusing it would grant fiscal-settings write to anyone later given generic enterprise management.

Every new action needs an `[InlineData]` row in `EvangelionERPV2.Test/Security/RbacControllerPolicyCoverageTests.cs` (NFe block currently at `:44-47`).

---

## E. EMAIL WITH ATTACHMENTS

The protocol already supports it: `SendManualEmail` serialises the **entire `MimeMessage`** with `message.WriteTo(stream)` (`EvangelionERPV2.Email.Application/Services/EmailService.cs:645-647`), HMAC-signs it (`CreateQueuedEmailPayload` `:338`, `CreateQueuedEmailSignature` `:409`) and enqueues. The worker (`EvangelionERPV2.Worker.Email/EmailWorker/EmailConsumerWorker.cs`) POSTs it verbatim to `Email/SendQueuedEmail`. **No queue-format change, no worker change, no signature change.** Attachments ride along inside the MIME.

Three edits:

**1. `EvangelionERPV2.Shared/Entities/EmailStructure.cs`** — add one property + one record:
```csharp
public IReadOnlyList<EmailAttachment> Attachments { get; set; } = [];

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);
```

**2. `EmailService.CreateEmail`** (`EmailService.cs:78-110`) — replace the direct `message.Body = new TextPart(...)` at `:97-101` with:
```csharp
var bodyBuilder = new BodyBuilder { HtmlBody = email.Body };
foreach (var attachment in email.Attachments ?? [])
    bodyBuilder.Attachments.Add(attachment.FileName, attachment.Content,
                                ContentType.Parse(attachment.ContentType));
message.Body = bodyBuilder.ToMessageBody();
```
`BodyBuilder.ToMessageBody()` with zero attachments produces a plain `text/html` part — **byte-identical to today for every existing caller** (`SendMonthEmail`, `SendStockEmail`, manual email). Behaviour-preserving.

**3. `NFeService.EmailDocumentAsync(accessKey, enterpriseId, recipients)`** builds the structure server-side and calls `_emailService.SendManualEmail(structure, new Enterprise { Id = enterpriseId })` — the exact shape `EmailController.cs:90-95` already uses. No new email endpoint, no client-supplied body.

**Size limits.**
- `EmailController.SendManualEmail` is capped at **64 KB** (`EmailController.cs:22,53`) — **irrelevant here**, because the NFe path never crosses that controller. It calls `IEmailService` in-process from `NFeController`.
- The binding constraint is `MaxQueuedEmailRequestBodySizeInBytes = 4 MB` (`EmailController.cs:26`), enforced on the worker's POST of the already-serialised payload.
- Inflation chain: raw bytes → **base64 in MIME (+33%)** → embedded as a JSON string inside `QueuedEmailPayload.RawMimeMessage` with escaping (+small) → the whole thing is HMAC'd over the *escaped* string (`:409` hashes `$"{enterpriseId:N}\n{rawMimeMessage}"`). Effective budget ≈ **2.8 MB of raw attachment bytes**.
- Real sizes: NFe XML (nfeProc) 8–40 KB, DANFE A4 PDF from PuppeteerSharp 40–200 KB. Two orders of magnitude of headroom.
- **Still add a hard guard** in `NFeService.EmailDocumentAsync`: total attachment bytes > 2 MB ⇒ throw/return 422 rather than enqueue a message that the worker will 413 on and silently drop. Failing loud at enqueue time beats a dead letter nobody reads.

`ShouldSendEmail(email, enterprise)` (`EmailService.cs:623`) gates the send — confirm it does not reject a structure whose `Body` is short; if it inspects `Enterprise.ShouldSendMonthlyBilling` (`Enterprise.cs:27`) it must not apply to transactional NFe mail. Read it before wiring, and add a `bypassPreferences` path only if needed.

Log hygiene: the (currently dead) `messageSummary` anonymous object at `EmailService.cs:631-639` reads `Subject/From/To/TextBody/HtmlBody` — it does **not** touch attachments. Leave it; do not extend it to log attachment names (the filename will contain the chave).

---

## F. SECURITY & LGPD

**This section is not optional. Treat each bullet as an acceptance criterion.**

1. **Private key never leaves Secrets Manager.** The .pfx bytes and its password exist in three places only: the multipart request body (in memory, for the duration of `UploadCertificate`), the `PutSecretValue` call, and the decrypted `X509Certificate2` in the provider's `IMemoryCache`. **No DB column, no disk write, no `CertificadoArquivo`, no log statement, no API response field ever carries them.** `EnterpriseFiscalSettings` stores thumbprint / subject CN / NotAfter / uploadedAt and nothing else. Add an assertion test: reflect over `EnterpriseFiscalSettingsDTO` and fail if any property name matches `/pfx|password|senha|privatekey|csc$/i`.
2. **CSC token** → `evangelion/{env}/nfe/csc/{enterpriseId}` as `{"cscId": 1, "csc": "..."}`. `CscId` is duplicated as a plain DB column for display; the token is write-only over the API (`UpdateCsc` → 204). It is a SEFAZ-issued shared secret that lets anyone forge a valid NFCe QR for the tenant — treat it exactly like the cert password.
3. **`XmlContent` is personal data.** It contains the destinatário's CPF/CNPJ, full name and full address. It **must** keep `[JsonIgnore]` (`NFeDocumentDTO.cs:18-19`) and must never be logged. `AuditTrailEntryFactory` already redacts `AccessKey`, `XmlContent` and `Document` (`EvangelionERPV2.Audit.Application/Services/AuditTrailEntryFactory.cs:17,18,25`) — verify the new columns `QrCode` (contains the chave) and `CancelXMotivo` are covered too; add `QrCode` and `UrlChave` to that redaction list.
4. **Audit.** `AuditedEntities` is an opt-in allowlist (`EvangelionERPV2.Shared/Auditing/AuditedEntities.cs:7-15`) and today contains neither `NFeDocument` nor `Enterprise`. **Add `typeof(NFeDocument)` and `typeof(EnterpriseFiscalSettings)`.** Fiscal-settings edits are admin actions with tax consequences; every issuance/cancellation must have a who-did-what trail. Do **not** add `Enterprise` itself in this change (out of scope, changes audit volume for unrelated flows).
5. **Tenant scoping — denormalise `EnterpriseId` onto `NFeDocument`.** Today every scope check re-reads the Order: `IsOrderWithinEnterpriseAsync` (`NFeService.cs:264-271`), `IsDocumentWithinEnterpriseAsync` (`:273-280`), `GetScopedOrderAsync` (`:249-262`). That is three query paths that must each be right, it makes a tenant-scoped *list* endpoint (D.2 `GetDocuments`) impossible without a join, and it makes the number sequence lookup awkward. Denormalise, backfill from `Order.EnterpriseId` in the migration, index it, and **keep** the Order-based check as a defence-in-depth assertion on the issue path only. Answer to the question in the brief: **yes, denormalise.**
6. **Rate limiting.** Add to `IpRateLimiting:GeneralRules` (`EvangelionERPV2.Web/appsettings.Production.json:130-…`, pattern from the login rules at `:136-155`): `*:/api/v1/NFe/Issue` 10/1m, `*:/api/v1/NFe/Cancel` 5/1m, `*:/api/v1/NFe/EmailToCustomer` 10/1m, `*:/api/v1/NFeFiscalSettings/UploadCertificate` 3/1m. IP-based limiting is not tenant-based; also enforce a per-enterprise ceiling inside `NFeService` for issuance. `// ponytail: in-memory counter; move to Redis if multi-pod drift matters.`
7. **SSRF / endpoint allowlist.** Unimake resolves SEFAZ URLs itself from `CodigoUF` + `TipoAmbiente`. The attack surface would be `Configuracao.WebEnderecoProducao`/`WebEnderecoHomologacao`. **Never expose those to tenant input** — no DB column, no DTO field, no config binding. Independently, add an egress allowlist at the network layer (security group / egress policy) for `*.fazenda.sp.gov.br`, `*.sefazrs.rs.gov.br`, `*.svrs.rs.gov.br`, `*.fazenda.pr.gov.br`, `*.sef.sc.gov.br`, `*.nfe.fazenda.gov.br`, plus AWS Secrets Manager. Also assert `Uf ∈ {SP,RS,SC,PR}` on settings save (v1 scope).
8. **`plain:` dev fallback.** `AWSKMSKeyProvider` accepts a `plain:` prefix that returns the literal string (`AWSKMSKeyProvider.cs:51-56`), guarded by `EnsurePlainSecretUsageIsAllowed` → `IsDevelopmentEnvironment()` (`:275-291`), which reads `ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT`. That guard is adequate for the connection string. **It is not adequate for a certificate private key.** In `EnterpriseCertificateProvider`, reject any resolved secret name starting with `plain:` **unconditionally** — a base64 PFX pasted into appsettings is a leaked signing identity, and an env-var misconfiguration must not be the only thing standing between us and that. One `if` at the top of `GetAsync`.
9. **Retention vs. erasure.** SEFAZ/CONFAZ require the emitente to preserve the authorized XML and its protocol for **5 years** (Ajuste SINIEF 07/2005; decadência tributária, CTN art. 173). LGPD art. 16, II expressly permits retention for "cumprimento de obrigação legal ou regulatória pelo controlador", and art. 7, II is the lawful basis for the processing itself. **Consequence: an LGPD erasure request from a customer does NOT authorise deleting an authorized NFe XML.** Concretely: (a) `NFeDocument` rows in status `Authorized`/`Cancelled`/`Denied` are exempt from any anonymisation/erasure routine; (b) any future audit-retention cleanup job (`RbacPermissions.Audit.CleanupRetention`) must exclude `NFeDocument` and its audit trails; (c) `Pending`/`Error`/`Rejected` rows with no protocol have no legal-obligation basis and **should** be purgeable after a short window. Document the basis in the privacy notice — do not leave it as tribal knowledge.
10. **Who may download the XML.** `nfe.xml.download` at **Manager** tier (D.3), never below. The DANFE PDF (`nfe.pdf.download`, Supervisor) shows a masked/partial document per DANFE layout and is the correct artefact for day-to-day staff. Emailing to the customer sends both, to the customer's own address — that is the data subject receiving their own data, which is fine; sending to an arbitrary operator-supplied address is an exfiltration channel, so `NFeEmailRequestDTO.RecipientEmails`, when supplied, must be validated against `Customer.Email` **or** require an extra confirmation flag, and every send is audited with the recipient hash (`GetLogSafeEmailIdentifier`, `EmailService.cs:323-331`) rather than the address.
11. **Homologação watermark.** When `Environment == 2`, `xNome` of the destinatário must be the mandated `NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL` and the DANFE must render the diagonal watermark. Not cosmetic — omitting it in homologação is a rejection, and rendering a homologação DANFE without the watermark is a compliance problem.

---

## G. IMPLEMENTATION STEPS

Every phase that touches an entity ends with the same three-step ritual:
1. Hand-write `EvangelionERPV2.Shared/Migrations/<yyyyMMddHHmmss>_<Name>.cs` — **`[DbContext(typeof(AppDbContext))]` + `[Migration("<full_timestamp_and_name>")]`, exactly as in `20260713120000_AddEnterprisePayrollSettings.cs:12-13`. Without both, `Database.Migrate()` silently skips the file and the model-snapshot test still passes green** (`EvangelionERPV2.Test/Security/AppDbContextModelSnapshotSyncTests.cs:13-17` says so explicitly). No `.Designer.cs` — nothing since `20260403120000` has one.
2. Hand-edit `EvangelionERPV2.Shared/Migrations/AppDbContextModelSnapshot.cs`.
3. Run `AppDbContextModelSnapshotSyncTests.ModelSnapshot_MatchesRuntimeModel` — it must be green.

---

**Phase 0 — Feasibility gate (no product code, not shipped).**
Throwaway console referencing `Unimake.DFe` from a `net10.0` project, run inside `mcr.microsoft.com/dotnet/sdk:10.0`. Prove: (a) package restores and the assembly loads on Linux; (b) `X509CertificateLoader.LoadPkcs12(bytes, pwd, EphemeralKeySet)` yields a usable key; (c) `StatusServico` returns cStat 107 in homologação for `CodigoUF` 35, 43, 42, 41 with `TipoDFe.NFe` **and** `TipoDFe.NFCe`; (d) a signed `enviNFe` passes the library's own schema validation.
**Exit criterion:** all four green, or the spec is amended to `zeus.nfe`. **Nothing downstream starts before this closes.**

**Phase 1 — Fiscal settings (schema + admin CRUD). No SEFAZ.**
- New `EvangelionERPV2.Shared/Entities/EnterpriseFiscalSettings.cs`, `Entities/NFeNumberSequence.cs`.
- New `EvangelionERPV2.Shared/DTOs/EnterpriseFiscalSettingsDTO.cs`, `DTOs/NFeCscRequestDTO.cs`.
- `AppDbContext.cs` — two `DbSet`s next to `:26`; fluent filtered unique index on `EnterpriseFiscalSettings.EnterpriseId` copying `:116-119`; hard unique index on the sequence tuple; `HasMaxLength` on the string columns.
- `MapperConfig.cs:22` — one `CreateMap<EnterpriseFiscalSettings, EnterpriseFiscalSettingsDTO>().ReverseMap()`.
- `NFe.Application/Services/NFeFiscalSettingsService.cs` + interface — structurally a copy of `PayrollService.GetSettingsAsync/UpsertSettingsAsync` (`EvangelionERPV2.Bills.Application/Services/PayrollService.cs:36-83`), plus CNPJ/IE/CEP/CFOP/UF validation and the lazy `MERGE` of the two sequence rows.
- `Web/Controllers/NFeFiscalSettingsController.cs` (settings actions only).
- RBAC: `nfe.settings.read` / `nfe.settings.update` in all four RBAC locations (D.3).
- Migration `20260801120000_AddEnterpriseFiscalSettings` + snapshot.
- Tests: `NFeFiscalSettingsServiceTests` (Moq, no DB), `NFeFiscalSettingsControllerAuthorizationTests` (copy `Test/Security/NFeControllerIssueAuthorizationTests.cs`), two new `[InlineData]` rows in `RbacControllerPolicyCoverageTests.cs`.
**Shippable:** admins can configure fiscal data. Nothing else changes.

**Phase 2 — Certificate lifecycle.**
- `NFe.Infra/Certificates/{IEnterpriseCertificateProvider,EnterpriseCertificateProvider}.cs`, `NFeCertificateMetadata`.
- `AWSKMSKeyProvider.GetKMSKeyAsync(name, bypassCache, ct)` overload (~4 lines, C.3).
- `NFe.Application/Services/NFeCertificateService.cs`, `UploadCertificate` / `GetCertificateStatus` / `DeleteCertificate` actions, `nfe.certificate.manage`.
- Unit tests with a self-signed pfx generated in-test: `Inspect_ValidPfx_ReturnsThumbprintAndNotAfter`, `GetAsync_ExpiredCertificate_ThrowsNFeCertificateExpiredException`, `GetAsync_PlainPrefixSecretName_Throws`.
- No migration (metadata columns landed in Phase 1).
**Shippable:** admins upload/rotate a cert and see its expiry.

**Phase 3 — NFeDocument schema, sequencing, tenant denormalisation.**
- `NFeDocument.cs` new columns (B.3) + `Rejected`/`Denied` enum members; `[Index(nameof(EnterpriseId))]`.
- `NFeDocumentDTO.cs` mirrors the safe subset (`Cstat`, `XMotivo`, `AuthorizedAtUtc`, `CancelCstat`, `CancelXMotivo`, `Model`; **not** `QrCode`, **not** `DigestValue`).
- `INFeRepository`/`NFeRepository`: `AllocateNextNumberAsync` (raw SQL, B.4), `GetByAccessKeyAsync(accessKey, enterpriseId)` overload, `GetByEnterprisePagedAsync`, `GetStalePendingAsync`.
- `NFeService` scope helpers switch to `document.EnterpriseId` (deleting the extra Order round-trips at `:264-280`).
- `AuditedEntities.cs` += `NFeDocument`, `EnterpriseFiscalSettings`; `AuditTrailEntryFactory` redaction += `QrCode`, `UrlChave`.
- Migration `20260801130000_AddNFeDocumentFiscalColumns` — add columns, **backfill `EnterpriseId` from `Order`**, then `alterColumn` to NOT NULL, create index, create `NFeNumberSequence` FKs. + snapshot.
- Tests: `AllocateNextNumber_ConcurrentCallers_ReturnsDistinctMonotonicValues` (integration, real SQL — the only DB-touching test in the plan; if that is unacceptable in this suite, assert the emitted SQL text instead), `NFeServiceTenantScopeTests` updated.
**Shippable:** schema and numbering in place, still issuing via the stub.

**Phase 4 — Real NFe modelo 55, homologação, issue only.**
- `PackageReference Unimake.DFe 20260722.1716.27` → `EvangelionERPV2.NFe.Infra` csproj only.
- Move `INFeProvider` + `NFeProviderResult` to `NFe.Infra.Providers`; fix the 4 `using`s (C.1).
- `NFe.Infra/Xml/{NFeAccessKeyBuilder,NFeItemFiscalResolver,NFeXmlBuilder}.cs`; `Sefaz/{UnimakeConfiguracaoFactory,SefazNFeProvider}.cs` (`IssueAsync` + `ConsultProtocolAsync` + `ConsultReceiptAsync` + `ConsultServiceStatusAsync`).
- `NFeService.IssueAsync` rewritten around the **transmit-safe order** (H): claim (keep the existing unique-index dance at `NFeService.cs:114-134`) → load settings/cert → allocate `nNF` → build+sign → **persist chave+number, COMMIT** → set `TransmittedAtUtc`, **COMMIT** → transmit → finalize.
- `NFe.Application/Services/NFeReconciliationService.cs` + a hosted background service (or a `Worker.Order`-style consumer) scanning `Pending ∧ TransmittedAtUtc < now-60s`.
- `Product` fiscal columns + `ProductDTO` + migration `20260801140000_AddProductFiscalColumns` + snapshot.
- `NFeIoC.cs`: register `IEnterpriseCertificateProvider`, `INFeProvider → SefazNFeProvider` (replacing `services.AddSingleton<INFeProvider, StubNFeProvider>()` at `NFeIoC.cs:59` — **`AddScoped`, not `AddSingleton`**, it now touches per-request state), `INFeFiscalSettingsService`, `INFeCertificateService`, `NFeReconciliationService`. Keep the swallowing try/catch shape (`:23,68-71`) for consistency, but **log the fiscal registrations at Information** — a swallowed DI failure here means silent non-issuance.
- Tests: `NFeAccessKeyBuilderTests` (known chave + DV vectors), `NFeItemFiscalResolverTests` (item overrides enterprise; blank falls back), `NFeXmlBuilderTests` (golden-file snapshot of `infNFe` for a fixed order), `NFeServiceIdempotencyTests` extended for the transmit-safe ordering, `NFeService_TransmitTimeout_LeavesPendingAndDoesNotReallocateNumber`.
**Shippable behind `EnterpriseFiscalSettings.Enabled=false` for all tenants; flip one pilot tenant to homologação.**

**Phase 5 — Cancel (evento 110111) + real Consult + service status.**
- `SefazNFeProvider.CancelAsync` (`RecepcaoEvento` + `detEvento` 110111, `nSeqEvento`, `xJust` 15..255).
- `NFeService.CancelAsync` replaces the fabricated protocol at `NFeService.cs:238-240`; enforces the 24h window for modelo 55 (SEFAZ rejects beyond it — surface as 422, not 500); rejects cancel on `Denied`.
- `NFeService.ConsultAsync` gains the SEFAZ refresh path.
- `GetServiceStatus` action.
- `NFeCancelRequestDTO` gains min-length validation (15 chars — `NFeController.cs:151` currently only checks non-empty and ≤500; SEFAZ minimum is 15 and maximum 255).
- Tests: `CancelAsync_SefazRejects_KeepsAuthorizedAndRecordsCstat`, `CancelAsync_DeniedDocument_Returns422`, `CancelAsync_Beyond24h_Returns422`.

**Phase 6 — NFCe modelo 65.**
- `Configuracao.CSC` / `CSCIDToken` wiring from Secrets Manager; `infNFeSupl` QR + `urlChave` persisted; `tpImp=4`, `indPres`, `modFrete` NFCe-specific rules; CPF-optional destinatário.
- `UpdateCsc` action.
- Tests: `NFCe_MissingCsc_ReturnsConfigurationError`, `NFCe_Authorized_PersistsQrCodeAndUrlChave`.

**Phase 7 — DANFE / DANFE-NFCe PDF + XML download.**
- Move `RenderPdfAsync`/`ShouldBlockBrowserRequest`/`GetBrowserAsync` from `BillsService.cs:162-198+` to `EvangelionERPV2.Shared/Utils/HeadlessPdfRenderer.cs`; `BillsService` delegates; `PuppeteerSharp 25.1.2` moves to `EvangelionERPV2.Shared.csproj`. **`EvangelionERPV2.BillsModule.Test` must stay green — this is a pure move.**
- `NFe.Infra/Danfe/DanfeRenderer.cs`: XML → self-contained HTML (inline CSS, `data:` barcode/QR images — the request interceptor blocks every non-`data:`/`about:` URL, `BillsService.cs:200-211`) → `HeadlessPdfRenderer`. A4 retrato for DANFE 55, 80 mm roll for DANFE-NFCe. Homologação watermark (F.11).
- `Danfe` / `Xml` actions + `nfe.pdf.download` / `nfe.xml.download` permissions + the two log-redaction prefixes in **both** middlewares (D.2).
- Tests: `DanfeRenderer_HomologationDocument_IncludesWatermark`, authorization tests, 2 `[InlineData]` rows.

**Phase 8 — Email with attachments.**
- `EmailStructure` + `EmailAttachment` (E.1); `EmailService.CreateEmail` BodyBuilder (E.2); `NFeService.EmailDocumentAsync` + `EmailToCustomer` action + `nfe.email`.
- Tests: `CreateEmail_NoAttachments_ProducesSameSinglePartBodyAsBefore` (regression guard on the shared path), `CreateEmail_WithPdfAndXml_ProducesTwoAttachments`, `EmailDocumentAsync_OverSizeBudget_Throws`, `EmailDocumentAsync_NonAuthorizedDocument_Returns422`.

**Phase 9 — Retire the process-wide flag; delete the stub.**
- `EnterpriseFiscalSettings.Enabled` + `.Environment` become authoritative. `NFeSettings` (`Configs/NFeSettings.cs`) shrinks to a **kill switch + defaults for tenant bootstrap**: keep `Enabled` as a global master off-switch (AND-ed with the per-tenant flag), delete `Environment`/`StateCode`/`Series`/`StartingNumber` or demote them to seed values. `NFeService.cs:60-64` becomes `if (!_settings.Enabled || !fiscalSettings.Enabled) return null;`.
- Update `appsettings.Development.json:72-78` and `appsettings.Production.json:66-72`.
- **Delete `EvangelionERPV2.NFe.Application/Providers/StubNFeProvider.cs`.** Leaving a provider that fabricates authorized notes with random access keys in a codebase that now issues real ones is a footgun with a one-line DI registration between it and production.
- Tests: `IssueAsync_GlobalKillSwitchOff_ReturnsNull`, `IssueAsync_TenantDisabled_ReturnsNull`.

---

## H. RISKS & OPEN QUESTIONS

**R1 — The duplicate-emission trap. MUST be solved; here is the mechanism.**

A `POST` to SEFAZ times out. The note may be authorized, may be rejected, may never have arrived. Retrying blind either double-emits (real tax liability, real money) or burns a number.

Solution, in this exact order — the ordering *is* the mechanism:

1. Claim the `NFeDocument` row (`Pending`, `TransmittedAtUtc = NULL`) via the existing unique-index dance (`NFeService.cs:113-134`).
2. `AllocateNextNumberAsync` — atomic, cross-instance (B.4).
3. Build the chave **locally** (`NFeAccessKeyBuilder`: cUF + AAMM + CNPJ + mod + serie + nNF + tpEmis + cNF + DV) and sign the XML.
4. **Persist `AccessKey`, `Number`, `Series`, `XmlContent` (unsigned-proc), still `Pending`, `TransmittedAtUtc = NULL`. COMMIT.**
5. **Set `TransmittedAtUtc = UtcNow`. COMMIT.** ← the write-ahead marker.
6. `POST` to SEFAZ. On a synchronous authorization, persist `Authorized` + `nProt` + `nfeProc`. On `nRec`, persist `ReceiptNumber` and poll `RetAutorizacao`.

Now every failure is recoverable, because **the chave is known before the first byte goes out**:

- Timeout / exception after step 5 ⇒ status stays `Pending`, `TransmittedAtUtc != NULL`. `IssueAsync` **refuses to retransmit** in this state; it calls `ConsultProtocolAsync(chave)` (or `ConsultReceiptAsync(nRec)` if one was captured) and adopts the truth: 100 → `Authorized`; 110/301/302/303 → `Denied`; 217 ("NF-e não consta na base") → the note genuinely never landed ⇒ **safe to retransmit the same XML, same number**; anything else → `Rejected`.
- Crash between 4 and 5 ⇒ `TransmittedAtUtc == NULL` ⇒ provably never sent ⇒ retransmit the same number, no consulta needed.
- `NFeProviderResult.NeverTransmitted` (set only for DNS/TLS/connect-refused, i.e. failures where no request was written) is the *only* other path that permits same-number retransmission.
- `NFeReconciliationService` sweeps `Pending ∧ TransmittedAtUtc < now-60s` on a timer and applies the same consulta, with backoff on `AttemptCount`, so a crashed request thread does not strand the row. It is also what the API's `202 Accepted` response points the client at.

The existing 15 s poll loop `WaitForFinalizedIssuanceAsync` (`NFeService.cs:345-360`) stays as the intra-request wait, but it must **not** throw `InsertDatabaseException("still in progress")` (`:359`) for a `Pending(unknown)` document — that maps to a 500. It should return the document and the controller should answer **202** with the current status.

**R2 — SEFAZ homologação credentials.** Homologação needs a valid A1 certificate for a real CNPJ; there is no sandbox certificate. **Open question for the user: do you have a company CNPJ and an A1 certificate we can use for testing, or do we need to buy one (~R$150–250/yr, e-CNPJ A1)?** Phase 0 cannot even start without it. SP/RS/PR/SC each require the CNPJ to have an active IE in that UF for anything beyond `StatusServico` — realistically we will test full issuance in **one** UF and rely on `StatusServico` + schema validation for the other three. **State that limitation up front.**

**R3 — Schema version churn.** Layout 4.00 has been stable since 2018, but NTs (Notas Técnicas) land 2-4×/year and can add mandatory fields with a hard cutover date. Mitigations: `SchemaVersion` is a per-tenant column (B.1); Unimake ships schema updates as package releases, so the response to an NT is a version bump + regression run, not a code rewrite; **subscribe someone to the NT mailing list — this is an operational commitment, not a technical one.** The `NFeXmlBuilderTests` golden files are what turns "they changed the schema" into a red build instead of a production rejection storm. **Reforma tributária / IBS-CBS (NT 2025.002) is a large, dated change on the horizon — explicitly out of scope for v1, flag it for planning.**

**R4 — NFCe contingency offline. OUT OF SCOPE v1. Stated plainly.** Offline NFCe (`tpEmis=9`) means emitting with a locally-generated chave, printing a DANFE marked as contingency, and transmitting within 24h. It requires durable local queueing, a distinct QR variant, a retransmission scheduler and a distinct rejection-handling path — comfortably the size of the entire rest of this spec. `ContingencyEnabled`/`ContingencyEnteredAt`/`ContingencyReason` exist as columns so the schema does not need to change later; **nothing reads them in v1.** If a tenant's NFCe volume makes SEFAZ downtime commercially unacceptable, that is a separate project. `// ponytail: contingency columns are inert placeholders; wire them only when a tenant actually needs offline NFCe.`

**R5 — SVC-AN / SVC-RS contingency. OUT OF SCOPE v1.** When a UF's autorizador is down, modelo 55 may be re-emitted through the Sistema Virtual de Contingência. `Configuracao.TipoEmissao` supports 6/7, so enabling it later is small — but the decision logic ("is the autorizador *really* down, or is it us?"), the operator UX and the reconciliation of notes split across two autorizadores are not. v1 behaviour on SEFAZ outage: the document stays `Pending`, the reconciler retries, `GetServiceStatus` tells the admin why. **Say so in the API docs so nobody expects magic.**

**R6 — PuppeteerSharp in the container.** `BillsService` already renders PDFs, so Chromium is presumably already resolved in the runtime image — **verify** before Phase 7, because `mcr.microsoft.com/dotnet/aspnet:10.0` ships none of Chromium's shared-library dependencies. If Bills PDF generation currently works in production, this is a non-issue; if it only works locally, Phase 7 inherits a pre-existing bug. **Check this early, it is a 5-minute check that can cost a sprint.**

**R7 — `Enterprise.Adress` (`Enterprise.cs:24`) is one free-text string.** `EnterpriseFiscalSettings` duplicates the address in structured form rather than migrating `Enterprise`. That is deliberate (smaller blast radius, no touching of the tenant root entity), but it means two addresses that can drift. Accepted. `// ponytail: structured address lives only on fiscal settings; unify onto Enterprise if a second consumer appears.`

**R8 — Open questions for the user.**
1. A1 certificate for testing — have one, or need to procure? (blocks Phase 0)
2. Which UF is the pilot tenant in? (determines which UF gets true end-to-end coverage)
3. Are any pilot tenants **Simples Nacional** (CRT 1/2 ⇒ CSOSN) or all **Regime Normal** (CRT 3 ⇒ CST)? Both are supported, but it changes which default set gets exercised first.
4. Is NFCe (65) actually needed at launch, or is NFe (55) sufficient for v1? NFCe adds the CSC secret, the QR builder, the 80 mm DANFE and the whole contingency conversation. **If 55 alone unblocks the business, Phases 6 ships later and the critical path shortens by roughly a third.**
5. Does the DANFE need to be emailed automatically on authorization, or only on explicit user action? The spec assumes **explicit only** (an endpoint, not a hook) — automatic send is a one-line addition afterwards, and doing it the other way round means un-sending emails.

---

### Critical Files for Implementation
- `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.NFe.Application/Services/NFeService.cs`
- `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.Shared/Entities/NFeDocument.cs`
- `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.Shared/Entities/EnterprisePayrollSettings.cs` (template for `EnterpriseFiscalSettings`)
- `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.Web/Security/RbacPermissions.cs`
- `/home/peanuut/CodeProjects/EvangelionERPV2/EvangelionERPV2.NFe.Infra/EvangelionERPV2.NFeModule.Infra.csproj`