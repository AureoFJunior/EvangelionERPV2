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
    /// <remarks>
    /// <para>
    /// <see cref="EnumVacationAccrualStatus.Lapsed"/> is set here and nowhere else, because it is a
    /// consequence of the calendar rather than a decision anybody makes. A person cannot choose not
    /// to owe it, so a person must not be able to set or clear it.
    /// </para>
    /// <para>
    /// The sweep is deliberately cross-tenant. It runs outside a request, where
    /// <c>AppDbContext.AmbientEnterpriseId</c> is <see cref="Guid.Empty"/> and the tenant query
    /// filter is disabled by design — see the remarks on <c>ApplyTenantQueryFilters</c>. A liability
    /// that only accrued for whichever tenant happened to be in scope would be worse than none.
    /// </para>
    /// </remarks>
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

            // The paged overload so the cap reaches SQL. Taking it client-side would still read
            // every lapsed-but-unmarked accrual in the installation into memory first, which is
            // precisely the set that grows without bound if the sweeper has ever been down.
            var candidates = (await _accrualRepository.GetAllAsync(
                    pageNumber: 1,
                    pageSize: batchSize,
                    predicate: accrual =>
                        (accrual.IsActive == null || accrual.IsActive == true)
                        && accrual.ConcessionDeadline < today
                        && accrual.Status != (int)EnumVacationAccrualStatus.Lapsed
                        && accrual.Status != (int)EnumVacationAccrualStatus.Closed
                        // RemainingDays is [NotMapped], so the arithmetic is spelled out to keep
                        // the filter in SQL rather than evaluating it per row in memory.
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

                    // After the commit, not before. Warning rather than Information because past
                    // this line the company owes these days twice, and that has to reach whoever
                    // reads the logs rather than only whoever queries the table — which makes a
                    // false positive from a failed write the one thing this message must never be.
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
                    // One unwritable row must not stop the sweep: the others are exactly the ones
                    // sitting on a liability nobody has noticed yet.
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
        /// <remarks>
        /// Request-path, so it is tenant-scoped twice: the ambient query filter does it in the
        /// model, and <paramref name="enterpriseId"/> repeats it here as the same second belt the
        /// rest of the solution keeps. Already-lapsed accruals are excluded — they are a debt to
        /// settle, not a deadline to meet, and mixing the two hides both.
        /// </remarks>
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
