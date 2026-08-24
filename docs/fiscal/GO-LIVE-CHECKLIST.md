# NFe go-live — the day the certificate arrives

Written 2026-08-24. The ordered procedure for taking modelo 55 issuance from "code complete,
unverified" to "one real note authorised". Every step says what to run, what a pass looks like, and
what the likely failure means — so the first transmission is one session rather than three.

**Prerequisite that is not a step:** the A1 certificate itself (`TODO-manual.md` §M1) and the
tenant's credenciamento with its UF (§M4). Neither is code, both take days, and the second is often
the slower one.

`infRespTec` is **no longer a blocker** — as of 2026-08-24 it is configuration
(`NFeRespTecSettings`), see step 0.

---

## Step 0 — configuration, before anything is uploaded

- [ ] Fill `NFeRespTecSettings` in `appsettings.{Development,Production}.json`: `Cnpj`,
      `ContactName`, `Email`, `Phone` — the **software developer's** four values, not the tenant's.
      All four or none: a partial group is omitted with a warning in the log, and a note without the
      group is the pre-2026-08-24 behaviour SEFAZ has been rejecting under NT 2018.005.
- [ ] `IdCsrt` + `CsrtSecret` only if the pilot UF demands the CSRT (chiefly NFC-e). The CSRT is a
      Secrets Manager reference; the fiscal library derives `hashCSRT` from it and the chave.
- [ ] Confirm the tenant's `EnterpriseFiscalSettings`: `Environment = 2` (homologação), the correct
      `StateCode`, CRT, série, and the complete emitente address. The builder hard-errors on an
      incomplete one rather than emitting a blank `xLgr`.
- [ ] Confirm the customer used for the test has the nine fiscal-address columns filled
      (`20260820100000_AddCustomerFiscalAddress`). `enderDest` needs each part separately.
- [ ] `NFeSettings:Enabled = true` for the installation **and** `EnterpriseFiscalSettings.Enabled`
      for the tenant. They AND together.

## Step 1 — upload the certificate

- [ ] `POST /api/v1/NFeFiscalSettings/UploadCertificate` (admin-only, multipart: `File` + `Password`).
- [ ] `GET /api/v1/NFeFiscalSettings/CertificateStatus` returns subject, expiry and days remaining.

**If it fails:** the endpoint rejects anything that is not a readable PKCS#12 before storing it. A
`plain:` base64 PFX in configuration is refused in every environment by design — the certificate
lives in Secrets Manager under `NFeSettings:CertificateSecretPrefix` + the enterprise id.

## Step 2 — cStat 107, the handshake that proves the TLS chain

- [ ] Call the status-serviço path (`SefazNFeProvider.GetStatusAsync`).

**Pass:** cStat **107**, "Serviço em Operação".

| Failure | What it means |
|---|---|
| TLS handshake refused | the certificate is not trusted by the autorizador — a self-signed cert reaches exactly this far and no further |
| cStat 108 / 109 | the autorizador is down or in scheduled maintenance. Not your bug; retry |
| Timeout with no cStat | check the UF resolution in `UnimakeConfiguracaoFactory` — it never sets a URL, the UF resolves the endpoint |

**Do not proceed past a failure here.** Everything downstream assumes a working socket.

## Step 3 — one note in homologação

- [ ] `POST /api/v1/NFe/Issue/{orderId}` on an order whose customer and products are complete.
      Use the tenant's homologação série.

**Pass:** `NFeStatus.Authorized`, cStat **100**, a protocol number, and `AuthorizedAtUtc` set.

| Failure | What it means |
|---|---|
| `ValidarXMLException` before the socket | schema rejection, thrown **inside** the `NFeAutorizacao` constructor with element, line and column. Nothing was transmitted; the allocated number is reusable |
| cStat 204 | duplicidade — this chave was already authorised. `NFeReconciliationService` should have recovered it; check `TransmittedAtUtc` |
| cStat 217 | SEFAZ has never seen the note. `SefazCStatMap.ForConsult(217)` clears `TransmittedAtUtc` so the number is not burned |
| cStat 539 | duplicidade with a different chave — a real fiscal problem, not a retry |
| A rejection naming `infRespTec` | step 0 was skipped or half-filled. The log carries a warning naming §M13 |
| A rejection naming ICMS or CSOSN | `NFeXmlBuilder` covers CST 00 and CSOSN 102/103/300/400 only; every other code throws by name before transmission, so a *SEFAZ* rejection here means the values are wrong, not unsupported |

Record the cStat of every rejection: **every mapping in `SefazCStatMap` is unverified until a real
autorizador produces it.** This is the whole point of the exercise.

## Step 4 — consult and cancel the same note

- [ ] `GET /api/v1/NFe/Consult/{accessKey}` — confirm the authorised state round-trips.
- [ ] `POST /api/v1/NFe/Cancel/{accessKey}` with a justification of 15–255 characters.

**Pass:** event 110111 homologated, cStat **135** (or **155**, cancelled out of time),
`CancelledAtUtc` and `CancelProtocol` set.

**If cancel reports success but nothing changed:** that was the pre-M6 lie and it was fixed —
`SefazNFeProvider` now reports the event's own cStat. A 501 means the 24-hour window has passed.

## Step 5 — only now, produção

- [ ] Flip `EnterpriseFiscalSettings.Environment` to `1`.
- [ ] Reset the série or start a new one — homologação and produção numbering are separate books.
- [ ] Issue one low-value real note and cancel it, repeating steps 3 and 4.

## Step 6 — write down what SEFAZ actually said

- [ ] Update `SPEC-remaining-code.md` §2.1: replace "unverified" with the cStats you observed.
- [ ] Correct any `SefazCStatMap` entry the real autorizador contradicted.
- [ ] Only then is **M7** (phases 7–9) and **M20b** (DFe distribuição + manifestação) unblocked.
      Both consume SEFAZ's answers; building them on guessed mappings compounds the guess.

---

## Still owed after go-live

- **§M14** — `natOp` and `tPag` are single constants (`VENDA DE MERCADORIA`, `99 / OUTROS`) because
  `Order` records neither. Correct for a pure sales flow, wrong the moment devolução or a real
  payment breakdown matters.
- **§M13 (operational)** — subscribe someone to the SEFAZ Notas Técnicas mailing list. NTs land
  2–4×/year and can add mandatory fields with a hard cutover date.
- **Release note** if `nfe.cancel` moved Supervisor → Manager: anyone Supervisor today who can
  cancel gets a 403.
