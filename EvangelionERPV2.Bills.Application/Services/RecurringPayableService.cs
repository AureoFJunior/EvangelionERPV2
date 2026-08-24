using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Serilog;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// Contas fixas: the bill that repeats, cloned forward one link at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cross-tenant, like the dunning sweep: it runs outside a request, where
    /// <c>AppDbContext.AmbientEnterpriseId</c> is <see cref="Guid.Empty"/> and the tenant filter is
    /// disabled by design. Every query here is scoped explicitly instead.
    /// </para>
    /// <para>
    /// One link per pass, never a whole year up front. A chain generated in advance is a year of
    /// bills to correct when the rent changes; one link means the next bill exists as soon as the
    /// current one falls due, and the human who edits it edits one row. The consequence is that a
    /// long-dormant chain catches up a month per day, which is the right speed for a sweeper
    /// nobody is watching.
    /// </para>
    /// <para>
    /// Idempotency is the filtered unique index on <c>RecurrenceParentId</c>, not a timestamp: a
    /// parent has at most one child, so two passes on the same day cannot double-bill.
    /// </para>
    /// </remarks>
    public class RecurringPayableService
    {
        private readonly IRepository<PayableBill> _payableBillRepository;
        private readonly IRepository<Enterprise> _enterpriseRepository;
        private readonly IPayableBillService _payableBillService;

        public RecurringPayableService(
            IRepository<PayableBill> payableBillRepository,
            IRepository<Enterprise> enterpriseRepository,
            IPayableBillService payableBillService)
        {
            _payableBillRepository = payableBillRepository;
            _enterpriseRepository = enterpriseRepository;
            _payableBillService = payableBillService;
        }

        /// <summary>Runs one pass over every chain that is due to advance.</summary>
        /// <returns>How many bills were generated.</returns>
        public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
        {
            var today = DateTime.UtcNow.Date;

            // A parent becomes eligible the day it falls due: the next bill should be visible from
            // the moment the current one has to be paid, not a month later.
            var parents = (await _payableBillRepository.GetAllAsync(
                    x => x.RecurrenceMonths != null
                         && x.RecurrenceMonths > 0
                         && (x.IsActive == null || x.IsActive == true)
                         && x.RefundedAt == null
                         && x.DueDate <= today))
                .ToList();

            if (parents.Count == 0)
                return 0;

            // One read of the chains already spawned, rather than a query per parent. Inactive
            // children count: a soft-deleted clone still means this link was generated, and
            // regenerating it would bring back a bill somebody deleted on purpose.
            var alreadySpawned = (await _payableBillRepository.GetAllAsync(x => x.RecurrenceParentId != null))
                .Select(x => x.RecurrenceParentId!.Value)
                .ToHashSet();

            var closedThroughByEnterprise = new Dictionary<Guid, DateTime?>();
            var generated = 0;

            foreach (var parent in parents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (alreadySpawned.Contains(parent.Id))
                    continue;

                var nextDueDate = parent.DueDate.Date.AddMonths(parent.RecurrenceMonths!.Value);

                if (parent.RecurrenceUntilUtc.HasValue && nextDueDate > parent.RecurrenceUntilUtc.Value.Date)
                    continue;

                if (!closedThroughByEnterprise.TryGetValue(parent.EnterpriseId, out var closedThrough))
                {
                    var enterprise = await _enterpriseRepository.GetByIdAsync(parent.EnterpriseId);
                    closedThrough = enterprise?.ClosedThroughUtc;
                    closedThroughByEnterprise[parent.EnterpriseId] = closedThrough;
                }

                // Checked here as well as inside the create, so a closed month skips the chain
                // quietly instead of throwing through the sweep. The chain resumes on its own once
                // the lock moves or the next link lands past it.
                if (closedThrough.HasValue && nextDueDate <= closedThrough.Value.Date)
                {
                    Log.Logger.Information(
                        "Recurring payable skipped: the period is closed. PayableBillId={PayableBillId}",
                        parent.Id);

                    continue;
                }

                try
                {
                    await _payableBillService.CreateAsync(BuildNextLink(parent, nextDueDate));
                    generated++;
                }
                catch (PeriodClosedException)
                {
                    // Lost a race with somebody closing the books mid-sweep. Same answer as above.
                    Log.Logger.Information(
                        "Recurring payable skipped: the period closed mid-sweep. PayableBillId={PayableBillId}",
                        parent.Id);
                }
                catch (Exception ex)
                {
                    // One broken chain must not cost the rest of the installation its sweep.
                    Log.Logger.Error(
                        "Recurring payable failed. PayableBillId={PayableBillId} ErrorType={ErrorType}",
                        parent.Id, ex.GetType().Name);
                }
            }

            return generated;
        }

        /// <summary>
        /// The clone: same description, amount, kind and supplier, one period later, unpaid.
        /// </summary>
        /// <remarks>
        /// Item lines are deliberately not carried over. Contas fixas are rent, internet and the
        /// accountant — none of them deliver goods, and cloning lines would tell the stock ledger a
        /// delivery is pending every month.
        /// </remarks>
        private static PayableBill BuildNextLink(PayableBill parent, DateTime nextDueDate) => new()
        {
            EnterpriseId = parent.EnterpriseId,
            SupplierId = parent.SupplierId,
            Description = parent.Description,
            Amount = parent.Amount,
            BillType = parent.BillType,
            DueDate = nextDueDate,
            IsPaid = false,
            RecurrenceMonths = parent.RecurrenceMonths,
            RecurrenceUntilUtc = parent.RecurrenceUntilUtc,
            RecurrenceParentId = parent.Id
        };
    }
}
