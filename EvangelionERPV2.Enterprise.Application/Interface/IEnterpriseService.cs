using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.EnterpriseModule.Application.Interface
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