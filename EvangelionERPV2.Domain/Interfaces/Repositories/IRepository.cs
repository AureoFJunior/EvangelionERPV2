namespace EvangelionERPV2.Domain.Interfaces.Repositories
{
    public interface IRepository<TEntity> where TEntity : class
    {
        #region Sync
        void Commit(CancellationToken cancellation = default);
        TEntity GetById(Guid id);
        IEnumerable<TEntity> GetAll();
        TEntity Create(TEntity entity);
        TEntity Update(TEntity entity);
        IEnumerable<TEntity> UpdateRange(IEnumerable<TEntity> entitys);
        TEntity Delete(TEntity entity);
        TEntity Delete<TInclude>(TEntity entity, params System.Linq.Expressions.Expression<Func<TEntity, TInclude>>[] includeProperties);
        IEnumerable<TEntity> DeleteRange(IEnumerable<TEntity> entitys);
        #endregion

        #region Async
        Task CommitAsync(CancellationToken cancellation = default);
        Task<Guid> GetLastId();
        Task<TEntity> GetByIdAsync(Guid id);
        Task<IEnumerable<TEntity>> GetAllAsync();
        Task<TEntity> CreateAsync(TEntity entity);
        Task<IEnumerable<TEntity>> CreateRangeAsync(IEnumerable<TEntity> entitys);
        IEnumerable<TEntity> GetByCondition(Func<TEntity, bool> condition);
        #endregion
    }
}