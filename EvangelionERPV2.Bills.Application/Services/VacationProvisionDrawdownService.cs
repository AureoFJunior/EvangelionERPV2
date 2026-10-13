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
        public async Task<ProvisionDrawdown> RecalculateAsync(Guid accrualId, CancellationToken cancellationToken = default)
        {
            var accrual = await _accrualRepository.GetByIdAsync(accrualId)
                ?? throw new InvalidOperationException($"Vacation accrual {accrualId} was not found.");

            var bills = await StandingBillsAsync(accrualId);
            var competencias = await CompetenciasAsync(accrual);

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
        private async Task<List<PayableBill>> StandingBillsAsync(Guid accrualId)
        {
            var bills = await _payableBillRepository.GetAllAsync(
                x => x.EmployeeVacationAccrualId == accrualId
                     && x.BillType == (int)EnumPayableBillType.Vacation
                     && (x.IsActive == null || x.IsActive == true)
                     && x.RefundedAt == null);

            return [.. bills.OrderBy(x => x.DueDate).ThenBy(x => x.Id)];
        }

        /// <summary>
        /// Writes each bill's netted <see cref="PayableBill.Amount"/>: its gross less the share of
        /// the provision it consumed.
        /// </summary>
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
