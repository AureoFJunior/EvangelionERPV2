using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.EmployeeModule.Application.Interface
{
    /// <summary>
    /// The férias entitlement ledger: what an employee has earned, booked, taken and sold.
    /// </summary>
    /// <remarks>
    /// Money is deliberately absent. This decides <em>when</em> férias happen; what they cost lives
    /// beside <c>PayrollService</c> in Bills, following the split the module already uses for
    /// payroll.
    /// </remarks>
    public interface IEmployeeVacationService<TEntity> where TEntity : class
    {
        /// <summary>
        /// Brings an employee's ledger up to date: opens any período aquisitivo their tenure has
        /// reached, recomputes each one's entitlement from the absences inside it, and settles
        /// stretches of leave that have since elapsed.
        /// </summary>
        /// <remarks>
        /// Idempotent by construction — periods are matched on <c>AcquisitionStart</c>, so running
        /// it twice reconciles instead of duplicating. That matters because it is called on every
        /// read of the ledger as well as on demand.
        /// </remarks>
        Task<IReadOnlyList<TEntity>> RefreshAsync(Guid employeeId, Guid enterpriseId);

        /// <summary>
        /// Books a stretch of leave against an accrual, writing the absence row and the decrement
        /// together.
        /// </summary>
        Task<TEntity> ScheduleAsync(Guid accrualId, Guid enterpriseId, DateTime startDate, int days);

        /// <summary>
        /// Converts days to cash — the abono pecuniário of CLT art. 143. Moves the counter only;
        /// the payment is Bills' problem.
        /// </summary>
        Task<TEntity> SellDaysAsync(Guid accrualId, Guid enterpriseId, int days, DateTime? requestedOn = null);
    }
}
