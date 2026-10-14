using EvangelionERPV2.EmployeeModule.Application.Configs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Options;
using Serilog;

namespace EvangelionERPV2.EmployeeModule.Application.Services
{
    /// <summary>
    /// Owner of the CLT art. 137 consequence: when a período concessivo closes with days still
    /// owed, those days become payable <b>in dobro</b>.
    /// </summary>
    public class EmployeeVacationLapseService
    {
        private readonly IRepository<EmployeeVacationAccrual> _accrualRepository;
        private readonly EmployeeVacationSettings _settings;

        public EmployeeVacationLapseService(
            IRepository<EmployeeVacationAccrual> accrualRepository,
            IOptions<EmployeeVacationSettings> settings)
        {
            _accrualRepository = accrualRepository;
            _settings = settings?.Value ?? new EmployeeVacationSettings();
        }

        /// <summary>Runs one sweep across every tenant.</summary>
        /// <returns>How many accruals were newly marked <see cref="EnumVacationAccrualStatus.Lapsed"/>.</returns>
        public async Task<int> SweepLapsedAsync(CancellationToken cancellationToken = default)
        {
            var today = DateTime.UtcNow.Date;
            var batchSize = Math.Max(1, _settings.LapseSweepBatchSize);

            var candidates = (await _accrualRepository.GetAllAsync(
                    pageNumber: 1,
                    pageSize: batchSize,
                    predicate: accrual =>
                        (accrual.IsActive == null || accrual.IsActive == true)
                        && accrual.ConcessionDeadline < today
                        && accrual.Status != (int)EnumVacationAccrualStatus.Lapsed
                        && accrual.Status != (int)EnumVacationAccrualStatus.Closed
                        && accrual.EntitledDays - accrual.ScheduledDays - accrual.TakenDays - accrual.SoldDays > 0))
                .OrderBy(accrual => accrual.ConcessionDeadline)
                .ToList();

            var lapsed = 0;
            foreach (var accrual in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var outstanding = accrual.RemainingDays;

                    accrual.Status = (int)EnumVacationAccrualStatus.Lapsed;
                    accrual.UpdatedAt = DateTime.UtcNow;

                    _accrualRepository.Update(accrual);
                    await _accrualRepository.CommitAsync(cancellationToken);
                    lapsed++;

                    Log.Logger.Warning(
                        "Vacation accrual {AccrualId} for employee {EmployeeId} lapsed: concessivo closed {ConcessionDeadline:yyyy-MM-dd} " +
                        "with {RemainingDays} day(s) outstanding, now owed in dobro (CLT art. 137).",
                        accrual.Id,
                        accrual.EmployeeId,
                        accrual.ConcessionDeadline,
                        outstanding);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Log.Logger.Error(
                        exception,
                        "Failed to lapse vacation accrual {AccrualId}.",
                        accrual.Id);
                }
            }

            return lapsed;
        }

        /// <summary>
        /// Accruals whose concessivo deadline falls inside the warning horizon and that still have
        /// days outstanding — the ones a human can still act on before they cost double.
        /// </summary>
        public async Task<IReadOnlyList<EmployeeVacationAccrual>> GetApproachingDeadlineAsync(Guid enterpriseId)
        {
            if (enterpriseId == Guid.Empty)
                return [];

            var today = DateTime.UtcNow.Date;
            var horizon = today.AddDays(Math.Max(0, _settings.DeadlineWarningHorizonDays));

            var approaching = await _accrualRepository.GetAllAsync(accrual =>
                (accrual.IsActive == null || accrual.IsActive == true)
                && accrual.EnterpriseId == enterpriseId
                && accrual.ConcessionDeadline >= today
                && accrual.ConcessionDeadline <= horizon
                && accrual.Status != (int)EnumVacationAccrualStatus.Lapsed
                && accrual.Status != (int)EnumVacationAccrualStatus.Closed
                && accrual.EntitledDays - accrual.ScheduledDays - accrual.TakenDays - accrual.SoldDays > 0);

            return approaching
                .OrderBy(accrual => accrual.ConcessionDeadline)
                .ToList();
        }
    }
}
