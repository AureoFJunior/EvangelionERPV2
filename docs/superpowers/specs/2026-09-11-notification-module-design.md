# Notification module + notification worker — design

Status: design, not yet implemented. Written 2026-09-11.

## Context

Two things the ERP cannot do today:

1. An admin has no way to tell everyone in the tenant anything. "Fechamento é sexta",
   "o sistema fica fora das 22h às 23h", "novo procedimento de conferência". There is
   no channel for a message that is not a document.
2. The seven daily signals computed by `DailyPanelService` are visible only to whoever
   opens the dashboard, and only to the Manager tier that holds `dashboard.read`. An
   Employee never learns that stock is critical; nobody learns that a NFe was rejected
   until they go looking.

This builds one bell that answers both.

### Prior decision this reopens, and why that is legitimate

The M14–M23 roadmap (`~/.claude/plans/let-s-plan-more-needed-scalable-hare.md`) cut the
notification table outright, and `DailyPanelService.cs:20` still carries the reasoning:

> every signal on the list is a derived view of a document that already exists;
> persisting them creates a second copy that goes stale the moment a bill is settled
> outside the app […] A persisted notification earns its place only for an alert with
> no document behind it, and there isn't one.

There is one now. A manual broadcast has no document behind it — it *is* the document.
That is the cut's own stated exception, so the manual half earns a table and the
automatic half does not. This design honours the cut rather than overturning it: **auto
alerts are still never persisted.**

### What already exists, and is reused rather than rebuilt

| Need | Already in the tree |
|---|---|
| Tenant-grouped push | `Shared/Hubs/OrderHub.cs` — groups by `enterpriseId`, gated `rbac:orders.read`, which sits at **Employee** tier so every user reaches it |
| Alert computation | `Bills.Application/Services/DailyPanelService.cs` — seven signals, `IDistributedCache`, 15-min TTL on the expensive one |
| Scheduled cluster-safe worker | `Shared/Utils/LeasedScheduleWorker.cs` + `IDistributedLease` (Redis) + `BroadcastSchedule` |
| Worker → API auth | `SelfAPIAuth` secret + `SelfApiCredential.TryParse` + `SharedFunctions.PostAsync`; workers never touch the database |
| Data access | `Shared/Repositories/Repository<T>` generic, `IUnitOfWork<AppDbContext>` |
| Permission predicate | `Web/Security/PdvTokenScope.Grants(principal, accessLevel, permission)` — the one predicate that is also correct for a PDV-scoped token |

## Decisions

### Two sources, one feed, only one of them persists

| | Manual broadcast | Auto alert |
|---|---|---|
| Storage | `Notification` row | none — projected from `DailyPanelDTO` at read time |
| Author | an Admin writes it | `DailyPanelService` computes it |
| Clears when | the user marks it read | the underlying document is fixed |
| Read state | `NotificationRead` row | not applicable |
| Staleness | none — it is the document | none by construction — recomputed per request |

Persisting auto alerts would rebuild exactly what M15 rejected: a second copy that
survives the bill being settled in the bank's own system. Projecting them per request
costs one `GetTodayAsync` call, and six of its seven signals are already cheap reads
against indexed columns; the seventh (stock criticality, a 180-day ledger scan) is
already cached 15 minutes by `DailyPanelService` itself.

### Broadcasts are one row, not one row per recipient

A broadcast to 50 users writes **one** `Notification`. Read state lives in a separate
`NotificationRead` table that gains a row only when a user actually reads something.
Unread is a `NOT EXISTS` against it.

Rejected: the conventional fanout table (one row per user per notification). Fifty
writes to deliver one sentence, fifty more rows every time an admin posts, and a
backfill problem the moment a user is created after the broadcast.

### Two counters, not one badge

Auto alerts have nothing to mark read — an overdue bill is either overdue or it is not.
A single unread badge that includes them can never reach zero while the company owes
anyone money, which trains people to ignore the badge.

So the feed returns **`UnreadBroadcastCount`** (goes to zero, drives the badge) and
**`ActiveAlertCount`** (a state, rendered as a count beside the alert section, never as
an unread badge).

This is a deliberate deviation from "per-user read state on everything". If alerts ever
need dismissing, the upgrade is a third table keyed `(UserId, AlertKey, ValueHash)` —
hash so that a dismissed "3 overdue" reappears at 4. Not built now: there is no evidence
anyone wants to silence a real overdue bill.

### No new hub, and the push carries no content

`OrderHub` already does everything needed: it groups connections by `enterpriseId`, it
verifies active tenant membership on connect, and its policy is Employee-tier so every
user can hold a connection. A `NotificationHub` would be the same class with a different
name.

The push is **content-free**:

```csharp
await _hub.Clients.Group(enterpriseId.ToString()).SendAsync("NotificationsChanged");
```

That is a security requirement, not brevity. The tenant group contains Employees, while
`payables.overdue` and `bank.uncleared` are Manager-tier data. Sending the payload over
the group would leak it to every till operator with a browser tab open. A ping makes the
client call `GET /Notification/Feed`, which filters against *that caller's* permissions
server-side — the same filtering an HTTP request already gets.

### Permission filtering happens in the Web layer

`RbacRolePermissionMap` and `PdvTokenScope` live in `Web/Security` and cannot be
referenced from an Application project without inverting the dependency. So:

- `NotificationService` returns every item, each carrying a `RequiredPermission` string
  (null = everyone in the tenant).
- `NotificationController` resolves the caller's `AccessLevel` once, filters with
  `PdvTokenScope.Grants`, and strips `RequiredPermission` before responding.

One `GetByIdAsync(userId)` per feed call, not nine `IAuthorizationService` round trips.

### Module shape: Application + Test only

`EvangelionERPV2.Notification.Application` and `EvangelionERPV2.NotificationModule.Test`.
**No `.Domain`, no `.Infra`.** The generic `Repository<T>` covers both tables and there
are no module-specific domain contracts to hold. Precedent in the tree:
`Pdv.Application` and `Ai.Application` ship this way; `Employee` and `Supplier` have no
`.Infra`.

`Notification.Application` references `Bills.Application` for `IDailyPanelService`. A
cross-module Application reference, which `EmailIoC` already does with `OrderService`.
The alternative — an `INotificationAlertSource` in the notification module implemented
once, by Bills — is an interface with one implementation and one consumer, bought at the
price of an extra indirection in both modules.

### The worker is its own project

`EvangelionERPV2.Worker.Notification`, not a fourth `AddHostedService` in
`Worker.Email`. The sweep runs every 15 minutes and drives the bell every user is
looking at; the email broadcasts run weekly and monthly. Different cadence, different
criticality, and separate images mean the alert sweep can be scaled or restarted without
touching the mail path. Cost is one `Dockerfile` and one compose service, both copied
from `Worker.Email`.

## Data model

Both entities go in `EvangelionERPV2.Shared/Entities` — the house rule is that shared
entities live there, not in a module's `.Domain`.

```csharp
[Index(nameof(EnterpriseId), nameof(IsActive), nameof(CreatedAt))]
public class Notification : BaseEntity
{
    [ForeignKey(nameof(Enterprise))]
    public Guid EnterpriseId { get; set; }
    public Enterprise? Enterprise { get; set; }

    [MaxLength(120)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Body { get; set; } = string.Empty;

    public EnumNotificationSeverity Severity { get; set; } = EnumNotificationSeverity.Info;

    [ForeignKey(nameof(CreatedByUser))]
    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    /// <summary>Null means it stays in the feed until an admin deletes it.</summary>
    public DateTime? ExpiresAt { get; set; }
}
```

```csharp
[Index(nameof(NotificationId), nameof(UserId), IsUnique = true)]
[Index(nameof(UserId))]
public class NotificationRead : BaseEntity
{
    [ForeignKey(nameof(Notification))]
    public Guid NotificationId { get; set; }
    public Notification? Notification { get; set; }

    [ForeignKey(nameof(User))]
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}
```

`EvangelionERPV2.Shared/Enums/EnumNotificationSeverity.cs`:

```csharp
public enum EnumNotificationSeverity { Info = 0, Warning = 1, Critical = 2 }
```

`Notification` declares `EnterpriseId`, so `AppDbContext.ApplyTenantQueryFilters` picks
it up by convention and tenant isolation costs nothing. `NotificationRead` deliberately
does **not** declare one: it is scoped through its `Notification`, and a second filter
on the join side would be a second copy of the same truth.

A broadcast is deleted by setting `IsActive = false`, matching every other soft delete in
the solution. `NotificationRead` rows are left alone.

Migration is hand-written plus a snapshot edit, the same way the payroll tables were
added. Nothing in the app calls `Database.Migrate()`, so it is applied with
`./scripts/local-db-update.sh`.

## Alert map

A pure static function, `NotificationAlerts.From(DailyPanelDTO panel, DateTime utcNow)`,
returning `IReadOnlyList<NotificationItemDTO>`. No I/O, no dependencies, trivially
testable — which is the point, because every boundary below is a real bug someone will
otherwise ship.

| Key | Fires when | Severity | `RequiredPermission` |
|---|---|---|---|
| `receivables.overdue` | `Receivables.OverdueCount > 0` | Warning | `dashboard.read` |
| `receivables.dueToday` | `Receivables.DueTodayCount > 0` | Info | `dashboard.read` |
| `payables.overdue` | `Payables.OverdueCount > 0` | Critical | `dashboard.read` |
| `payables.dueToday` | `Payables.DueTodayCount > 0` | Warning | `dashboard.read` |
| `stock.critical` | `Stock.CriticalCount > 0` | Warning | `products.read` |
| `nfe.rejected` | `Fiscal.RejectedCount > 0` | Critical | `nfe.read` |
| `nfe.pending` | `Fiscal.PendingCount > 0` | Warning | `nfe.read` |
| `nfe.outcomeUnknown` | `Fiscal.OutcomeUnknownCount > 0` | Critical | `nfe.read` |
| `fiscal.certExpiring` | `Fiscal.CertificateDaysRemaining is int d && d <= 30` | Critical | `nfe.certificate.read` |
| `bank.uncleared` | `Bank.UnclearedCount > 0` | Info | `bank-reconciliation.read` |

Rules the tests pin:

- A count of exactly `0` produces no item. Never an item that says "0 overdue".
- `CertificateDaysRemaining == null` produces **no** alert. It means no certificate is
  configured, which is not the same as an expired one, and the panel already says so
  through `CertificateConfigured`.
- `CertificateDaysRemaining == 30` fires; `31` does not.
- A negative `CertificateDaysRemaining` fires, with the title saying expired rather than
  "expires in -4 days".

Titles and bodies are Portuguese, formatted from the counts and amounts, matching the
language of the panel this mirrors.

## API surface

`EvangelionERPV2.Web/Controllers/NotificationController.cs`, standard versioned route
`api/v{version}/Notification/{action}`.

| Action | Verb | Policy | Behaviour |
|---|---|---|---|
| `Feed` | GET | `notifications.read` | Active, unexpired broadcasts for the tenant + projected alerts, permission-filtered, newest first |
| `MarkRead` | POST `{id}` | `notifications.read` | Idempotent upsert of one `NotificationRead` |
| `MarkAllRead` | POST | `notifications.read` | One read row per unread broadcast, in a single transaction |
| `Broadcast` | POST | `notifications.broadcast` | Creates the row, pushes `NotificationsChanged` |
| `Broadcast` | DELETE `{id}` | `notifications.broadcast` | Soft delete, pushes `NotificationsChanged` |
| `SweepAlerts` | POST | `notifications.sweep` | Worker-facing; walks tenants and pushes where the alert set changed |

### DTOs

```csharp
public class NotificationFeedDTO
{
    public List<NotificationItemDTO> Items { get; set; } = [];
    public int UnreadBroadcastCount { get; set; }
    public int ActiveAlertCount { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
}

public class NotificationItemDTO
{
    /// <summary>Set for a broadcast, null for an alert — an alert has no row.</summary>
    public Guid? Id { get; set; }

    /// <summary>"broadcast" or "alert".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Stable key for an alert ("payables.overdue"), null for a broadcast.</summary>
    public string? Key { get; set; }

    public EnumNotificationSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Always false for an alert.</summary>
    public bool IsRead { get; set; }

    /// <summary>Never serialised — the controller filters on it, then clears it.</summary>
    [JsonIgnore]
    public string? RequiredPermission { get; set; }
}

public class CreateBroadcastDTO
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public EnumNotificationSeverity Severity { get; set; } = EnumNotificationSeverity.Info;
    public DateTime? ExpiresAt { get; set; }
}
```

`CreateBroadcastDTO` gets a FluentValidation validator in `Web/FluentValidator`: title
1–120 characters, body 1–2000, `ExpiresAt` either null or in the future, severity a
defined enum value.

## Security and RBAC

Three new permissions in `RbacPermissions`, three matching policies in `RbacPolicies`,
and the role map updated:

```csharp
public static class Notifications
{
    // Employee tier. The feed is the bell every user carries; its *contents* are
    // filtered per item against the caller's own permissions, so the permission to
    // open it grants nothing the caller could not already read elsewhere.
    public const string Read = "notifications.read";

    // Admin only, alongside payroll.* and nfe.certificate.manage. A broadcast is an
    // unsolicited message to every person in the company with the company's authority
    // behind it. There is no document to check it against and no approval step, which
    // is exactly why it does not belong at Manager.
    public const string Broadcast = "notifications.broadcast";

    // Admin only, and reached only by the worker's SelfAPIAuth identity. It computes
    // every tenant's panel in one call, so it is the one endpoint here that crosses the
    // tenant boundary by design.
    public const string Sweep = "notifications.sweep";
}
```

All three are listed in `RbacPermissions.All`, which is the master list and also the
Admin role's permission set. `Notifications.Read` is additionally appended to
`EmployeePermissions`, so Supervisor and Manager inherit it through the existing
`..EmployeePermissions` spread. `Broadcast` and `Sweep` appear in no role array, which
is what leaves them reachable only through `All` — Admin.

Three further points:

- **`SweepAlerts` is the only cross-tenant surface.** It iterates enterprises and must
  therefore bypass the tenant query filter to enumerate them. It reads aggregates,
  writes nothing, and returns a count — never a per-tenant payload. It is also why it
  carries its own permission rather than reusing `notifications.broadcast`: the
  worker's credential should not be able to post a message to every company.
- **The hub push is content-free** (see Decisions), so the SignalR path leaks nothing
  regardless of who holds a connection.
- **LGPD:** neither table stores personal data. `CreatedByUserId` is an internal
  identifier already present on every audited row, and a broadcast body is company
  communication authored by an admin, not data about a person. Auto alerts are counts
  and amounts, identical in kind to what `DashboardController.Today` already returns.

New `[InlineData]` rows in `RbacControllerPolicyCoverageTests` for all six actions.

## Worker

`EvangelionERPV2.Worker.Notification`, copied in shape from `Worker.Email`:
`Program.cs`, `NotificationLogConfig.cs`, `NotificationWorker/NotificationSweepWorker.cs`,
`Dockerfile`, `appsettings*.json`.

```csharp
public sealed class NotificationSweepWorker : LeasedScheduleWorker
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);
    private const int MaxAttempts = 3;

    public override string LeaseKey => "eva:lease:notification-sweep";

    public override TimeSpan TimeUntilNextRun(DateTime utcNow) => SweepInterval;

    protected override async Task RunPassAsync(CancellationToken stoppingToken)
    {
        // Log in with SelfAPIAuth, POST Notification/SweepAlerts, retry up to MaxAttempts.
        // Identical to EmailStockSenderWorker.RunPassAsync — no database access here.
    }
}
```

Fifteen minutes because that is the TTL of the panel's own stock cache: sweeping faster
re-serves the same cached numbers and buys nothing. `LeasedScheduleWorker` already
handles the Redis lease, the "a failed pass must not kill the host" rule, and not
running on startup.

### `SweepAlerts` on the API side

For each active enterprise:

1. `GetTodayAsync(enterpriseId)` → `NotificationAlerts.From(...)`.
2. Hash the ordered firing keys (`string.Join('|', keys)`, SHA-256, hex).
3. Compare against `IDistributedCache` key `eva:notif:alerts:{enterpriseId}`.
4. If different: store the new hash (24h TTL) and push `NotificationsChanged` to that
   tenant's group.

**No database writes.** A tenant whose alert set has not changed since the last sweep
costs one panel read and one Redis get.

```
// ponytail: sequential per-tenant loop and a 15-minute floor. At ~50 tenants that is
// seconds; parallelise or shard the lease if the sweep ever outruns its slot.
```

Hashing the key set, not the counts, is deliberate: an overdue count moving from 3 to 4
is the same alert and should not re-ping every open browser in the company. The count
the user sees is correct regardless, because the feed recomputes it per request.

### Compose and configuration

A `worker_notification` service under the existing `workers` profile, environment copied
from `worker_email` minus the RabbitMQ block — this worker speaks only HTTP to the API
and Redis for the lease. It needs `HttpConfig__DefaultApiUrl`, `SelfAPIAuth`,
`Encryption__Key`, `RedisSettings__ConnectionString` and the AWS block.

`scripts/local-up.ps1` and `local-down.ps1` keep working unchanged; the worker is
off unless `--profile workers`.

## Wiring

- `NotificationIoC.Configure` registers `IRepository<Notification>`,
  `IRepository<NotificationRead>`, `INotificationService`, and takes
  `IDailyPanelService` from `BillsIoC` (already registered).
- `NotificationIoC.Configure(builder.Services, builder.Configuration)` added to
  `ConfigureIoC` in `Web/Program.cs`, after `BillsIoC`.
- `Notification` and `NotificationRead` `DbSet`s on `AppDbContext`.
- Both projects added to `EvangelionERPV2.sln`.

## Verification

`EvangelionERPV2.NotificationModule.Test`, xUnit + Moq, matching the existing module
test projects.

**Alert map** (pure, no mocks):
- zero counts produce an empty list
- `CertificateDaysRemaining == null` produces no item, and specifically not an expiry one
- exactly 30 days fires, 31 does not, negative fires with expired wording
- every item carries a non-null `RequiredPermission` and a unique `Key`

**Feed:**
- an Employee-tier caller sees `stock.critical` but no `payables.overdue`
- an Admin sees every alert
- an expired broadcast is excluded; one expiring tomorrow is included
- a soft-deleted broadcast is excluded
- `UnreadBroadcastCount` excludes broadcasts the caller has read and counts nothing else
- `ActiveAlertCount` never contributes to `UnreadBroadcastCount`

**Read state:**
- marking read twice writes one row, second call is a no-op returning success
- one user reading does not change another user's unread count
- `MarkAllRead` on an empty feed writes nothing

**Sweep:**
- unchanged hash → no push, no write
- changed hash → exactly one push to that tenant's group, and the new hash stored
- a tenant that throws does not abort the remaining tenants

**Worker:**
- `RunOnceUnderLeaseAsync` returns false and does not call the API when the lease is held
- a failing pass releases the lease

**Security:**
- the six new `[InlineData]` rows in `RbacControllerPolicyCoverageTests`
- a test asserting `NotificationItemDTO.RequiredPermission` is `[JsonIgnore]`, so a
  refactor cannot start serialising the permission gate to the client

## Non-goals

Named because each was considered and rejected, not because nobody thought of them.

| Not built | Why |
|---|---|
| Persisted auto alerts | The M15 cut, unchanged: the document is the source of truth and settling it is what clears the alert |
| Email digest of notifications | `Worker.Email` already sends the weekly stock and monthly order digests through `EmailService`; a second digest of the same numbers is a second thing to keep in sync |
| Web push / VAPID | A service worker, a subscription table, and a key rotation story, for users who already have the tab open |
| Per-user or per-role broadcast targeting | The answer to the question asked was "broadcast to tenant". Targeting adds an audience column and a recipient picker for a need nobody has stated |
| Replies, threads, reactions | That is internal messaging, a different product |
| Per-user notification preferences | A preferences table needs something to turn off; with one broadcast channel and self-clearing alerts there is nothing yet |
| A `NotificationHub` | `OrderHub` already groups by tenant at Employee tier |
| Alert dismissal | See Decisions — the upgrade path is written down, the trigger is someone actually asking |

## Open questions

1. **Broadcast retention.** Nothing prunes `Notification` or `NotificationRead` today.
   `AuditTrailController.CleanupRetention` is the existing pattern for this; worth
   copying once the table has real volume, not before.
2. **FrontV2.** A bell in the header, a dropdown, and a `NotificationsChanged` handler on
   the existing SignalR connection. Out of scope for this spec, which is backend only.
3. **Sweep interval per environment.** Hard-coded at 15 minutes here. Config-bind it if
   a tenant ever wants faster, but not before one does.
