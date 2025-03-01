using EvangelionERPV2.Domain.Models;

namespace EvangelionERPV2.Domain.Interfaces.Services
{
    public interface IUserService<TEntity> where TEntity : class
    {
        #region Sync
        public TEntity Delete(Guid id);
        public TEntity Update(User user);
        #endregion

        #region Async
        public Task<TEntity> CreateAsync(User user);
        #endregion
    }
}