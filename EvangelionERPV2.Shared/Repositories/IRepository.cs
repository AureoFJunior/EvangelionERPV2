using System.Linq.Expressions;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Shared.Repositories
{
    public interface IRepository<TEntity> where TEntity : BaseEntity
    {
        #region Sync
        void Commit(CancellationToken cancellation = default);
        void ExecuteInTransaction(Action operation, CancellationToken cancellation = default);
        TResult ExecuteInTransaction<TResult>(Func<TResult> operation, CancellationToken cancellation = default);
        TEntity GetById(Guid id);
        IEnumerable<TEntity> GetAll();
        IEnumerable<TEntity> GetByCondition(Func<TEntity, bool> condition);
        TEntity Create(TEntity entity);
        IEnumerable<TEntity> CreateRange(IEnumerable<TEntity> entitys);
        TEntity Update(TEntity entity);
        IEnumerable<TEntity> UpdateRange(IEnumerable<TEntity> entitys);
        TEntity Delete(TEntity entity);
        TEntity Delete<TInclude>(TEntity entity, params Expression<Func<TEntity, TInclude>>[] includeProperties);
        IEnumerable<TEntity> DeleteRange(IEnumerable<TEntity> entitys);
        void DetachEntity<TDetach>(TDetach entity);
        #endregion

        #region Async
        Task CommitAsync(CancellationToken cancellation = default);
        Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellation = default);
        Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellation = default);
        Task<Guid> GetLastId();
        Task<TEntity> GetByIdAsync(Guid id);
        Task<IEnumerable<TEntity>> GetAllAsync(Func<TEntity, bool>? predicate = null);
        Task<IEnumerable<TEntity>> GetAllAsync(int? pageNumber, int? pageSize, Func<TEntity, bool>? predicate = null);
        Task<IEnumerable<TEntity>> GetAllAsyncByFilter(bool descending,
            int? pageNumber,
            int? pageSize,
            Expression<Func<TEntity, bool>>? predicate = null,
            Expression<Func<TEntity, object>>? orderBy = null);
        Task<TEntity> CreateAsync(TEntity entity);
        Task<IEnumerable<TEntity>> CreateRangeAsync(IEnumerable<TEntity> entitys);
        #endregion
    }
}
