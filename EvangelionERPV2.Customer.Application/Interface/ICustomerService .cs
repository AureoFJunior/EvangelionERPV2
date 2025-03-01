using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.CustomerModule.Application.Interface
{
    public interface ICustomerService<TEntity> where TEntity : class
    {
        #region Sync
        public TEntity Delete(Guid id);
        public TEntity Update(Customer customer);
        #endregion

        #region Async
        public Task<TEntity> CreateAsync(Customer customer);
        #endregion
    }
}