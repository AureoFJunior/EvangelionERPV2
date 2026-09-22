using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    public class PayrollService : IPayrollService
    {
        private const int MinPayrollYear = 2000;
        private const int MaxPayrollYear = 2100;
        private const int MaxPayrollDueDay = 28;

        private readonly IRepository<EnterprisePayrollSettings> _settingsRepository;
        private readonly IRepository<Employee> _employeeRepository;
        private readonly IRepository<EmployeeAbsence> _absenceRepository;
        private readonly IRepository<PayableBill> _payableBillRepository;
        private readonly IRepository<PayrollBillDetail> _payrollDetailRepository;
        private readonly IPayableBillService _payableBillService;

        public PayrollService(
            IRepository<EnterprisePayrollSettings> settingsRepository,
            IRepository<Employee> employeeRepository,
            IRepository<EmployeeAbsence> absenceRepository,
            IRepository<PayableBill> payableBillRepository,
            IRepository<PayrollBillDetail> payrollDetailRepository,
            IPayableBillService payableBillService)
        {
            _settingsRepository = settingsRepository;
            _employeeRepository = employeeRepository;
            _absenceRepository = absenceRepository;
            _payableBillRepository = payableBillRepository;
            _payrollDetailRepository = payrollDetailRepository;
            _payableBillService = payableBillService;
        }

        public async Task<EnterprisePayrollSettings> GetSettingsAsync(Guid enterpriseId)
        {
            if (enterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Payroll settings enterprise is required.");

            var settings = (await _settingsRepository.GetAllAsync(
                    x => x.IsActive == true && x.EnterpriseId == enterpriseId))
                .FirstOrDefault();

            return settings ?? new EnterprisePayrollSettings { EnterpriseId = enterpriseId };
        }

        public async Task<EnterprisePayrollSettings> UpsertSettingsAsync(Guid enterpriseId, EnterprisePayrollSettings settings)
        {
            if (settings == null)
                throw new InsertDatabaseException($"{nameof(EnterprisePayrollSettings)} is null.");

            if (enterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Payroll settings enterprise is required.");

            ValidateRates(settings);

            var now = DateTime.UtcNow;
            var existing = (await _settingsRepository.GetAllAsync(
                    x => x.IsActive == true && x.EnterpriseId == enterpriseId))
                .FirstOrDefault();

            if (existing == null)
            {
                existing = new EnterprisePayrollSettings
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId,
                    CreatedAt = now,
                    IsActive = true
                };
                ApplyRates(existing, settings, now);
                await _settingsRepository.CreateAsync(existing);
            }
            else
            {
                ApplyRates(existing, settings, now);
                _settingsRepository.Update(existing);
            }

            await _settingsRepository.CommitAsync();
            return existing;
        }

        public async Task<IEnumerable<PayrollEmployeeBreakdownDTO>> GeneratePayrollAsync(Guid enterpriseId, int year, int month, Guid? employeeId = null)
        {
            ValidatePeriod(enterpriseId, year, month);

            var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1);
            var settings = await GetSettingsAsync(enterpriseId);
            ValidateRates(settings);

            List<Employee> employees;
            try
            {
                employees = (await _employeeRepository.GetAllAsync(
                        x => x.IsActive == true
                             && x.EnterpriseId == enterpriseId
                             && (!employeeId.HasValue || x.Id == employeeId.Value)
                             && x.Salary.HasValue
                             && x.Salary.Value > 0
                             && x.HireDate < monthEnd
                             && (!x.TerminationDate.HasValue || x.TerminationDate.Value >= monthStart)))
                    .ToList();
            }
            catch (NotFoundDatabaseException)
            {
                employees = [];
            }

            if (!employees.Any())
                throw new InsertDatabaseException(employeeId.HasValue
                    ? "Employee is not eligible for this payroll period."
                    : "No active employees with salary were found for this payroll period.");

            var (existingDetails, billsById, orphanedDetails) = await LoadExistingAsync(
                enterpriseId, year, month, employees.Select(x => x.Id).ToHashSet());

            var employeesById = employees.ToDictionary(x => x.Id, x => x);
            var results = existingDetails
                .Select(detail => MapBreakdown(
                    detail,
                    employeesById.TryGetValue(detail.EmployeeId, out var employee) ? employee.Name : string.Empty,
                    billsById[detail.PayableBillId]))
                .ToList();

            var alreadyBilled = existingDetails.Select(x => x.EmployeeId).ToHashSet();
            var pendingEmployees = employees.Where(x => !alreadyBilled.Contains(x.Id)).ToList();
            var staleDetails = orphanedDetails
                .Where(detail => pendingEmployees.Any(employee => employee.Id == detail.EmployeeId))
                .ToList();
            var absencesByEmployee = pendingEmployees.Any()
                ? await LoadAbsencesAsync(enterpriseId, monthStart, monthEnd)
                : [];
            var dueDay = Math.Clamp(settings.PayrollDueDay, 1, MaxPayrollDueDay);
            var dueDate = monthEnd.AddDays(dueDay - 1);
            var createdResults = new List<PayrollEmployeeBreakdownDTO>();

            if (pendingEmployees.Any())
            {
                await _payrollDetailRepository.ExecuteInTransactionAsync(async () =>
                {
                    createdResults.Clear();
                    var now = DateTime.UtcNow;

                    // Retire the detail rows whose payable bill was deleted or refunded before
                    // writing their replacements. The rows stay behind when a payroll bill is soft
                    // deleted — nothing cascades IsActive down to PayrollBillDetail — and since
                    // 20260722120000 there is a unique filtered index on
                    // (ReferenceYear, ReferenceMonth, EmployeeId) WHERE IsActive = 1, so leaving
                    // them active makes regeneration fail on a constraint violation instead of
                    // silently accumulating duplicates as it used to.
                    if (staleDetails.Any())
                    {
                        foreach (var stale in staleDetails)
                        {
                            stale.IsActive = false;
                            stale.UpdatedAt = now;
                        }

                        _payrollDetailRepository.UpdateRange(staleDetails);
                        await _payrollDetailRepository.CommitAsync();
                    }

                    foreach (var employee in pendingEmployees)
                    {
                        var detail = BuildDetail(
                            employee,
                            absencesByEmployee.TryGetValue(employee.Id, out var employeeAbsences) ? employeeAbsences : [],
                            settings,
                            year,
                            month,
                            now);
                        var bill = await _payableBillService.CreateAsync(new PayableBill
                        {
                            Description = $"Folha de pagamento {month:00}/{year} - {employee.Name}",
                            DueDate = dueDate,
                            Amount = detail.TotalCost,
                            IsPaid = false,
                            BillType = (int)EnumPayableBillType.Payroll,
                            EnterpriseId = enterpriseId
                        }, useAmbientTransaction: true);

                        detail.PayableBillId = bill.Id;
                        await _payrollDetailRepository.CreateAsync(detail);

                        createdResults.Add(MapBreakdown(detail, employee.Name, bill));
                    }

                    await _payrollDetailRepository.CommitAsync();
                });
            }

            results.AddRange(createdResults);
            return results.OrderBy(x => x.EmployeeName).ToList();
        }

        public async Task<IEnumerable<PayrollEmployeeBreakdownDTO>> GetPayrollAsync(Guid enterpriseId, int year, int month)
        {
            ValidatePeriod(enterpriseId, year, month);

            var (details, billsById, _) = await LoadExistingAsync(enterpriseId, year, month);
            if (!details.Any())
                return [];

            var detailEmployeeIds = details.Select(x => x.EmployeeId).ToHashSet();
            List<Employee> employees;
            try
            {
                employees = (await _employeeRepository.GetAllAsync(
                        x => x.EnterpriseId == enterpriseId && detailEmployeeIds.Contains(x.Id)))
                    .ToList();
            }
            catch (NotFoundDatabaseException)
            {
                employees = [];
            }

            var employeesById = employees.ToDictionary(x => x.Id, x => x);
            return details
                .Select(detail => MapBreakdown(
                    detail,
                    employeesById.TryGetValue(detail.EmployeeId, out var employee) ? employee.Name : string.Empty,
                    billsById[detail.PayableBillId]))
                .OrderBy(x => x.EmployeeName)
                .ToList();
        }

        /// <returns>
        /// The details whose payable bill is still alive, those bills, and the <c>orphaned</c>
        /// details whose bill was deleted or refunded. Orphans are still <c>IsActive = true</c> in
        /// the database and must be retired before their employee is billed again — see the unique
        /// filtered index added by 20260722120000.
        /// </returns>
        private async Task<(
            List<PayrollBillDetail> details,
            Dictionary<Guid, PayableBill> billsById,
            List<PayrollBillDetail> orphanedDetails)> LoadExistingAsync(
            Guid enterpriseId, int year, int month, HashSet<Guid>? employeeIds = null)
        {
            var filterByEmployee = employeeIds != null;
            var filteredEmployeeIds = employeeIds ?? [];

            var details = (await _payrollDetailRepository.GetAllAsync(
                    x => x.IsActive == true
                         && x.EnterpriseId == enterpriseId
                         && x.ReferenceYear == year
                         && x.ReferenceMonth == month
                         && (!filterByEmployee || filteredEmployeeIds.Contains(x.EmployeeId))))
                .ToList();

            if (!details.Any())
                return ([], [], []);

            var billIds = details.Select(x => x.PayableBillId).ToHashSet();
            var billsById = (await _payableBillRepository.GetAllAsync(
                    x => x.IsActive == true
                         && x.EnterpriseId == enterpriseId
                         && !x.RefundedAt.HasValue
                         && billIds.Contains(x.Id)))
                .ToDictionary(x => x.Id, x => x);

            var aliveDetails = details.Where(x => billsById.ContainsKey(x.PayableBillId)).ToList();
            var orphanedDetails = details.Where(x => !billsById.ContainsKey(x.PayableBillId)).ToList();
            return (aliveDetails, billsById, orphanedDetails);
        }

        private static PayrollBillDetail BuildDetail(
            Employee employee,
            IReadOnlyCollection<EmployeeAbsence> absences,
            EnterprisePayrollSettings settings,
            int year,
            int month,
            DateTime now)
        {
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var gross = RoundMoney(employee.Salary!.Value * GetWorkedDays(employee, absences, year, month, daysInMonth) / daysInMonth);

            var thirteenth = RoundMoney(gross / 12m);
            var vacation = RoundMoney(gross / 9m);
            var chargeBase = gross + thirteenth + vacation;

            var inss = RoundMoney(chargeBase * settings.InssRate);
            var rat = RoundMoney(chargeBase * settings.RatRate);
            var thirdParty = RoundMoney(chargeBase * settings.ThirdPartyRate);
            var fgts = RoundMoney(chargeBase * settings.FgtsRate);

            return new PayrollBillDetail
            {
                Id = Guid.NewGuid(),

                EnterpriseId = employee.EnterpriseId ?? Guid.Empty,
                ReferenceYear = year,
                ReferenceMonth = month,
                EmployeeId = employee.Id,
                GrossSalary = gross,
                ThirteenthSalaryProvision = thirteenth,
                VacationProvision = vacation,
                InssAmount = inss,
                RatAmount = rat,
                ThirdPartyAmount = thirdParty,
                FgtsAmount = fgts,
                TotalCost = gross + thirteenth + vacation + inss + rat + thirdParty + fgts,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            };
        }

        /// <summary>
        /// Inclusive day count of the intersection between the competência month and the
        /// employee's [HireDate, TerminationDate] window, minus the days an absence took away.
        /// </summary>
        private static int GetWorkedDays(Employee employee, IReadOnlyCollection<EmployeeAbsence> absences, int year, int month, int daysInMonth)
        {
            var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddDays(daysInMonth - 1);

            var start = employee.HireDate.Date > monthStart ? employee.HireDate.Date : monthStart;
            var end = employee.TerminationDate.HasValue && employee.TerminationDate.Value.Date < monthEnd
                ? employee.TerminationDate.Value.Date
                : monthEnd;

            var workedDays = (end - start).Days + 1;
            if (workedDays <= 0)
                return 0;

            return Math.Clamp(workedDays - CountUnpaidDays(absences, start, end), 0, daysInMonth);
        }

        /// <summary>
        /// Days inside [<paramref name="start"/>, <paramref name="end"/>] covered by an absence
        /// that costs the employee the day, counted once each.
        /// </summary>
        private static int CountUnpaidDays(IReadOnlyCollection<EmployeeAbsence> absences, DateTime start, DateTime end)
        {
            if (absences.Count == 0)
                return 0;

            var unpaidDays = new HashSet<DateTime>();

            foreach (var absence in absences.Where(ReducesPay))
            {
                var from = absence.StartDate.Date > start ? absence.StartDate.Date : start;
                var to = absence.EndDate.Date < end ? absence.EndDate.Date : end;

                for (var day = from; day <= to; day = day.AddDays(1))
                    unpaidDays.Add(day);
            }

            return unpaidDays.Count;
        }

        private static bool ReducesPay(EmployeeAbsence absence)
        {
            if (absence.Type == (int)EnumEmployeeAbsenceType.Vacation)
                return true;

            return !absence.IsJustified
                && absence.Type != (int)EnumEmployeeAbsenceType.Advertencia;
        }

        /// <summary>
        /// Every absence overlapping the competência month for the tenant, grouped by employee.
        /// </summary>
        private async Task<Dictionary<Guid, List<EmployeeAbsence>>> LoadAbsencesAsync(Guid enterpriseId, DateTime monthStart, DateTime monthEnd)
        {
            try
            {
                var absences = await _absenceRepository.GetAllAsync(
                    x => x.IsActive == true
                         && x.EnterpriseId == enterpriseId
                         && x.StartDate < monthEnd
                         && x.EndDate >= monthStart);

                return absences
                    .GroupBy(x => x.EmployeeId)
                    .ToDictionary(group => group.Key, group => group.ToList());
            }
            catch (NotFoundDatabaseException)
            {
                return [];
            }
        }

        private static decimal RoundMoney(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static PayrollEmployeeBreakdownDTO MapBreakdown(PayrollBillDetail detail, string employeeName, PayableBill bill)
        {
            return new PayrollEmployeeBreakdownDTO
            {
                PayableBillId = detail.PayableBillId,
                EmployeeId = detail.EmployeeId,
                EmployeeName = employeeName,
                ReferenceYear = detail.ReferenceYear,
                ReferenceMonth = detail.ReferenceMonth,
                GrossSalary = detail.GrossSalary,
                ThirteenthSalaryProvision = detail.ThirteenthSalaryProvision,
                VacationProvision = detail.VacationProvision,
                InssAmount = detail.InssAmount,
                RatAmount = detail.RatAmount,
                ThirdPartyAmount = detail.ThirdPartyAmount,
                FgtsAmount = detail.FgtsAmount,
                TotalCost = detail.TotalCost,
                DueDate = bill.DueDate,
                IsPaid = bill.IsPaid
            };
        }

        private static void ValidatePeriod(Guid enterpriseId, int year, int month)
        {
            if (enterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Payroll enterprise is required.");

            if (year < MinPayrollYear || year > MaxPayrollYear || month < 1 || month > 12)
                throw new InsertDatabaseException("Invalid payroll period.");
        }

        private static void ApplyRates(EnterprisePayrollSettings target, EnterprisePayrollSettings source, DateTime now)
        {
            target.InssRate = source.InssRate;
            target.RatRate = source.RatRate;
            target.ThirdPartyRate = source.ThirdPartyRate;
            target.FgtsRate = source.FgtsRate;
            target.PayrollDueDay = source.PayrollDueDay;
            target.UpdatedAt = now;
        }

        private static void ValidateRates(EnterprisePayrollSettings settings)
        {
            if (settings.InssRate < 0 || settings.InssRate > 1
                || settings.RatRate < 0 || settings.RatRate > 1
                || settings.ThirdPartyRate < 0 || settings.ThirdPartyRate > 1
                || settings.FgtsRate < 0 || settings.FgtsRate > 1)
                throw new InsertDatabaseException("Payroll charge rates must be between 0 and 1.");

            if (settings.PayrollDueDay < 1 || settings.PayrollDueDay > MaxPayrollDueDay)
                throw new InsertDatabaseException("Payroll due day must be between 1 and 28.");
        }
    }
}
