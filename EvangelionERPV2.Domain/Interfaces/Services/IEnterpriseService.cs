using EvangelionERPV2.Domain.Models;

namespace EvangelionERPV2.Domain.Interfaces.Services
{
    public interface IEnterpriseService<TEntity> where TEntity : class
    {
        #region Sync
        public TEntity Delete(Guid id);
        public TEntity Update(Enterprise enterprise);
        #endregion

        #region Async
        public Task<TEntity> CreateAsync(Enterprise enterprise);
        #endregion
    }
}