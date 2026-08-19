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
        IEnumerable<TEntity> GetByCondition(Expression<Func<TEntity, bool>> condition);
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
        Task<IEnumerable<TEntity>> GetAllAsync(Expression<Func<TEntity, bool>>? predicate = null);
        Task<IEnumerable<TEntity>> GetAllAsync(int? pageNumber, int? pageSize, Expression<Func<TEntity, bool>>? predicate = null);
        /// <summary>
        /// Paged read straight off <c>DbSet&lt;TEntity&gt;</c>.
        /// WARNING: unlike <see cref="GetAllAsync(int?, int?, Expression{Func{TEntity, bool}})"/>,
        /// this is NOT overridden by CustomerRepository / EmployeeRepository / EnterpriseRepository /
        /// OrderRepository / OrderedProductRepository / ProductRepository, so it returns rows
        /// with no <c>Include</c>, no <c>AsSplitQuery</c> and no implicit <c>IsActive</c> filter.
        /// Only use it for entities whose repository does not override <c>GetAllAsync</c>
        /// (currently Opportunity), or override it alongside <c>GetAllAsync</c> first.
        /// </summary>
        Task<(IReadOnlyList<TEntity> Items, int TotalItems)> GetPagedAsync(
            Expression<Func<TEntity, bool>>? predicate = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
            int? pageNumber = null,
            int? pageSize = null);
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
