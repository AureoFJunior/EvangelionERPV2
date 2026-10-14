using EvangelionERPV2.Domain.Models;

namespace EvangelionERPV2.Domain.Interfaces.Services
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