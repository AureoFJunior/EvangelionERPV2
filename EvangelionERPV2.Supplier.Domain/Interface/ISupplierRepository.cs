using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.SupplierModule.Domain.Interface
{
    public interface ISupplierRepository<TEntity> where TEntity : class
    {
        #region Async
        Task<IEnumerable<Supplier>> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Supplier supplier);
        #endregion
    }
}
