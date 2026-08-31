# NFC-e (modelo 65) for the till — plan

Written 2026-08-31, after the decision in ROADMAP-STATUS §A8 / §D1: **the till issues NFC-e.**
Supersedes the two-line sketch in `SPEC-remaining-code.md` §2.3 (Phase 6), which was written before
M29 existed and assumed NFC-e was a back-office concern.

**Scope is not confirmed yet.** §0 lists what has to be answered before task 1 starts.

## Goal

A counter sale rung on the till produces an authorised NFC-e, with a consumer QR code the customer
can scan, and keeps selling when SEFAZ is unreachable.

**Non-goals.** SAT-CF-e (SP-only, needs certified hardware — rejected with the decision). NFS-e
(services, per-municipality, see §A6). Changing anything about modelo 55, which is a different
document issued from a different screen for a different reason.

## Context: how much already exists

Far more than the roadmap's "size L" implies. Verified in the tree on 2026-08-31, not remembered:

| Piece | State |
|---|---|
| `NFeDocumentType.NFCe`, `NFeDocument.ModelFor` → 65 | **exists** (`NFeDocument.cs:174-176`) |
| `NFeNumberSequence` keyed `(enterprise, model, series, environment)` | **exists** — 65 needs no schema change (`NFeNumberSequence.cs:27`) |
| `NFeXmlBuilder` branching on model | **exists** — `TpImp = FormatoImpressaoDANFE.NFCe`, `IndPres = OperacaoPresencial` (`NFeXmlBuilder.cs:141,150`) |
| `EnterpriseFiscalSettings.CscId` | **exists** (`:112`) |
| `Configuracao.CSCIDToken` wiring | **exists**, guarded `isNFCe && CscId.HasValue` (`UnimakeConfiguracaoFactory.cs:91-97`) |
| `NFeController.Issue(orderId, type)` accepting `NFCe` | **exists** (`:138`) |
| `NFeDocument.QrCode` / `.UrlChave` columns | **exist — and nothing ever writes them.** The only assignments in the codebase set them to `""` on reset (`NFeService.cs:638-639`) |
| `Configuracao.CSC` (the secret itself) | **missing.** `UnimakeConfiguracaoFactory.cs:92-95` carries a `ponytail:` comment naming this exact gap and where the secret belongs |

So the work is narrower than a new module and much narrower than NFe phases 1–5 were. It is
finishing a seam the code already marks.

## 0. Blocking questions — task 1 does not start until these are answered

| # | Question | Why it blocks | Ref |
|---|---|---|---|
| Q1 | **The A1 certificate** | No certificate, no fiscal document of any model. NFC-e cannot be tested even in homologação | §A1 |
| Q2 | **The CSC and its id**, issued per UF through the SEFAZ portal | The consumer QR is an HMAC over the access key **with the CSC as the key**. Without it there is no QR, and an NFC-e without a QR is not a valid consumer document | §M6 |
| Q3 | **Cancellation window** — national reference is 30 min; RS and PR have practised shorter | Hard-codes a refusal the operator sees at the counter. Wrong value = either an illegal cancellation accepted or a legal one refused | §M10 |
| Q4 | **Consumer-identification threshold** — R$ 10.000 reference, unverified per UF | Above it, `dest` (CPF/CNPJ) is mandatory; below it, optional. Wrong value = rejected notes at the counter | §M11 |
| Q5 | **Contingency: does the pilot need offline NFC-e?** See §4 R1 — this is the biggest design fork | Decides whether this is ~5 tasks or ~12 | new |

Q1 and Q2 are procurement. Q3, Q4 and Q5 are decisions. **Q5 is the one that changes the shape of
the work**, and it is not in the existing manual list because it only became a question when the
till appeared.

## 1. Scope → modules

Bounded contexts touched: **NFe** (the document), **Pdv** (what triggers it), **Enterprise** (the
CSC as tenant configuration), **Web** (endpoints, policy).

No new module. Handling PII: yes — `dest` carries a consumer CPF, which is personal data under LGPD
and must not reach a log, a DTO or the till's SQLite. `/net-review` applies before merge.

## 2. Layer breakdown

### NFe module

**Infra**
- `UnimakeConfiguracaoFactory` — resolve the CSC secret and set `Configuracao.CSC`, closing the
  `ponytail:` at `:92`. Secret at `evangelion/{env}/nfe/csc/{enterpriseId}`, resolved through
  `AWSKMSKeyProvider.GetKMSKeyAsync`, the same path the certificate already uses. **Never a column.**
- `NFeXmlBuilder` — `infNFeSupl` (qrCode + urlChave), `dest` gated on the §Q4 threshold, the NFC-e
  homologação legend (§M12), and NFC-e's own `tPag` handling: a counter sale has a real payment
  breakdown, unlike modelo 55 where `tPag` is a single constant (§A7).

**Application**
- `NFeService` — persist `QrCode` and `UrlChave` on authorisation. They exist as columns and are
  only ever cleared today; this is the gap that would otherwise ship an NFC-e nobody can scan.
- NFC-e cancellation window (§Q3) enforced separately from modelo 55's.

**Web**
- `UpdateCsc` endpoint: takes `CscId` + the CSC, writes the id to `EnterpriseFiscalSettings` and the
  secret to Secrets Manager. Admin-tier. **The CSC is write-only** — it is never read back through
  the API, exactly like the certificate password.
- Log-redaction prefixes for any new route in **both** `RequestLoggingMiddleware` and
  `ExceptionHandlingMiddleware` — the access key embeds CNPJ + note number (§2.4 of the spec).

### Pdv module
- A synced sale issues an NFC-e. **Out of band, never on the sale's write path** — see §4 R2.

## 3. Tasks

Ordered so the tree stays green between commits. Each is one commit with a verification line.

| # | Task | Verify |
|---|---|---|
| 1 | CSC secret resolution → `Configuracao.CSC` | Unit test: a configuration built for NFCe with a CscId carries the resolved CSC; one built for 55 never resolves the secret at all |
| 2 | QR code + `urlChave` into `infNFeSupl` | `NFeXmlBuilderTests`: the emitted XML has `infNFeSupl/qrCode`; the hash matches a known-good vector for a fixed CSC + access key |
| 3 | Persist `QrCode`/`UrlChave` on authorisation | Service test: an authorised NFCe has both populated; `NFeDocumentDTO` still omits them |
| 4 | `dest` above the §Q4 threshold, CPF optional below | Builder test at threshold − 0.01, threshold, threshold + 0.01 |
| 5 | `UpdateCsc` endpoint + policy in all four RBAC places | `RbacControllerPolicyCoverageTests` row; a test proving the CSC is not readable back |
| 6 | NFC-e cancellation window (§Q3) | Service test: inside the window cancels, outside is refused with a message naming the window |
| 7 | Till issues the NFC-e out of band | Pdv test: a synced sale queues issuance; **a SEFAZ failure leaves the sale Synced, never Rejected** |
| 8+ | Contingency, only if §Q5 is yes | Separate plan — see R1 |

Tasks 1–3 are the spine: without them there is no valid NFC-e. Tasks 4–6 are correctness at the
counter. Task 7 is what makes it the till's document rather than a button someone remembers to press.

## 4. Risks

**R1 — Contingency is the real fork, and it is not in the old spec.**
`UnimakeConfiguracaoFactory` hard-codes `TipoEmissao.Normal` with a deliberate comment saying
contingency stays a code change. That was the right call for modelo 55, issued from a back office
with a working connection. **A till is not that.** A shop floor loses its link, and NFC-e has a legal
offline mode for exactly this: issue against a contingency series, print, transmit within the
regulated window. If the pilot needs it, that is its own plan — offline numbering, a contingency
series, a re-transmission queue and the window. If the pilot always has connectivity, say so in
writing and the till simply refuses to sell when SEFAZ is down. **Do not discover this at a counter.**

**R2 — Issuance must never be on the sale's write path.**
`PdvService.SyncSaleAsync` currently either records a sale or rejects it terminally, and `RejectAsync`
is final — the till stops resending. If NFC-e issuance runs inside that path, one SEFAZ timeout
permanently rejects a sale the customer already paid for. Issuance is out of band, and its failure is
visible and retryable without touching the sale's status.

**R3 — The CSC is a signing key.**
It is not a setting. Anyone holding it can mint valid consumer QR codes for the shop. Secrets Manager
only, write-only through the API, never in a DTO, a log, a response or the till's SQLite. The till
never needs it — it holds a charge id and a sale, exactly as it holds no Stripe key.

**R4 — Every value in §0 is a guess until an autorizador answers.**
The same trap as `SefazCStatMap`: the code will compile, the tests will pass against fixtures, and
the first real UF may disagree. Thresholds and windows are configuration, not constants, so a wrong
answer is an edit and not a release.

**R5 — Consumer CPF is LGPD data.** It enters at a counter, on a shared machine, and must not be
logged or cached on the terminal.

## 5. Definition of done

- [ ] Builds Release, whole suite green
- [ ] New behaviour covered in `EvangelionERPV2.NFeModule.Test` and `EvangelionERPV2.PdvModule.Test`
- [ ] `/net-review` for layering + LGPD (consumer CPF)
- [ ] Migration applied through `./scripts/local-db-update.sh` if schema changed
- [ ] **One real NFC-e authorised in homologação** — the only evidence that counts, and it needs Q1 + Q2

## Decisions worth an ADR once §0 is answered

`docs/adr/` — the cancellation window and threshold as configuration rather than constants (R4), and
the contingency answer (R1), which precludes or requires a whole body of work.
