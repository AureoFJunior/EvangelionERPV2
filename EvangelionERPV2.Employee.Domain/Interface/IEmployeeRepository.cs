using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.EmployeeModule.Domain.Interface
{
    public interface IEmployeeRepository<TEntity> where TEntity : class
    {
        #region Sync

        #endregion

        #region Async
        Task<IEnumerable<Employee>> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Employee employee,
        bool? isActive = null);
        #endregion
    }
}
