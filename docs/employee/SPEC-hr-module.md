# Employee module — HR expansion spec (atestados, férias, advertências)

Written 2026-08-14 against `feature/33-Boleto-and-NFE-emission`. Verified against the working tree, not
remembered.

Scope decided with the requester on 2026-08-14:

| Question | Answer |
|---|---|
| Who submits | **HR / manager only.** No employee self-service in v1. |
| In scope | Atestado, férias, advertências. |
| Férias and money | **Full payroll integration** — an approved férias moves cash. |
| KPIs | **Deferred.** Not specified here; §8 records what the schema must carry so they stay cheap. |

---

## 1. State of play, measured

| Thing | Reality in the tree today |
|---|---|
| `Employee` entity | `Shared/Entities/Employee.cs`. Name, phone, email, position, picture, `Document`, `Salary`, `HireDate`, `TerminationDate`, `EnterpriseId`. Nothing time-based beyond hire/termination. |
| Employee module | `Employee.Application` + `Employee.Domain` only — **no `Employee.Infra`**; the repository sits in `Domain/Repositories`, same as Order. |
| `IEmployeeService` | Five members: `Delete`, `Update`, `CreateAsync`, `UpdatePictureAsync`, `GetPictureBase64Async`. CRUD and a picture, nothing else. |
| Payroll | Lives in **Bills**, not Employee: `Bills.Application/Services/PayrollService.cs`. Reads `IRepository<Employee>` directly. |
| Accruals | `PayrollBillDetail` already accrues `ThirteenthSalaryProvision` (gross/12) and `VacationProvision` (gross/9 = 1/12 × 4/3) every competência. |
| Worked days | `PayrollService.GetWorkedDays:317` intersects the month with `[HireDate, TerminationDate]` and **nothing else**. No absence reduces pay today. |
| `Employee` ↔ `User` | **No link.** No `UserId`, no navigation, either direction. |
| RBAC | `RbacPermissions.Employees.{Read,Create,Update,Delete,UploadPicture}`, `Payroll.{Read,Generate,UpdateSettings}`. |
| LGPD | `Document` and `Salary` are already withheld from `EmployeeDTO`, pinned by `EvangelionERPV2.Test/Security/EmployeeDtoLgpdSerializationTests.cs`. `/Employee/*` write routes are body-redacted in `RequestLoggingMiddleware:179-181`. |

Two consequences fall straight out of that list:

- **HR-only was the right call.** Self-service would have needed `Employee.UserId` first, and that column
  is a migration plus a backfill plus an identity-matching rule for every existing row. Nothing in this
  spec needs it.
- **The absence spine has to be one table.** Payroll must answer "how many days did this person actually
  work in this month" with a single query. Three sibling tables (atestado, férias, suspensão) would make
  that a three-way union that grows every time HR gains a concept.

---

## 2. The rules that shape the schema

These are CLT rules, not preferences. Getting them wrong is expensive in a way that a code review will not
catch, so they are written down before any table is.

### 2.1 Atestado (medical leave)

- The employer pays the **first 15 days** of an incapacity. From **day 16** the INSS pays and the
  employment contract is *suspended* — the employer pays no salary for those days.
- The 15 days are **cumulative per illness within 60 days**: an employee who returns and relapses with the
  same CID inside 60 days does not restart the count. This is why `IsRelapse` and the CID both have to be
  stored, not just a day count.
- **The CID is health data** — LGPD art. 5 II and art. 11 special category, a stricter class than
  `Document` or `Salary`. It must be encrypted at rest (`SharedFunctions.Encrypt`, already used for other
  sensitive columns), never serialized on a list DTO, and never reach a log or the audit trail.

### 2.2 Férias

- **Período aquisitivo**: 12 months worked earns 30 days. **Período concessivo**: the following 12 months,
  inside which the employer *must* grant them.
- Miss the concessivo window and the férias are owed **in dobro** (art. 137) — double pay. This is a real,
  silent, accruing liability and it is the single best reason this module exists.
- Faltas injustificadas reduce the entitlement on a step table (art. 130): ≤5 → 30 days, 6–14 → 24, 15–23
  → 18, 24–32 → 12, >32 → 0. So the entitlement is **computed from the absence table**, never stored as a
  constant 30.
- Splitting (art. 134 §1): at most 3 periods, one of them ≥14 days, none <5 days.
- **Abono pecuniário** (art. 143): the employee may sell up to 1/3 — 10 days — requested at least 15 days
  before the aquisitivo closes.
- Pay is salary + the **1/3 constitucional**, due up to 2 days before the period starts (art. 145).

### 2.3 Advertências

- Progressive discipline (verbal → written → suspension → justa causa) is practice, not statute; the
  escalation history is what makes a later dismissal defensible, so the records are ordered and immutable.
- A **suspensão disciplinar may not exceed 30 consecutive days** (art. 474) — beyond that it is legally a
  dismissal. A hard validation, not a warning.
- Suspension days are **unpaid**, so a suspension is also an absence. Same table.

---

## 3. Schema

Four new entities in `Shared/Entities` (the repo keeps all entities there, not in each `*.Domain`).

### 3.1 `EmployeeAbsence` — the spine

Every dated non-working period, whatever caused it. **This is the only table payroll reads.**

```
EmployeeId, EnterpriseId          scoped like every other tenant row
Type                              EnumAbsenceType: MedicalLeave, Vacation,
                                  DisciplinarySuspension, UnjustifiedAbsence, Other
StartDate, EndDate                date-only semantics, inclusive
EmployerPaidDays                  days the employer owes salary for
SocialSecurityPaidDays            days INSS covers (atestado from day 16)
UnpaidDays                        suspension, faltas
Status                            Draft / Approved / Cancelled
Notes
```

The three day-count columns are **stored, not derived at read time**. They are the output of rules that
change by law and by case (the 60-day relapse window, the 15-day employer limit); recomputing them from
`Type` at every payroll run would silently rewrite history the next time a rule changes.

Index: `(EmployeeId, StartDate, EndDate)` and `(EnterpriseId, Status)`.

### 3.2 `EmployeeMedicalCertificate` — 0..1 per absence

```
AbsenceId                         FK, Restrict
CidCode                           ENCRYPTED. Health data, §2.1
DoctorName, DoctorCrm
IssueDate
IsRelapse                         drives the 60-day cumulative rule
DocumentKey                       S3 object key, same pattern as Employee.Picture
```

### 3.3 `EmployeeVacationAccrual` — the entitlement ledger

One row per período aquisitivo per employee. Not an absence — an entitlement.

```
EmployeeId, EnterpriseId
AcquisitionStart, AcquisitionEnd
ConcessionDeadline                = AcquisitionEnd + 12 months. The dobro clock.
EntitledDays                      computed from faltas via the art. 130 table
ScheduledDays, TakenDays, SoldDays
Status                            Accruing / Available / PartiallyTaken / Closed / Lapsed
```

`Lapsed` is set by a check, not by a person, and it is the state that means money is now owed twice.

### 3.4 `EmployeeDisciplinaryAction`

```
EmployeeId, EnterpriseId
Type                              Verbal / Written / Suspension
IssuedAt, Reason
AbsenceId                         nullable; set only for Suspension
DocumentKey                       nullable signed-copy scan
```

---

## 4. Ordered backlog

### P1 — Absence spine + atestado, recorded only

`EmployeeAbsence` + `EmployeeMedicalCertificate`, CRUD through `IEmployeeAbsenceService`, migration,
RBAC, LGPD wiring. **Payroll is not touched in this phase** — absences are recorded and visible and
change no money. Ship and use it for a competência before P2 makes it load-bearing.

The CID encryption, the DTO exclusion and the log redaction all land here, not later. Retrofitting
confidentiality onto a column that already has plaintext rows means a data migration.

### P2 — Payroll consumes absences

`GetWorkedDays` stops being a hire/termination intersection and subtracts `UnpaidDays` and
`SocialSecurityPaidDays` for the competência. **This is the phase where a bug costs somebody their
salary**, so it gets the heaviest tests in the module: a fixture per absence type, plus the boundary
cases (absence spanning a month end, absence starting before the hire date, overlapping absences).

Overlap is a real case — an atestado during scheduled férias interrupts the férias — so the service
rejects overlapping approved absences rather than silently summing them.

### P3 — Advertências

Cheapest and fully independent: a table, CRUD, the 30-day art. 474 validation, and a suspension that
writes its own `EmployeeAbsence` row. Slot it wherever it is convenient; nothing waits on it.

### P4 — Férias entitlement ledger

`EmployeeVacationAccrual`, the art. 130 entitlement calculation off P1's absence data, period scheduling
with the art. 134 split rules, and a check that flags accruals approaching `ConcessionDeadline`.

Still no money: scheduling a férias creates an `EmployeeAbsence` of type `Vacation`, and P2 already knows
what to do with an absence.

### P5 — Férias pay

Lives in **Bills, next to `PayrollService`**, not in the Employee module — the precedent is already set,
`PayrollService` reads `IRepository<Employee>` and owns `PayableBill` creation. Employee owns *when* the
férias happen; Bills owns *what they cost*.

> **The trap, stated plainly.** An employee on férias receives salary + 1/3 for those days, paid up front,
> and does **not** also receive ordinary payroll salary for them. A naive implementation creates a
> `PayableBill` for the férias and leaves the competência's payroll untouched, and the company pays the
> salary portion twice. P2's worked-day reduction is what prevents this, which is why P5 cannot ship
> before P2.

Also here: a `Vacation` value on `EnumPayableBillType` (currently `…, Other, Payroll` — append, never
insert, the values go on the wire), the abono pecuniário amount, and the payment-date rule (2 days before
start), which drives the `PayableBill.DueDate`.

**Open question for the accountant, not for us:** `VacationProvision` on `PayrollBillDetail` accrues every
month, and P5 pays the férias out. Whether the payment should *consume* the accrued provision or whether
the provision stays a pure forecast input is an accounting decision. It changes the cash-flow forecast,
not the cash. Ask before building, because both are defensible and only one is theirs.

---

## 5. Layering

| Piece | Project |
|---|---|
| `EmployeeAbsence` / `EmployeeMedicalCertificate` / `EmployeeVacationAccrual` / `EmployeeDisciplinaryAction` entities | `Shared/Entities` |
| `IEmployeeAbsenceService`, `IEmployeeVacationService`, `IEmployeeDisciplineService` + impls | `Employee.Application` |
| Absence and accrual queries | `Employee.Domain/Repositories` (no `Employee.Infra` exists — do not create one for this) |
| Férias → `PayableBill` | `Bills.Application`, beside `PayrollService` |
| Controllers | `Web/Controllers/EmployeeAbsenceController.cs`, `EmployeeVacationController.cs` |

Namespaces use the `*Module.*` form (`EvangelionERPV2.EmployeeModule.Application`) even though the folders
do not. Existing gotcha, not a new one.

---

## 6. Repo mechanics — the four that break the build if skipped

1. **A new permission goes in four places or startup throws**: `RbacPermissions`, `RbacPolicies`,
   `RbacRolePermissionMap`, and the `.All` set. New permissions needed: `employees.absence.{read,write}`,
   `employees.vacation.{read,schedule}`, `employees.discipline.{read,write}`. Absence and discipline read
   should sit **no lower than Manager** — they expose health data and disciplinary history, the same risk
   class the map already applies to `payroll.*`.
2. **Every new controller action needs an `[InlineData]` row** in `RbacControllerPolicyCoverageTests`.
3. **Migrations are hand-written here, snapshot included.** After each one run the list check — the
   snapshot test does *not* catch a missing `[Migration]` attribute:
   ```bash
   dotnet ef migrations list \
     --project EvangelionERPV2.Shared/EvangelionERPV2.Shared.csproj \
     --startup-project EvangelionERPV2.Web/EvangelionERPV2.Web.csproj --no-connect
   ```
   ⚠ Related known hazard: five migrations in this repo were once invisible to EF, so no environment is
   known to have run the chain end to end. **Verify the real schema before trusting `database update`.**
4. **Log redaction**: add the new routes to the sensitive-endpoint lists in **both**
   `RequestLoggingMiddleware` and `ExceptionHandlingMiddleware`, next to the existing `/Employee/*`
   entries. An atestado body carries a CID.

---

## 7. Tests

Mirror `EmployeeServiceTests`; project is `EvangelionERPV2.EmployeeModule.Test`.

| Test | Why it exists |
|---|---|
| art. 130 entitlement table, one case per band | A step table is exactly where an off-by-one hides. |
| 15-day employer / day-16 INSS split, plus the 60-day relapse | The rule most likely to be implemented from memory and wrong. |
| Suspension > 30 days rejected | art. 474. |
| Split férias: 3 periods, one ≥14, none <5 | art. 134 §1. |
| Worked days with each absence type, and across a month boundary | P2 is the money path. |
| Overlapping approved absences rejected | Prevents double-subtraction of the same day. |
| CID absent from every list/detail DTO | Extend `EmployeeDtoLgpdSerializationTests`, same pattern as `Document`/`Salary`. |
| Concessivo lapse flagged | The dobro liability. |

---

## 8. Deferred — KPIs

Explicitly out of scope, per the 2026-08-14 decision. Recorded only so the schema above does not make
them expensive later:

- Absenteeism, atestado frequency and warnings-per-employee are all `GROUP BY` over `EmployeeAbsence` and
  `EmployeeDisciplinaryAction` as specified. No extra column needed.
- Expiring-férias liability reads `EmployeeVacationAccrual.ConcessionDeadline`. Already there.
- Cost per head reads `PayrollBillDetail`. Already there.
- **Cost or absence *by department* is the one thing that does not work**, because `Employee` has no
  `Department` and no manager link. If that cut ever matters, it is a nullable column on `Employee` and a
  migration — cheap on its own, but not worth adding speculatively today.

---

## 9. Open decisions

| # | Decision | Owner |
|---|---|---|
| H1 | Does the férias payment consume `VacationProvision`, or is the provision purely a forecast input? (§4 P5) | Accountant |
| H2 | Is FGTS still owed during an INSS-covered afastamento? It is for accident-related leave and not for ordinary illness — confirm before P2 changes any charge base. | Accountant |
| H3 | Retention period for advertências and atestados after termination. LGPD wants a defined limit; labour-claim exposure wants five years. | You / legal |
| H4 | Self-service later? If yes, `Employee.UserId` is the first migration and it is cheaper to add before the tables above accumulate rows. | You |

---

## 10. Commands

```bash
dotnet build EvangelionERPV2.sln
dotnet test  EvangelionERPV2.EmployeeModule.Test/EvangelionERPV2.EmployeeModule.Test.csproj
dotnet test  EvangelionERPV2.BillsModule.Test/EvangelionERPV2.BillsModule.Test.csproj   # P5 touches payroll
```

Run the `net-review` skill over the diff before merging — layering, async/EF, security and LGPD.
