using EvangelionERPV2.EmployeeModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.EmployeeModule.Application.Services
{
    /// <summary>
    /// Keeps <see cref="EmployeeVacationAccrual"/> honest: one row per período aquisitivo, its
    /// entitlement recomputed from the absence spine, and its counters moved only through the
    /// legal rules in <see cref="VacationEntitlement"/>.
    /// </summary>
    public class EmployeeVacationService : IEmployeeVacationService<EmployeeVacationAccrual>
    {
        /// <summary>
        /// CLT art. 145: a férias bill falls due two days before the leave starts. The same rule
        /// <c>VacationPay.DueDateFor</c> applies, copied rather than shared for the same reason the
        /// calendar link below is copied — Employee does not depend on the Bills module.
        /// </summary>
        private const int PaymentLeadDays = 2;

        private readonly IRepository<EmployeeVacationAccrual> _accrualRepository;
        private readonly IRepository<EmployeeAbsence> _absenceRepository;
        private readonly IRepository<Employee> _employeeRepository;
        private readonly IRepository<PayableBill> _payableBillRepository;

        public EmployeeVacationService(
            IRepository<EmployeeVacationAccrual> accrualRepository,
            IRepository<EmployeeAbsence> absenceRepository,
            IRepository<Employee> employeeRepository,
            IRepository<PayableBill> payableBillRepository)
        {
            _accrualRepository = accrualRepository;
            _absenceRepository = absenceRepository;
            _employeeRepository = employeeRepository;
            _payableBillRepository = payableBillRepository;
        }

        public async Task<IReadOnlyList<EmployeeVacationAccrual>> RefreshAsync(Guid employeeId, Guid enterpriseId)
        {
            if (employeeId == Guid.Empty)
                throw new InsertDatabaseException("A vacation ledger must belong to an employee.");

            try
            {
                var employee = await _employeeRepository.GetByIdAsync(employeeId)
                    ?? throw new NotFoundDatabaseException($"{nameof(Employee)} was not found in database.");

                if (employee.EnterpriseId != enterpriseId)
                    throw new NotFoundDatabaseException($"{nameof(Employee)} was not found in database.");

                var existing = (await _accrualRepository.GetAllAsync(
                        x => x.IsActive == true && x.EmployeeId == employeeId && x.EnterpriseId == enterpriseId))
                    .OrderBy(x => x.AcquisitionStart)
                    .ToList();

                var absences = (await _absenceRepository.GetAllAsync(
                        x => x.IsActive == true && x.EmployeeId == employeeId))
                    .ToList();

                var today = DateTime.UtcNow.Date;
                var ledger = new List<EmployeeVacationAccrual>();

                foreach (var (start, end) in AcquisitionPeriods(employee, today))
                {
                    var accrual = existing.FirstOrDefault(x => x.AcquisitionStart == start);

                    if (accrual == null)
                    {
                        accrual = new EmployeeVacationAccrual
                        {
                            Id = Guid.NewGuid(),
                            EmployeeId = employeeId,
                            EnterpriseId = enterpriseId,
                            AcquisitionStart = start,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        };

                        Reconcile(accrual, end, absences, today);
                        await _accrualRepository.CreateAsync(accrual);
                    }
                    else
                    {
                        if (accrual.Status == (int)EnumVacationAccrualStatus.Lapsed)
                        {
                            ledger.Add(accrual);
                            continue;
                        }

                        Reconcile(accrual, end, absences, today);
                        accrual.UpdatedAt = DateTime.UtcNow;
                        _accrualRepository.Update(accrual);
                    }

                    ledger.Add(accrual);
                }

                await _accrualRepository.CommitAsync();
                return ledger;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (InsertDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while refreshing the employee vacation ledger.", ex);
            }
        }

        public async Task<EmployeeVacationAccrual> ScheduleAsync(Guid accrualId, Guid enterpriseId, DateTime startDate, int days)
        {
            if (days <= 0)
                throw new InsertDatabaseException("A férias period must be at least one day.");

            try
            {
                var accrual = await LoadAsync(accrualId, enterpriseId);
                var start = startDate.Date;
                var end = start.AddDays(days - 1);

                if (accrual.Status == (int)EnumVacationAccrualStatus.Accruing)
                    throw new InsertDatabaseException("This período aquisitivo has not closed yet, so its férias cannot be scheduled.");

                if (days > accrual.RemainingDays)
                    throw new InsertDatabaseException($"Only {accrual.RemainingDays} day(s) remain on this período aquisitivo.");

                if (start <= accrual.AcquisitionEnd.Date || end > accrual.ConcessionDeadline.Date)
                    throw new InsertDatabaseException("The férias period must fit entirely inside the concessivo window for this período aquisitivo.");

                var bookedPeriods = await BookedPeriodsAsync(accrual);
                if (bookedPeriods.Any(period => period.StartDate.Date <= end && period.EndDate.Date >= start))
                    throw new InsertDatabaseException("The férias period overlaps another period already scheduled for this employee.");

                var bookedLengths = bookedPeriods
                    .Select(period => (period.EndDate.Date - period.StartDate.Date).Days + 1)
                    .ToList();
                if (!VacationEntitlement.TrySplit(days, bookedLengths, accrual.RemainingDays - days, out var reason))
                    throw new InsertDatabaseException(reason);

                await _accrualRepository.ExecuteInTransactionAsync(async () =>
                {
                    await _absenceRepository.CreateAsync(new EmployeeAbsence
                    {
                        Id = Guid.NewGuid(),
                        EmployeeId = accrual.EmployeeId,
                        EnterpriseId = accrual.EnterpriseId,
                        Type = (int)EnumEmployeeAbsenceType.Vacation,
                        StartDate = start,
                        EndDate = end,

                        IsJustified = true,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });

                    accrual.ScheduledDays += days;
                    accrual.Status = (int)StatusFor(accrual);
                    accrual.UpdatedAt = DateTime.UtcNow;
                    _accrualRepository.Update(accrual);

                    await _accrualRepository.CommitAsync();
                });

                return accrual;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (InsertDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while scheduling férias.", ex);
            }
        }

        public async Task<EmployeeVacationAccrual> SellDaysAsync(Guid accrualId, Guid enterpriseId, int days, DateTime? requestedOn = null)
        {
            if (days <= 0)
                throw new InsertDatabaseException("An abono pecuniário must be at least one day.");

            try
            {
                var accrual = await LoadAsync(accrualId, enterpriseId);
                var requestDate = (requestedOn ?? DateTime.UtcNow).Date;

                var deadline = accrual.AcquisitionEnd.Date.AddDays(-VacationEntitlement.AbonoRequestDeadlineDays);
                if (requestDate > deadline)
                    throw new InsertDatabaseException(
                        $"An abono pecuniário must be requested at least {VacationEntitlement.AbonoRequestDeadlineDays} days before the período aquisitivo closes (CLT art. 143 §1).");

                var sellable = VacationEntitlement.MaxSellableDays(accrual.EntitledDays, accrual.SoldDays);
                if (days > sellable)
                    throw new InsertDatabaseException(
                        $"At most {sellable} more day(s) may be sold: the abono is capped at a third of the entitlement (CLT art. 143).");

                if (days > accrual.RemainingDays)
                    throw new InsertDatabaseException($"Only {accrual.RemainingDays} day(s) remain on this período aquisitivo.");

                if (accrual.RemainingDays - days <= 0 && !await HasUnbilledStretchAsync(accrual, enterpriseId))
                    throw new InsertDatabaseException(
                        "Every férias period on this período aquisitivo has already been billed and none of its days are left to schedule another, so an abono pecuniário sold now would have no férias payment to be paid on (CLT art. 143).");

                accrual.SoldDays += days;
                accrual.Status = (int)StatusFor(accrual);
                accrual.UpdatedAt = DateTime.UtcNow;

                _accrualRepository.Update(accrual);
                await _accrualRepository.CommitAsync();
                return accrual;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (InsertDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while recording an abono pecuniário.", ex);
            }
        }

        private async Task<EmployeeVacationAccrual> LoadAsync(Guid accrualId, Guid enterpriseId)
        {
            var accrual = await _accrualRepository.GetByIdAsync(accrualId);

            if (accrual == null || accrual.IsActive != true || accrual.EnterpriseId != enterpriseId)
                throw new NotFoundDatabaseException($"{nameof(EmployeeVacationAccrual)} was not found in database.");

            if (accrual.Status == (int)EnumVacationAccrualStatus.Lapsed)
                throw new InsertDatabaseException("This período aquisitivo has lapsed and is owed in dobro; it cannot be scheduled or sold.");

            return accrual;
        }

        /// <summary>
        /// Stretches already booked against one accrual, so both overlap and art. 134 §1 can be
        /// checked against the complete set rather than against a single request.
        /// </summary>
        private async Task<IReadOnlyCollection<EmployeeAbsence>> BookedPeriodsAsync(EmployeeVacationAccrual accrual)
        {
            var windowStart = accrual.AcquisitionEnd.Date.AddDays(1);
            var windowEnd = accrual.ConcessionDeadline.Date;

            var booked = await _absenceRepository.GetAllAsync(
                x => x.IsActive == true
                     && x.EmployeeId == accrual.EmployeeId
                     && x.Type == (int)EnumEmployeeAbsenceType.Vacation
                     && x.StartDate >= windowStart
                     && x.StartDate <= windowEnd);

            return booked.ToList();
        }

        /// <summary>
        /// Whether any férias stretch booked against this accrual still has no bill standing against
        /// it — that is, whether a future <c>VacationPayService.GenerateAsync</c> run still has
        /// something to issue, and therefore something an abono could ride.
        /// </summary>
        private async Task<bool> HasUnbilledStretchAsync(EmployeeVacationAccrual accrual, Guid enterpriseId)
        {
            var windowStart = accrual.AcquisitionEnd.Date;
            var windowEnd = accrual.ConcessionDeadline.Date;

            var stretches = await _absenceRepository.GetAllAsync(
                x => x.IsActive == true
                     && x.EmployeeId == accrual.EmployeeId
                     && x.Type == (int)EnumEmployeeAbsenceType.Vacation
                     && x.StartDate >= windowStart
                     && x.StartDate <= windowEnd);

            var bills = await _payableBillRepository.GetAllAsync(
                x => x.IsActive == true
                     && x.EnterpriseId == enterpriseId
                     && !x.RefundedAt.HasValue
                     && x.EmployeeVacationAccrualId == accrual.Id);

            var billedDueDates = bills.Select(x => x.DueDate.Date).ToHashSet();

            return stretches.Any(x => !billedDueDates.Contains(x.StartDate.Date.AddDays(-PaymentLeadDays)));
        }

        /// <summary>
        /// Recomputes one accrual's entitlement and settles the stretches that have since elapsed.
        /// </summary>
        private static void Reconcile(
            EmployeeVacationAccrual accrual,
            DateTime acquisitionEnd,
            IReadOnlyCollection<EmployeeAbsence> absences,
            DateTime today)
        {
            accrual.AcquisitionEnd = acquisitionEnd;
            accrual.ConcessionDeadline = acquisitionEnd.AddMonths(VacationEntitlement.ConcessionMonths);

            var faltas = VacationEntitlement.CountUnjustifiedFaltas(absences, accrual.AcquisitionStart, acquisitionEnd);
            accrual.EntitledDays = VacationEntitlement.EntitledDaysFor(faltas);

            var taken = absences
                .Where(x => x.IsActive == true
                            && x.Type == (int)EnumEmployeeAbsenceType.Vacation
                            && x.StartDate.Date >= accrual.AcquisitionEnd.Date
                            && x.StartDate.Date <= accrual.ConcessionDeadline.Date)
                .Sum(x => ElapsedDays(x, today));

            var booked = absences
                .Where(x => x.IsActive == true
                            && x.Type == (int)EnumEmployeeAbsenceType.Vacation
                            && x.StartDate.Date >= accrual.AcquisitionEnd.Date
                            && x.StartDate.Date <= accrual.ConcessionDeadline.Date)
                .Sum(x => (x.EndDate.Date - x.StartDate.Date).Days + 1);

            accrual.TakenDays = Math.Min(taken, accrual.EntitledDays);
            accrual.ScheduledDays = Math.Max(0, Math.Min(booked, accrual.EntitledDays) - accrual.TakenDays);

            accrual.SoldDays = Math.Min(accrual.SoldDays, accrual.EntitledDays);

            accrual.Status = (int)(today <= acquisitionEnd
                ? EnumVacationAccrualStatus.Accruing
                : StatusFor(accrual));
        }

        private static int ElapsedDays(EmployeeAbsence absence, DateTime today)
        {
            var end = absence.EndDate.Date < today ? absence.EndDate.Date : today.AddDays(-1);
            return end < absence.StartDate.Date ? 0 : (end - absence.StartDate.Date).Days + 1;
        }

        private static EnumVacationAccrualStatus StatusFor(EmployeeVacationAccrual accrual)
        {
            if (accrual.RemainingDays <= 0)
                return EnumVacationAccrualStatus.Closed;

            return accrual.ScheduledDays + accrual.TakenDays + accrual.SoldDays > 0
                ? EnumVacationAccrualStatus.PartiallyTaken
                : EnumVacationAccrualStatus.Available;
        }

        /// <summary>
        /// Every período aquisitivo the employee's tenure has begun, oldest first.
        /// </summary>
        private static IEnumerable<(DateTime Start, DateTime End)> AcquisitionPeriods(Employee employee, DateTime today)
        {
            var horizon = employee.TerminationDate?.Date is { } termination && termination < today ? termination : today;
            var start = employee.HireDate.Date;

            while (start <= horizon)
            {
                yield return (start, start.AddMonths(VacationEntitlement.AcquisitionMonths).AddDays(-1));
                start = start.AddMonths(VacationEntitlement.AcquisitionMonths);
            }
        }
    }
}
