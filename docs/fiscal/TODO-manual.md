# Fiscal — manual TODO (NFe + Boleto)

Nothing on this list is fixable in code. Each item blocks something; the code backlog is in
[`SPEC-remaining-code.md`](./SPEC-remaining-code.md).

IDs are referenced from the code spec as §M1 / §M-B1. Last reviewed 2026-08-20.

---

## NFe / NFCe

### Purchases and provisioning

- [ ] **M1 — Buy an e-CNPJ A1 certificate** for a real CNPJ (R$109–235/yr, see pricing below). No SEFAZ sandbox certificate
      exists; homologação requires a real one. **It blocks *verification* of phases 4–9, not their
      development** — the phase-0 gate signed and schema-validated a complete modelo-55 note with a
      self-signed cert, so only the mutual-TLS handshake with SEFAZ needs the real thing. See
      [`SPEC-remaining-code.md`](./SPEC-remaining-code.md) §2.1 for the buildable-now / cert-gated split.
      Still buy it early: M4 runs after it and is often the slower half.
      **Alternatives were searched (2026-08-14) and there are none.** ICP-Brasil is required for both the
      XML signature and the mutual TLS, in homologação as well as produção; the phase-0 gate proved it
      empirically when PR rejected a self-signed cert with `ssl/tls alert certificate unknown`
      (`../nfe/HANDOFF.md` §7.3). The "certificado A1 de teste" kits circulating online are ordinary
      self-signed PKCS#12 files (password `123456`) that SEFAZ refuses — and we already generate an
      equivalent one in-test at `EvangelionERPV2.NFeModule.Test/NFe/EnterpriseCertificateProviderTests.cs`,
      so there is nothing to download. An e-CPF cannot substitute: the signing certificate must carry the
      emitter's CNPJ. Mock SEFAZ servers exist (`jardelnovaes/sefaz-mocked`) but cost more plumbing than a
      C# fake at the `INFeProvider` boundary, which is what `SPEC-remaining-code.md` §2.1 already prescribes.
      Current pricing: e-CNPJ A1 12 months runs R$109–179 on promotion, R$219–235 at list. *Owner: you / finance.*
- [ ] **M2 — IAM + secret provisioning.** The API role needs `secretsmanager:GetSecretValue` /
      `PutSecretValue` / `CreateSecret` on `evangelion/{env}/nfe/cert/*` (and `/csc/*` if NFCe ships).
      Without it the failure surfaces as "certificate not configured", which sends you looking in the
      wrong place. *Owner: you / infra.*
- [ ] **M3 — Egress allowlist for SEFAZ**, using the list verified in the phase-0 gate:
      `*.fazenda.sp.gov.br`, `*.nfce.fazenda.sp.gov.br`, `*.sefazrs.rs.gov.br`, `*.svrs.rs.gov.br`,
      **`*.sefa.pr.gov.br`** (not `fazenda.pr.gov.br`), `*.nfe.fazenda.gov.br`. **SC needs no domain of
      its own** — it is served entirely by SVRS. *Owner: you / infra.*
- [ ] **M4 — Credenciamento of the emitting CNPJ** at the pilot UF's SEFAZ, homologação first, then
      produção. Separate from the certificate and often the slower of the two. *Owner: pilot tenant.*

### Decisions only a human can make

- [ ] **M5 — Which UF is the pilot in?** End-to-end issuance needs an active IE in that state, so one UF
      gets true coverage and the other three get `StatusServico` + schema validation only. Blocks the
      phase-4 test plan. *Owner: you.*
- [ ] **M6 — Is NFCe (65) needed at launch?** If NFe 55 alone unblocks the business, phase 6 ships later
      and the critical path shortens by roughly a third. *Owner: you.*
- [ ] **M7 — Simples Nacional (CRT 1/2 ⇒ CSOSN) or Regime Normal (CRT 3 ⇒ CST)?** Both are supported;
      decides which default set the phase-4 fixtures exercise. *Owner: pilot tenant's accountant.*
- [ ] **M8 — Confirm the DANFE is emailed only on explicit user action**, not automatically on
      authorization. The spec assumes explicit; the reverse means un-sending emails. *Owner: you.*
- [ ] **M13 — `infRespTec`: the software developer's CNPJ, contact name, e-mail and phone.** Required by
      NT 2018.005 in every UF, and the values are business facts nobody has told this system. **No longer a
      code change (2026-08-24):** they are configuration — `NFeRespTecSettings` in
      `appsettings.{Development,Production}.json` — bound installation-wide, because the software developer
      is the same company for every tenant. `SefazNFeProvider` resolves them and `NFeXmlBuilder` writes the
      group; all four values or none, and a partial group is omitted with a warning naming this item rather
      than half-filled, because a placeholder CNPJ is a false declaration to the fisco. `IdCsrt` +
      `CsrtSecret` (a Secrets Manager reference) only if the pilot UF demands the CSRT — the library derives
      `hashCSRT` from it and the chave. **Fill the four values or the first real transmission is still
      rejected.** *Owner: you — the values, not the code.*
- [ ] **M14 — one nature of operation per tenant, and only the till knows how it was paid.**
      **Both halves moved on 2026-09-01.** `tPag` is now real on an NFC-e: the note is built from the
      sale's own `PdvSalePayment` rows, one `detPag` per tender, with `vTroco` and the card
      authorisation code. And `natOp` is no longer a constant in the XML builder — it is
      `EnterpriseFiscalSettings.DefaultNatOp`, defaulting to `VENDA DE MERCADORIA`, so the wording is
      the shop's and its accountant's rather than a release.
      **What is still owed by a human:** whether the pilot ever issues anything but a plain sale. A
      devolução or a remessa needs a natOp that follows the CFOP, and a modelo 55 still declares
      `99 / OUTROS` because a back-office `Order` records no means of payment — inventing one would be
      a claim to the fisco. Confirm plain sales only, or a nature-of-operation field goes on `Order`.
      *Owner: you / pilot tenant's accountant.*
- [ ] **M9 — `NFeDocument.EnterpriseId` FK: `Restrict` or no FK at all.** The original "cascade would
      delete fiscal documents" argument does not survive scrutiny — `NFeNumberSequence` already
      `Restrict`s the tenant delete. A code decision, but yours to make. *Owner: you, before merge.*

### Values the accountant must confirm before produção

These are configuration columns, never constants, and must be checked against each UF's RICMS:

- [ ] **M10 — NFCe cancellation window.** 30 min is the national reference; RS and PR have practised
      different values. NFe's 24 h is reliable. *Owner: accountant.*
- [ ] **M11 — NFCe consumer-identification threshold.** R$ 10.000 reference, unverified per UF.
      *Owner: accountant.*
- [ ] **M12 — The mandatory homologação legend string**, against the current MOC/NT. NFCe uses the
      `NFC-E` variant. *Owner: accountant.*

### Ongoing

- [ ] **M13 — Subscribe someone to the SEFAZ Notas Técnicas mailing list.** NTs land 2–4×/year and can
      add mandatory fields with a hard cutover date. An operational commitment, not a technical one.
      *Owner: you.*
- [ ] **M14 — Release notes** if `nfe.cancel` moves Supervisor → Manager. Anyone who is Supervisor today
      and can cancel gets a 403 after phase 5. *Owner: you.*

---

## Boleto

The boleto is the more dangerous of the two right now: it *looks* finished. It renders a valid barcode
and emails a PDF, but no bank has ever heard of the title and nothing marks it paid.

### Bank relationship — all of this precedes any remessa code

- [ ] **M-B1 — Who is the beneficiary?** The platform (one bank account for everyone, which is a payment
      intermediation arrangement with its own regulatory weight) or each tenant with its own bank
      contract? **This is the single decision that shapes the whole module** — per-tenant means building
      `EnterpriseBillingSettings` (spec §B1); platform-only means writing that down so `BillsSettings`
      being global stops looking like a bug. *Owner: you.*
- [ ] **M-B2 — Sign a convênio de cobrança with the bank.** Production config currently says bank `33`
      (Santander) and the code default is `237` (Bradesco) — pick one and make them agree. *Owner: you.*
- [ ] **M-B3 — Collect the real contract values:** agência + digit, conta + digit, código do
      beneficiário/cedente, código de transmissão, carteira and variação, tipo de cadastramento (com or
      sem registro), and the **faixa de nosso número** (start, end, digit rule). Everything currently in
      `appsettings.Production.json` under `BillsSettings` is placeholder. Real values go to **Secrets
      Manager, never into a committed file** (spec §B7). *Owner: you.*
- [ ] **M-B4 — Obtain the bank's CNAB 240/400 layout manual** for the contracted wallet. BoletoNetCore
      implements the common layouts, but banks vary the free-use positions per convênio and the manual is
      the only authority. *Owner: you.*
- [ ] **M-B5 — Transport credentials for remessa/retorno** (SFTP host + key, or the bank's cobrança API
      credentials). Plus who watches the retorno file arriving daily. *Owner: you / infra.*
- [ ] **M-B6 — PIX on the boleto (híbrido)?** Separate agreement with the bank plus a registered chave
      PIX. Decide **before** the remessa layout is built — retrofitting it means revisiting the layout.
      *Owner: you.*
- [ ] **M-B7 — Bank homologação.** Most banks validate a test remessa file and approve the printed boleto
      layout before releasing the range to production. Lead time is measured in weeks, not days. Book it
      as soon as M-B2 closes. *Owner: you.*

### Operational checks

- [ ] **M-B8 — Does Chromium actually launch in the *production* image?** The Dockerfile installs the
      shared libraries, but PuppeteerSharp downloads the browser at runtime — that needs egress and a
      writable path. The failure is **silent**: the bill email goes out with no PDF attached. Five-minute
      check that also de-risks NFe phase 7, which reuses the same renderer. *Owner: you, needs production
      access.*
- [ ] **M-B9 — Reconciliation ownership.** Once retorno processing exists, someone has to own the daily
      exceptions: partial payments, payments after the due date with interest, titles the bank rejected.
      That is a process, not a feature. *Owner: you.*
- [ ] **M-B10 — Keep `BillsSettings.Enabled = false` in production** until M-B3 and the payment-state
      work (spec §B4) are both done. Enabling it today emails customers a boleto no bank will settle.
      *Owner: you.*

### Legal / accounting

- [ ] **M-B11 — Confirm the boleto's espécie and instructions with the accountant.** The code hardcodes
      `TipoEspecieDocumento.DM` (duplicata mercantil) and takes the instruction text from configuration
      ("Nao receber apos o vencimento."). Wrong espécie affects protest rights. *Owner: accountant.*
- [ ] **M-B12 — Interest / fine policy after due date** (juros de mora, multa). Not modelled anywhere in
      the code today; it must be a decision before it becomes a field. *Owner: you / accountant.*

---

## The two shortest paths

- **NFe**: M1 unblocks the SEFAZ round trip, and only that. Buy the certificate, and write the phase-4
  remainder in parallel while it is issued — none of it is gated on the cert (`SPEC-remaining-code.md` §2.1).
  The write-ahead rewrite, the largest cert-free piece, landed 2026-08-14; the reconciler it created work
  for is next, and also cert-free.
- **Boleto**: M-B1 unblocks everything else. Until ownership is decided, both `EnterpriseBillingSettings`
  and the remessa layout are guesses.
