# PDV sales: record the tender, finish the order, name the walk-in customer

**Date:** 2026-09-03
**Work item:** AB#33
**Status:** approved, not yet implemented

## Why

Three complaints about till sales, all about what the rest of the ERP can see once a
sale has been rung up.

1. **The order does not say how it was paid.** `PdvSalePayment` has held the tenders since
   the module was built — method, amount, authorisation code, installments, change — but
   nothing on `Order` does. Every order list, every export, every report that reads an
   order and not a till session shows a sale with no idea whether cash, PIX or a credit
   card settled it.
2. **A till sale is not "Paid", it is over.** `Paid` is a way-station on the backoffice
   ladder: an order can still be edited, re-priced, deleted. A sale where the customer
   took the goods and walked out is not in that state and never returns to it.
3. **A walk-in shows as nobody.** A PDV sale with no identified customer sets
   `Order.CustomerId = null`, `OrderDTO.CustomerName` maps to null, and the front end
   renders "Cliente Desconhecido" — which reads as a data problem, not as what actually
   happened.

## What exists today

- `PdvService.BuildOrder` (`EvangelionERPV2.Pdv.Application/Services/PdvService.cs:558`)
  builds the `Order` from the sync request and sets `Status = EnumOrderStatus.Paid`.
- Tenders are written by `CompleteAsync` into `PdvSalePayment`, one row per tender.
  Split payment is the normal case at a till, which is why it is a table.
- `EnumPaymentMethod` is the NFC-e `tPag` vocabulary collapsed to what a till offers.
  `NFeXmlBuilder.TPagFor` maps it. Every member has a fiscal counterpart.
- `CreateOrderRequestValidator` requires `CustomerId`. A backoffice order therefore always
  carries a customer, and a null customer on an order can only have come from the till.
- `OrderService.IsImmutableStatus` is `Finished || Refund`. It gates edit
  (`OrderService.cs:384`), delete (`:471`) and **refund** (`:234`, plus the guarded SQL at
  `:261`).
- There is no PDV void or return endpoint. A till mistake is corrected by
  `OrderController`'s refund, which returns stock and reverses the enterprise balance.

## Decisions

### 1. `Order.PaymentMethod`, a nullable column

`PdvSalePayment` stays the source of truth for tender detail. The new column answers one
question — *how was this order paid* — everywhere an order is read, without a join that
every read surface would otherwise have to grow.

- Single tender: that method.
- Two or more distinct methods: a new `EnumPaymentMethod.Mixed = 98`.
- Backoffice orders: null. Not "unknown"; there is genuinely no tender recorded for them.

`Mixed` is **order-level only**. It must never be written to `PdvSalePayment.Method` and
must never reach `NFeXmlBuilder.TPagFor`, which has no `tPag` for it. Two guards, because
one is a courtesy and the other is the rule:

- `PdvRequestValidators` currently accepts any `Enum.IsDefined` value
  (`PdvRequestValidators.cs:121`), so adding the member would silently make it a legal
  inbound tender. An explicit `!= Mixed` rule is required.
- `SyncSaleAsync` rejects a sale carrying a `Mixed` tender the same way it rejects an
  uncoded card — `Rejected`, not thrown, because no number of retries will make that
  payload acceptable.

Surfaced as `OrderDTO.PaymentMethod`. AutoMapper binds it by name; no `ForMember`.

### 2. PDV orders are created `Finished`

One line in `BuildOrder`. Every consumer of order status was audited:

| Consumer | `Paid` | `Finished` | Change |
|---|---|---|---|
| `EnterpriseBalanceContributionHelper.GetOrderRealizedContribution` | counted | counted | none |
| `OrderService.GetOpenExposureAsync` (credit limit) | excluded | excluded | none |
| `ReceivablesReportService`, `DunningService` | excluded | excluded | none |
| `OrderService.IsPaydayStatus` | true | true | none |
| `BankReconciliationService`, `DailyPanelService`, `OpportunityRadarService` | only `Refund` is special | same | none |
| `OrderService.IsImmutableStatus` | false | **true** | edit and delete blocked |

The immutability is the entire point of the change. Nothing about revenue, receivables,
credit exposure or payday moves.

### 3. Refund is exempted from the `Finished` guard

Decision 2 would otherwise make every till sale permanently unrefundable, and the till is
where mistakes are most likely and most urgent. Refund is therefore removed from the
`Finished` gate in both places that enforce it:

- the `IsImmutableStatus` check at `OrderService.cs:234` narrows to "already refunded",
- the guarded `UPDATE` at `:261` drops `Status <> Finished`, keeping `RefundedAt IS NULL`
  and `Status <> Refund` — which are what make the refund idempotent under a race.

Edit (`:384`) and delete (`:471`) keep the `Finished` guard exactly as it is.

**This widens behaviour beyond PDV, on purpose.** A `Finished` backoffice order becomes
refundable too. That is the more defensible reading of the two: a finished sale is
precisely the thing a refund exists to reverse, and "finished" should mean the order can
no longer be *rewritten*, not that money can never be given back. The period lock at
`:239` still refuses a refund into a closed month, which is the guard that actually
protects the books.

A first-class PDV void — its own permission, its own cash movement, NFC-e cancellation —
is the right long-term answer and is explicitly **not** in this scope.

### 4. "Venda PDV" comes from the mapper

One `ForMember` in `MapperConfig.cs:26`:

```csharp
src.Customer != null ? src.Customer.Name : "Venda PDV"
```

No new column. A null customer on an order can only be a till walk-in, because
`CreateOrderRequestValidator` requires `CustomerId` on every backoffice order. A PDV sale
that *did* identify its customer passes `CustomerId` through and keeps the real name.

An `Order.Origin` column was considered and rejected: it would record a fact the null
already records, and would need its own backfill to be trustworthy on historical rows.

## Migration

`AddOrderPaymentMethod` — add the column, then a hand-written backfill in the style of
`AddFiscalNatOp` and `AddNFCeContingencySeries`.

```sql
-- Status: Paid = 2, Finished = 5.  PdvSale.Status: Synced = 2.
UPDATE o
SET o.Status        = 5,
    o.PaymentMethod = m.Method
FROM [Order] o
JOIN PdvSale s ON s.OrderId = o.Id AND s.Status = 2
CROSS APPLY (
    SELECT CASE WHEN COUNT(p.Method) = 0            THEN NULL
                WHEN COUNT(DISTINCT p.Method) = 1   THEN MIN(p.Method)
                ELSE 98 END AS Method
    FROM PdvSalePayment p
    WHERE p.PdvSaleId = s.Id
) m
WHERE o.Status = 2
  AND o.RefundedAt IS NULL;
```

- Scoped to orders that a synced PDV sale points at. A backoffice order that happens to be
  `Paid` is untouched and keeps a null `PaymentMethod`.
- A sale with no payment rows cannot exist (`SyncSaleAsync` refuses it), but the `CASE`
  answers `NULL` rather than `Mixed` for one anyway: a corrupt row should read as "not
  known", never as a split payment that never happened.
- Refunded orders are left alone: their status already says what happened to them, and
  moving a refunded row to `Finished` would lose that.
- `Down` drops the column. It does not restore `Paid`: the status a row held before the
  backfill is not recoverable from the row itself, and inventing one would be worse than
  leaving the corrected value in place.

The model snapshot is updated alongside, as every migration in this repo does.

## Testing

`EvangelionERPV2.PdvModule.Test`:

- a single-tender sale stores that method on the order
- a sale split across two methods stores `Mixed`
- a sale split across two tenders of the *same* method stores that method, not `Mixed`
- a synced sale lands on `Finished`, not `Paid`
- a tender submitted as `Mixed` is `Rejected`, and the rejection reason says why
- an order with no customer maps to `CustomerName == "Venda PDV"`; one with a customer
  keeps the customer's name

`EvangelionERPV2.OrderModule.Test`:

- refunding a `Finished` order succeeds, returns stock and reverses the balance
- refunding an already-refunded order still throws
- editing a `Finished` order still throws
- deleting a `Finished` order still throws

## Out of scope

- A PDV void/return endpoint with its own permission and cash movement.
- Recording a payment method on backoffice orders. The column is nullable and ready for
  it; nothing writes it outside the till.
- Changing how a PDV sale settled by `Boleto` or `StoreCredit` is treated. It is marked
  paid and excluded from receivables today, which is arguably wrong, and is a separate
  question from this one.
