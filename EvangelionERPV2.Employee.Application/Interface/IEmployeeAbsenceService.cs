using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.EmployeeModule.Application.Interface
{
    public interface IEmployeeAbsenceService<TEntity> where TEntity : class
    {
        #region Sync
        public TEntity Delete(Guid id);

        /// <param name="plainCid">
        /// A newly supplied diagnosis code in the clear, or null to keep whatever is stored.
        /// The service is the only place that encrypts it.
        /// </param>
        public TEntity Update(EmployeeAbsence absence, string? plainCid = null);
        #endregion

        #region Async
        /// <param name="plainCid">A diagnosis code in the clear, encrypted before it is persisted.</param>
        public Task<TEntity> CreateAsync(EmployeeAbsence absence, string? plainCid = null);
        #endregion
    }
}
