using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.SupplierModule.Application.Interface
{
    public interface ISupplierService<TEntity> where TEntity : class
    {
        #region Sync
        public TEntity Delete(Guid id);
        public TEntity Update(Supplier supplier);
        #endregion

        #region Async
        public Task<TEntity> CreateAsync(Supplier supplier);
        #endregion
    }
}
