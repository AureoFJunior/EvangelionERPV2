using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;

using Serilog;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// Keeps <see cref="PayrollBillDetail.VacationProvisionConsumed"/> in step with what has
    /// actually been paid out against one período aquisitivo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The accounting decision behind this is recorded in <c>docs/employee/SPEC-hr-module.md</c>
    /// §9 H1: a férias payment <b>consumes</b> the provision that payroll accrued for it, rather
    /// than the provision staying a pure forecast input.
    /// </para>
    /// <para>
    /// It recomputes the whole allocation from every férias bill on the accrual rather than
    /// applying one payment's delta. That is deliberate and it is the entire idempotency story:
    /// there is no "already applied" flag to keep honest, running it twice lands on the same
    /// numbers, and a bill that is later refunded or deactivated releases its share the next time
    /// it runs. An incremental version would need a ledger of its own to be correct about any of
    /// that.
    /// </para>
    /// </remarks>
    public sealed class VacationProvisionDrawdownService
    {
        private readonly IRepository<EmployeeVacationAccrual> _accrualRepository;
        private readonly IRepository<PayableBill> _payableBillRepository;
        private readonly IRepository<PayrollBillDetail> _payrollDetailRepository;

        public VacationProvisionDrawdownService(
            IRepository<EmployeeVacationAccrual> accrualRepository,
            IRepository<PayableBill> payableBillRepository,
            IRepository<PayrollBillDetail> payrollDetailRepository)
        {
            _accrualRepository = accrualRepository;
            _payableBillRepository = payableBillRepository;
            _payrollDetailRepository = payrollDetailRepository;
        }

        /// <summary>
        /// Recalculates the drawdown for one accrual from every férias bill currently standing
        /// against it, and writes the result.
        /// </summary>
        /// <remarks>
        /// Call it after creating, refunding or deactivating a férias bill. Calling it more often
        /// than necessary is harmless.
        /// </remarks>
        public async Task<ProvisionDrawdown> RecalculateAsync(Guid accrualId, CancellationToken cancellationToken = default)
        {
            var accrual = await _accrualRepository.GetByIdAsync(accrualId)
                ?? throw new InvalidOperationException($"Vacation accrual {accrualId} was not found.");

            var bills = await StandingBillsAsync(accrualId);
            var competencias = await CompetenciasAsync(accrual);

            // The gross, never the bills' Amount: Amount already carries the netted figure this
            // method wrote last time, and allocating against it would feed on its own output.
            var paidTotal = bills.Sum(GrossOf);

            var drawdown = VacationProvisionAllocator.Allocate(paidTotal, competencias);
            var byId = competencias.ToDictionary(x => x.Id);
            var now = DateTime.UtcNow;
            var changed = new List<PayrollBillDetail>();

            foreach (var allocation in drawdown.Allocations)
            {
                var detail = byId[allocation.Id];
                if (detail.VacationProvisionConsumed == allocation.Consumed)
                    continue;

                detail.VacationProvisionConsumed = allocation.Consumed;
                detail.UpdatedAt = now;
                changed.Add(detail);
            }

            if (changed.Count > 0)
            {
                _payrollDetailRepository.UpdateRange(changed);
                await _payrollDetailRepository.CommitAsync(cancellationToken);
            }

            await NetBillsAsync(bills, drawdown.TotalConsumed, now, cancellationToken);

            if (drawdown.Shortfall > 0)
            {
                // Information, not Warning: an employee hired part-way through the período
                // aquisitivo, or one who has had a raise since, produces this every time. The money
                // is owed regardless — it simply was never set aside.
                Log.Logger.Information(
                    "Férias payment for accrual {AccrualId} exceeds the provision accrued for it by {Shortfall}; " +
                    "{Consumed} of {PaidTotal} was covered.",
                    accrualId,
                    drawdown.Shortfall,
                    drawdown.TotalConsumed,
                    paidTotal);
            }

            return drawdown;
        }

        /// <summary>Every férias bill currently standing against this accrual, oldest due first.</summary>
        /// <remarks>
        /// Refunded and deactivated bills are excluded so that reversing one releases the provision
        /// it was holding, which is the behaviour that makes recomputation the right shape here.
        /// </remarks>
        private async Task<List<PayableBill>> StandingBillsAsync(Guid accrualId)
        {
            var bills = await _payableBillRepository.GetAllAsync(
                x => x.EmployeeVacationAccrualId == accrualId
                     && x.BillType == (int)EnumPayableBillType.Vacation
                     && (x.IsActive == null || x.IsActive == true)
                     && x.RefundedAt == null);

            // Id breaks the tie so two stretches sharing a due date still net deterministically.
            return [.. bills.OrderBy(x => x.DueDate).ThenBy(x => x.Id)];
        }

        /// <summary>
        /// Writes each bill's netted <see cref="PayableBill.Amount"/>: its gross less the share of
        /// the provision it consumed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The decision is in docs/employee/SPEC-hr-module.md §4 P5a — the monthly payroll bill's
        /// Amount already includes the provision as cash leaving, so a férias bill charged at gross
        /// would project the same money twice. Netting here rather than at bill creation is what
        /// keeps it true afterwards: refunding one bill releases its provision, and the next run
        /// re-nets whatever is left standing.
        /// </para>
        /// <para>
        /// Oldest due date first, for the same reason the competências fill oldest first — the
        /// provision that exists today belongs to the payment that falls due first.
        /// </para>
        /// </remarks>
        private async Task NetBillsAsync(
            List<PayableBill> bills,
            decimal totalConsumed,
            DateTime now,
            CancellationToken cancellationToken)
        {
            if (bills.Count == 0)
                return;

            var offsets = VacationProvisionAllocator.Allocate(
                totalConsumed,
                [.. bills.Select(x => (x.Id, GrossOf(x)))]);

            var byId = bills.ToDictionary(x => x.Id);
            var changed = new List<PayableBill>();

            foreach (var offset in offsets.Allocations)
            {
                var bill = byId[offset.Id];
                var gross = GrossOf(bill);
                var netted = gross - offset.Consumed;

                if (bill.GrossAmount == gross && bill.Amount == netted)
                    continue;

                // Backfills the gross on a bill issued before the column existed, whose Amount was
                // the gross by definition.
                bill.GrossAmount = gross;
                bill.Amount = netted;
                bill.UpdatedAt = now;
                changed.Add(bill);
            }

            if (changed.Count == 0)
                return;

            _payableBillRepository.UpdateRange(changed);
            await _payableBillRepository.CommitAsync(cancellationToken);
        }

        /// <summary>What the employee is actually paid for this bill, before any netting.</summary>
        private static decimal GrossOf(PayableBill bill) => bill.GrossAmount ?? bill.Amount;

        /// <summary>
        /// The payroll competências whose provision this accrual is entitled to draw on.
        /// </summary>
        /// <remarks>
        /// The months the entitlement was <em>earned</em> in — AcquisitionStart..AcquisitionEnd —
        /// not the months the leave is taken in. Compared as a single year*12+month ordinal so the
        /// range stays one SQL predicate instead of a client-side date rebuild per row.
        /// </remarks>
        private async Task<List<PayrollBillDetail>> CompetenciasAsync(EmployeeVacationAccrual accrual)
        {
            var from = (accrual.AcquisitionStart.Year * 12) + accrual.AcquisitionStart.Month;
            var to = (accrual.AcquisitionEnd.Year * 12) + accrual.AcquisitionEnd.Month;

            var details = await _payrollDetailRepository.GetAllAsync(
                x => x.EmployeeId == accrual.EmployeeId
                     && (x.IsActive == null || x.IsActive == true)
                     && ((x.ReferenceYear * 12) + x.ReferenceMonth) >= from
                     && ((x.ReferenceYear * 12) + x.ReferenceMonth) <= to);

            return [.. details];
        }
    }
}
