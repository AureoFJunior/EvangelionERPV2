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
        private readonly IRepository<PayableBill> _payableBillRepository;
        private readonly IRepository<PayrollBillDetail> _payrollDetailRepository;
        private readonly IPayableBillService _payableBillService;

        public PayrollService(
            IRepository<EnterprisePayrollSettings> settingsRepository,
            IRepository<Employee> employeeRepository,
            IRepository<PayableBill> payableBillRepository,
            IRepository<PayrollBillDetail> payrollDetailRepository,
            IPayableBillService payableBillService)
        {
            _settingsRepository = settingsRepository;
            _employeeRepository = employeeRepository;
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
                // The employee repository throws instead of returning an empty set; without this
                // the 400 below would surface as a 204 from the exception middleware.
                employees = [];
            }

            if (!employees.Any())
                throw new InsertDatabaseException(employeeId.HasValue
                    ? "Employee is not eligible for this payroll period."
                    : "No active employees with salary were found for this payroll period.");

            // Idempotency: one payroll bill per employee per competência month, tracked
            // through PayrollBillDetail. Employees whose bill is still alive are skipped.
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
            var dueDay = Math.Clamp(settings.PayrollDueDay, 1, MaxPayrollDueDay);
            var dueDate = monthEnd.AddDays(dueDay - 1);
            var createdResults = new List<PayrollEmployeeBreakdownDTO>();

            if (pendingEmployees.Any())
            {
                // One transaction for the whole loop: bills and their PayrollBillDetail rows share
                // the scoped context, so committing per bill would leave a bill without its detail
                // when a later employee fails — and idempotency keys off the detail, so the retry
                // would bill that employee twice.
                await _payrollDetailRepository.ExecuteInTransactionAsync(async () =>
                {
                    // Everything is staged inside the delegate: the execution strategy clears the
                    // change tracker before each attempt.
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
                        var detail = BuildDetail(employee, settings, year, month, now);
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

            // Details first, employees second: loading the whole employee history just to build a
            // name lookup pulled every row (and its joined Enterprise) of the tenant.
            var (details, billsById, _) = await LoadExistingAsync(enterpriseId, year, month);
            if (!details.Any())
                return [];

            // No IsActive filter: terminated/inactive employees keep their historical payroll.
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
            // PayrollBillDetail has no enterprise column: tenant isolation comes from the payable
            // bill lookup below, which discards details whose bill is not in this enterprise.
            var filterByEmployee = employeeIds != null;
            var filteredEmployeeIds = employeeIds ?? [];

            var details = (await _payrollDetailRepository.GetAllAsync(
                    x => x.IsActive == true
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

            // Details whose bill was deleted or refunded no longer count — the employee
            // becomes eligible for regeneration, but the stale row has to be retired first.
            var aliveDetails = details.Where(x => billsById.ContainsKey(x.PayableBillId)).ToList();
            var orphanedDetails = details.Where(x => !billsById.ContainsKey(x.PayableBillId)).ToList();
            return (aliveDetails, billsById, orphanedDetails);
        }

        private static PayrollBillDetail BuildDetail(Employee employee, EnterprisePayrollSettings settings, int year, int month, DateTime now)
        {
            // A mid-month hire or termination is only owed the days actually worked, so the
            // salary is prorated before any accrual or employer charge is derived from it.
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var gross = RoundMoney(employee.Salary!.Value * GetWorkedDays(employee, year, month, daysInMonth) / daysInMonth);

            // Monthly accruals per Brazilian labor legislation (CLT): 13º salário (1/12)
            // and férias plus the 1/3 constitutional bonus (1/12 * 4/3 = 1/9). Employer
            // charges apply over salary and accruals alike.
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

        // Inclusive day count of the intersection between the competência month and the
        // employee's [HireDate, TerminationDate] window.
        private static int GetWorkedDays(Employee employee, int year, int month, int daysInMonth)
        {
            var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddDays(daysInMonth - 1);

            var start = employee.HireDate.Date > monthStart ? employee.HireDate.Date : monthStart;
            var end = employee.TerminationDate.HasValue && employee.TerminationDate.Value.Date < monthEnd
                ? employee.TerminationDate.Value.Date
                : monthEnd;

            var workedDays = (end - start).Days + 1;
            return Math.Clamp(workedDays, 0, daysInMonth);
        }

        private static decimal RoundMoney(decimal value)
        {
            // Half away from zero, matching how SQL Server rounds the decimal(18,2) money columns.
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
