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

            var parents = (await _payableBillRepository.GetAllAsync(
                    x => x.RecurrenceMonths != null
                         && x.RecurrenceMonths > 0
                         && (x.IsActive == null || x.IsActive == true)
                         && x.RefundedAt == null
                         && x.DueDate <= today
                         && (x.RecurrenceUntilUtc == null || x.RecurrenceUntilUtc >= today)))
                .ToList();

            if (parents.Count == 0)
                return 0;

            var alreadySpawned = (await _payableBillRepository.GetAllAsync(
                    x => x.RecurrenceParentId != null
                         && (x.RecurrenceUntilUtc == null || x.RecurrenceUntilUtc >= today)))
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
                    Log.Logger.Information(
                        "Recurring payable skipped: the period closed mid-sweep. PayableBillId={PayableBillId}",
                        parent.Id);
                }
                catch (Exception ex)
                {
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
