using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// Turns a scheduled férias into money: one <see cref="PayableBill"/> per stretch, due before
    /// the leave starts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lives in Bills rather than in the Employee module because Employee owns <em>when</em> the
    /// férias happen and Bills owns <em>what they cost</em> — the same split
    /// <see cref="PayrollService"/> already follows.
    /// </para>
    /// <para>
    /// One bill per stretch, not per accrual. CLT art. 145 makes the payment due two days before the
    /// leave starts, and art. 134 §1 allows the leave to be split into three; a single bill would
    /// have no honest due date for the second and third.
    /// </para>
    /// <para>
    /// This is only safe because <see cref="PayrollService"/> stops paying ordinary salary for
    /// férias days. If that ever regresses the company pays the salary portion twice — see the
    /// blockquote in docs/employee/SPEC-hr-module.md §4 P5.
    /// </para>
    /// </remarks>
    public class VacationPayService : IVacationPayService
    {
        private readonly IRepository<EmployeeVacationAccrual> _accrualRepository;
        private readonly IRepository<Employee> _employeeRepository;
        private readonly IRepository<EmployeeAbsence> _absenceRepository;
        private readonly IRepository<PayableBill> _payableBillRepository;
        private readonly IPayableBillService _payableBillService;
        private readonly VacationProvisionDrawdownService _drawdownService;

        public VacationPayService(
            IRepository<EmployeeVacationAccrual> accrualRepository,
            IRepository<Employee> employeeRepository,
            IRepository<EmployeeAbsence> absenceRepository,
            IRepository<PayableBill> payableBillRepository,
            IPayableBillService payableBillService,
            VacationProvisionDrawdownService drawdownService)
        {
            _accrualRepository = accrualRepository;
            _employeeRepository = employeeRepository;
            _absenceRepository = absenceRepository;
            _payableBillRepository = payableBillRepository;
            _payableBillService = payableBillService;
            _drawdownService = drawdownService;
        }

        public async Task<IEnumerable<VacationPayDTO>> GenerateAsync(Guid enterpriseId, Guid accrualId)
        {
            var (accrual, employee) = await LoadAsync(enterpriseId, accrualId);

            var stretches = await ScheduledStretchesAsync(accrual);
            if (stretches.Count == 0)
                throw new InsertDatabaseException("No férias have been scheduled against this período aquisitivo yet.");

            var existingBills = await ExistingBillsAsync(enterpriseId, accrualId);

            // What is still owed on the abono: everything sold, less what the bills already
            // standing against this accrual carry. Days sold *after* the first bill used to fall
            // out here — the abono rode that bill or nothing — and were never paid.
            // ponytail: days sold after every scheduled stretch is already billed still have no
            // bill to ride. Paying them needs a stretchless férias bill, which is a document with
            // no due date rule of its own; the days stay visible on the accrual until one exists.
            var settledDays = existingBills.Sum(bill => SettledAbonoDaysOn(bill, accrual, employee, stretches));
            var abonoDays = Math.Max(0, accrual.SoldDays - settledDays);

            var pending = new List<(EmployeeAbsence Stretch, int SoldDays, VacationPayAmounts Amounts)>();

            foreach (var stretch in stretches)
            {
                var dueDate = VacationPay.DueDateFor(stretch.StartDate);
                if (existingBills.Any(x => x.DueDate.Date == dueDate))
                    continue;

                // Only the first stretch this run bills carries the abono.
                var soldDays = pending.Count == 0 ? abonoDays : 0;
                pending.Add((stretch, soldDays, VacationPay.For(
                    employee.Salary!.Value, DaysOf(stretch), soldDays, stretch.StartDate)));
            }

            if (pending.Count == 0)
                return await GetAsync(enterpriseId, accrualId);

            // One transaction for the whole accrual: a split férias billed halfway through would
            // leave the employee paid for one stretch and not the next, with no record of which.
            await _payableBillRepository.ExecuteInTransactionAsync(async () =>
            {
                foreach (var (stretch, soldDays, amounts) in pending)
                {
                    await _payableBillService.CreateAsync(new PayableBill
                    {
                        // Recorded, not inferred: the next run has to know these days are paid for
                        // even after more are sold against the same período.
                        AbonoDaysSettled = soldDays,

                        Description = $"Férias {stretch.StartDate:dd/MM/yyyy} a {stretch.EndDate:dd/MM/yyyy} - {employee.Name}",
                        DueDate = VacationPay.DueDateFor(stretch.StartDate),
                        // Amount is written gross here and netted by the drawdown below, which is
                        // the only place that knows how much provision this accrual has left to
                        // give — see docs/employee/SPEC-hr-module.md §4 P5a.
                        Amount = amounts.Total,
                        GrossAmount = amounts.Total,
                        IsPaid = false,
                        BillType = (int)EnumPayableBillType.Vacation,
                        EnterpriseId = enterpriseId,

                        // The back-link that makes the provision drawdown auditable: without it,
                        // "which competências did this payment consume" is a guess.
                        EmployeeVacationAccrualId = accrual.Id
                    }, useAmbientTransaction: true);
                }

                // Inside the transaction, after every bill exists: the drawdown recomputes from the
                // bills standing against this accrual, so it has to see all of them or none. It is
                // idempotent, so a retry of this whole block lands on the same numbers.
                await _drawdownService.RecalculateAsync(accrual.Id);
            });

            // Read back rather than reporting what was written: the drawdown nets every standing
            // bill's Amount, including bills this call did not create, so the amounts in memory
            // before it ran are stale.
            return await GetAsync(enterpriseId, accrualId);
        }

        public async Task<IEnumerable<VacationPayDTO>> GetAsync(Guid enterpriseId, Guid accrualId)
        {
            var (accrual, employee) = await LoadAsync(enterpriseId, accrualId);

            var bills = await ExistingBillsAsync(enterpriseId, accrualId);
            if (bills.Count == 0)
                return [];

            var stretches = await ScheduledStretchesAsync(accrual);

            return bills
                .Select(bill =>
                {
                    // The due date is start - 2 days by construction, so it identifies the stretch.
                    var stretch = stretches.FirstOrDefault(x => VacationPay.DueDateFor(x.StartDate) == bill.DueDate.Date);
                    return stretch == null
                        ? Orphan(accrual, employee, bill)
                        : Map(accrual, employee, stretch, SoldDaysOn(bill, accrual, employee, stretch), bill);
                })
                .OrderBy(x => x.StartDate)
                .ToList();
        }

        private async Task<(EmployeeVacationAccrual Accrual, Employee Employee)> LoadAsync(Guid enterpriseId, Guid accrualId)
        {
            if (enterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Vacation pay enterprise is required.");

            if (accrualId == Guid.Empty)
                throw new InsertDatabaseException("Vacation pay período aquisitivo is required.");

            var accrual = await _accrualRepository.GetByIdAsync(accrualId);
            if (accrual == null || accrual.IsActive != true || accrual.EnterpriseId != enterpriseId)
                throw new NotFoundDatabaseException($"{nameof(EmployeeVacationAccrual)} was not found in database.");

            var employee = await _employeeRepository.GetByIdAsync(accrual.EmployeeId);
            if (employee == null || employee.EnterpriseId != enterpriseId)
                throw new NotFoundDatabaseException($"{nameof(Employee)} was not found in database.");

            if (!employee.Salary.HasValue || employee.Salary.Value <= 0)
                throw new InsertDatabaseException("This employee has no salary on record, so their férias cannot be priced.");

            return (accrual, employee);
        }

        /// <summary>
        /// The férias stretches booked against one accrual, oldest first.
        /// </summary>
        /// <remarks>
        /// The same calendar link <c>EmployeeVacationService.BookedPeriodLengthsAsync</c> uses:
        /// <c>EmployeeAbsence</c> carries no accrual FK, and concessivo windows of consecutive
        /// periods do not overlap, so "starts inside this accrual's concessivo window" is exact.
        /// Copying the rule rather than sharing it is deliberate — the alternative is a Bills
        /// dependency on the Employee module, which the layering table in
        /// docs/employee/SPEC-hr-module.md §5 does not have.
        /// </remarks>
        private async Task<List<EmployeeAbsence>> ScheduledStretchesAsync(EmployeeVacationAccrual accrual)
        {
            var windowStart = accrual.AcquisitionEnd.Date;
            var windowEnd = accrual.ConcessionDeadline.Date;

            try
            {
                var stretches = await _absenceRepository.GetAllAsync(
                    x => x.IsActive == true
                         && x.EmployeeId == accrual.EmployeeId
                         && x.Type == (int)EnumEmployeeAbsenceType.Vacation
                         && x.StartDate >= windowStart
                         && x.StartDate <= windowEnd);

                return stretches.OrderBy(x => x.StartDate).ToList();
            }
            catch (NotFoundDatabaseException)
            {
                // The repositories throw instead of returning an empty set; an accrual with nothing
                // scheduled yet is the ordinary case, not an error.
                return [];
            }
        }

        private async Task<List<PayableBill>> ExistingBillsAsync(Guid enterpriseId, Guid accrualId)
        {
            try
            {
                var bills = await _payableBillRepository.GetAllAsync(
                    x => x.IsActive == true
                         && x.EnterpriseId == enterpriseId
                         && !x.RefundedAt.HasValue
                         && x.EmployeeVacationAccrualId == accrualId);

                return bills.OrderBy(x => x.DueDate).ToList();
            }
            catch (NotFoundDatabaseException)
            {
                return [];
            }
        }

        private static int DaysOf(EmployeeAbsence stretch) =>
            (stretch.EndDate.Date - stretch.StartDate.Date).Days + 1;

        /// <summary>
        /// How many abono days an already-issued bill settled.
        /// </summary>
        /// <remarks>
        /// <see cref="PayableBill.AbonoDaysSettled"/> when the bill carries it. A bill issued before
        /// that column existed is inferred the old way — a bill worth more than its stretch alone is
        /// worth is the one that settled the abono — and the inference can only answer "all of what
        /// had been sold", so it is capped at what the accrual says was sold in total.
        /// </remarks>
        private static int SoldDaysOn(PayableBill bill, EmployeeVacationAccrual accrual, Employee employee, EmployeeAbsence stretch)
        {
            if (bill.AbonoDaysSettled is { } recorded)
                return Math.Min(recorded, accrual.SoldDays);

            if (accrual.SoldDays == 0)
                return 0;

            var withoutAbono = VacationPay.For(employee.Salary!.Value, DaysOf(stretch), 0, stretch.StartDate);

            // The gross, not Amount: Amount is netted by the provision drawdown, so a bill that did
            // settle the abono can still be worth less than the stretch alone.
            return GrossOf(bill) > withoutAbono.Total ? accrual.SoldDays : 0;
        }

        /// <summary>
        /// The same question for a bill whose stretch has to be found first — the due date is
        /// start - 2 days by construction, so it identifies it. A bill with no scheduled stretch
        /// left (a cancelled one) settled whatever it recorded.
        /// </summary>
        /// <remarks>
        /// A bill that records nothing is a bill issued before the column existed, and the amount
        /// inference cannot run on it: pricing the stretch it was issued for needs the stretch, and
        /// the stretch is gone. Read as "settled nothing" it would put the whole abono back on the
        /// next bill — paying days that are already standing, unrefunded, on this one. So the old
        /// rule stands for exactly those rows: the abono rode the first bill this accrual produced.
        /// Under-billing an orphan that never carried it leaves the days visible on the accrual;
        /// over-billing one that did sends the money twice.
        /// </remarks>
        private static int SettledAbonoDaysOn(PayableBill bill, EmployeeVacationAccrual accrual,
            Employee employee, IReadOnlyList<EmployeeAbsence> stretches)
        {
            var stretch = stretches.FirstOrDefault(x => VacationPay.DueDateFor(x.StartDate) == bill.DueDate.Date);

            return stretch == null
                ? Math.Min(bill.AbonoDaysSettled ?? accrual.SoldDays, accrual.SoldDays)
                : SoldDaysOn(bill, accrual, employee, stretch);
        }

        private static VacationPayDTO Map(
            EmployeeVacationAccrual accrual,
            Employee employee,
            EmployeeAbsence stretch,
            int soldDays,
            PayableBill bill)
        {
            var split = VacationPay.For(employee.Salary!.Value, DaysOf(stretch), soldDays, stretch.StartDate);

            return new VacationPayDTO
            {
                PayableBillId = bill.Id,
                EmployeeVacationAccrualId = accrual.Id,
                EmployeeId = employee.Id,
                EmployeeName = employee.Name,
                StartDate = stretch.StartDate.Date,
                EndDate = stretch.EndDate.Date,
                Days = DaysOf(stretch),
                SoldDays = soldDays,
                SalaryPortion = split.SalaryPortion,
                ConstitutionalThird = split.ConstitutionalThird,
                AbonoAmount = split.AbonoAmount,
                AbonoConstitutionalThird = split.AbonoConstitutionalThird,
                TotalAmount = split.Total,
                ProvisionOffset = GrossOf(bill) - bill.Amount,
                NetAmount = bill.Amount,
                DueDate = bill.DueDate,
                IsPaid = bill.IsPaid
            };
        }

        /// <summary>What the employee is paid, before the provision drawdown nets the bill down.</summary>
        private static decimal GrossOf(PayableBill bill) => bill.GrossAmount ?? bill.Amount;

        /// <summary>
        /// A férias bill whose stretch no longer exists — the absence was cancelled after the bill
        /// was issued. Reported with the bill's own amount rather than being hidden, because the
        /// money went out and somebody has to reconcile it.
        /// </summary>
        private static VacationPayDTO Orphan(EmployeeVacationAccrual accrual, Employee employee, PayableBill bill) =>
            new()
            {
                PayableBillId = bill.Id,
                EmployeeVacationAccrualId = accrual.Id,
                EmployeeId = employee.Id,
                EmployeeName = employee.Name,
                StartDate = bill.DueDate.Date.AddDays(VacationPay.PaymentLeadDays),
                EndDate = bill.DueDate.Date.AddDays(VacationPay.PaymentLeadDays),
                Days = 0,
                TotalAmount = GrossOf(bill),
                ProvisionOffset = GrossOf(bill) - bill.Amount,
                NetAmount = bill.Amount,
                DueDate = bill.DueDate,
                IsPaid = bill.IsPaid
            };
    }
}
