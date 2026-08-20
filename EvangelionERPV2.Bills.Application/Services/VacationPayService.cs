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
            var billedDueDates = existingBills.Select(x => x.DueDate.Date).ToHashSet();

            // The abono is one conversion of days into cash for the whole período, so it settles
            // with the first bill this accrual ever produces and never rides a second one.
            // ponytail: days sold *after* that first bill are therefore not billed by this method.
            // Recording the abono settled on the bill would fix it; that needs a column, and
            // selling days after the leave is already being paid out is not a flow anyone has.
            var abonoDays = existingBills.Count == 0 ? accrual.SoldDays : 0;

            var results = new List<VacationPayDTO>();
            var pending = new List<(EmployeeAbsence Stretch, int SoldDays, VacationPayAmounts Amounts)>();

            foreach (var stretch in stretches)
            {
                var dueDate = VacationPay.DueDateFor(stretch.StartDate);
                var existing = existingBills.FirstOrDefault(x => x.DueDate.Date == dueDate);

                if (existing != null)
                {
                    results.Add(Map(accrual, employee, stretch, SoldDaysOn(existing, accrual, employee, stretch), existing));
                    continue;
                }

                // Only the first stretch this run bills carries the abono.
                var soldDays = pending.Count == 0 ? abonoDays : 0;
                pending.Add((stretch, soldDays, VacationPay.For(
                    employee.Salary!.Value, DaysOf(stretch), soldDays, stretch.StartDate)));

                billedDueDates.Add(dueDate);
            }

            if (pending.Count == 0)
                return results.OrderBy(x => x.StartDate).ToList();

            var created = new List<VacationPayDTO>();

            // One transaction for the whole accrual: a split férias billed halfway through would
            // leave the employee paid for one stretch and not the next, with no record of which.
            await _payableBillRepository.ExecuteInTransactionAsync(async () =>
            {
                created.Clear();

                foreach (var (stretch, soldDays, amounts) in pending)
                {
                    var bill = await _payableBillService.CreateAsync(new PayableBill
                    {
                        Description = $"Férias {stretch.StartDate:dd/MM/yyyy} a {stretch.EndDate:dd/MM/yyyy} - {employee.Name}",
                        DueDate = VacationPay.DueDateFor(stretch.StartDate),
                        Amount = amounts.Total,
                        IsPaid = false,
                        BillType = (int)EnumPayableBillType.Vacation,
                        EnterpriseId = enterpriseId,

                        // The back-link that makes the provision drawdown auditable: without it,
                        // "which competências did this payment consume" is a guess.
                        EmployeeVacationAccrualId = accrual.Id
                    }, useAmbientTransaction: true);

                    created.Add(Map(accrual, employee, stretch, soldDays, bill, amounts));
                }

                // Inside the transaction, after every bill exists: the drawdown recomputes from the
                // bills standing against this accrual, so it has to see all of them or none. It is
                // idempotent, so a retry of this whole block lands on the same numbers.
                await _drawdownService.RecalculateAsync(accrual.Id);
            });

            results.AddRange(created);
            return results.OrderBy(x => x.StartDate).ToList();
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
        /// Whether an already-issued bill included the abono, inferred from its amount.
        /// </summary>
        /// <remarks>
        /// Nothing records it, and the alternative is a column for a value that is derivable: a bill
        /// worth more than the stretch alone is worth is the one that settled the abono.
        /// </remarks>
        private static int SoldDaysOn(PayableBill bill, EmployeeVacationAccrual accrual, Employee employee, EmployeeAbsence stretch)
        {
            if (accrual.SoldDays == 0)
                return 0;

            var withoutAbono = VacationPay.For(employee.Salary!.Value, DaysOf(stretch), 0, stretch.StartDate);
            return bill.Amount > withoutAbono.Total ? accrual.SoldDays : 0;
        }

        private static VacationPayDTO Map(
            EmployeeVacationAccrual accrual,
            Employee employee,
            EmployeeAbsence stretch,
            int soldDays,
            PayableBill bill,
            VacationPayAmounts? amounts = null)
        {
            var split = amounts ?? VacationPay.For(employee.Salary!.Value, DaysOf(stretch), soldDays, stretch.StartDate);

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
                DueDate = bill.DueDate,
                IsPaid = bill.IsPaid
            };
        }

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
                TotalAmount = bill.Amount,
                DueDate = bill.DueDate,
                IsPaid = bill.IsPaid
            };
    }
}
