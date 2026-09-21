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

            var settledDays = existingBills.Sum(bill => SettledAbonoDaysOn(bill, accrual, employee, stretches));
            var abonoDays = Math.Max(0, accrual.SoldDays - settledDays);

            var pending = new List<(EmployeeAbsence Stretch, int SoldDays, VacationPayAmounts Amounts)>();

            foreach (var stretch in stretches)
            {
                var dueDate = VacationPay.DueDateFor(stretch.StartDate);
                if (existingBills.Any(x => x.DueDate.Date == dueDate))
                    continue;

                var soldDays = pending.Count == 0 ? abonoDays : 0;
                pending.Add((stretch, soldDays, VacationPay.For(
                    employee.Salary!.Value, DaysOf(stretch), soldDays, stretch.StartDate)));
            }

            if (pending.Count == 0)
                return await GetAsync(enterpriseId, accrualId);

            await _payableBillRepository.ExecuteInTransactionAsync(async () =>
            {
                foreach (var (stretch, soldDays, amounts) in pending)
                {
                    await _payableBillService.CreateAsync(new PayableBill
                    {
                        AbonoDaysSettled = soldDays,

                        Description = $"Férias {stretch.StartDate:dd/MM/yyyy} a {stretch.EndDate:dd/MM/yyyy} - {employee.Name}",
                        DueDate = VacationPay.DueDateFor(stretch.StartDate),
                        Amount = amounts.Total,
                        GrossAmount = amounts.Total,
                        IsPaid = false,
                        BillType = (int)EnumPayableBillType.Vacation,
                        EnterpriseId = enterpriseId,

                        EmployeeVacationAccrualId = accrual.Id
                    }, useAmbientTransaction: true);
                }

                await _drawdownService.RecalculateAsync(accrual.Id);
            });

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
        private static int SoldDaysOn(PayableBill bill, EmployeeVacationAccrual accrual, Employee employee, EmployeeAbsence stretch)
        {
            if (bill.AbonoDaysSettled is { } recorded)
                return Math.Min(recorded, accrual.SoldDays);

            if (accrual.SoldDays == 0)
                return 0;

            var withoutAbono = VacationPay.For(employee.Salary!.Value, DaysOf(stretch), 0, stretch.StartDate);

            return GrossOf(bill) > withoutAbono.Total ? accrual.SoldDays : 0;
        }

        /// <summary>
        /// The same question for a bill whose stretch has to be found first — the due date is
        /// start - 2 days by construction, so it identifies it. A bill with no scheduled stretch
        /// left (a cancelled one) settled whatever it recorded.
        /// </summary>
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
