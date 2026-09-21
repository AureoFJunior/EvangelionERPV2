using System.Linq.Expressions;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Repositories
{
    public abstract class RepositoryBase<TEntity> where TEntity : BaseEntity
    {
        protected readonly AppDbContext _context;

        protected RepositoryBase(AppDbContext context)
        {
            _context = context;
        }

        #region Sync

        public virtual void Commit(CancellationToken cancellation = default)
        {
            _context.SaveChanges();
            return;
        }

        public virtual void ExecuteInTransaction(Action operation, CancellationToken cancellation = default)
        {
            ArgumentNullException.ThrowIfNull(operation);

            var strategy = _context.Database.CreateExecutionStrategy();
            strategy.Execute(() =>
            {
                _context.ChangeTracker.Clear();

                using var transaction = _context.Database.BeginTransaction();
                operation();
                transaction.Commit();
            });
        }

        public virtual TResult ExecuteInTransaction<TResult>(Func<TResult> operation, CancellationToken cancellation = default)
        {
            ArgumentNullException.ThrowIfNull(operation);

            var strategy = _context.Database.CreateExecutionStrategy();
            TResult result = default!;

            strategy.Execute(() =>
            {
                _context.ChangeTracker.Clear();

                using var transaction = _context.Database.BeginTransaction();
                result = operation();
                transaction.Commit();
            });

            return result;
        }

        public virtual TEntity GetById(Guid id)
        {
            var query = _context.Set<TEntity>().Where(e => e.Id == id).AsNoTracking();

            var entity = query.FirstOrDefault();
            return entity ?? null!;
        }

        public virtual IEnumerable<TEntity> GetAll()
        {
            var query = _context.Set<TEntity>().AsNoTracking();

            var result = query.ToList();
            return result.Count > 0 ? result : new List<TEntity>();
        }

        public virtual IEnumerable<TEntity> GetByCondition(Expression<Func<TEntity, bool>> condition)
        {
            var query = _context.Set<TEntity>().AsNoTracking().Where(condition);

            var result = query.ToList();
            return result.Count > 0 ? result : new List<TEntity>();
        }

        public virtual TEntity Create(TEntity entity)
        {
            _context.Set<TEntity>().Add(entity);
            return entity;
        }

        public virtual IEnumerable<TEntity> CreateRange(IEnumerable<TEntity> entitys)
        {
            _context.Set<TEntity>().AddRange(entitys);
            return entitys;
        }

        public virtual TEntity Update(TEntity entity)
        {
            var trackedEntity = _context.Set<TEntity>().Local.FirstOrDefault(localEntity => localEntity.Id == entity.Id);
            if (trackedEntity != null && !ReferenceEquals(trackedEntity, entity))
            {
                _context.Entry(trackedEntity).CurrentValues.SetValues(entity);
                _context.Entry(trackedEntity).State = EntityState.Modified;
                return trackedEntity;
            }

            _context.Set<TEntity>().Update(entity);
            return entity;
        }

        public virtual IEnumerable<TEntity> UpdateRange(IEnumerable<TEntity> entitys)
        {
            var entitiesToUpdate = new List<TEntity>();

            foreach (var entity in entitys)
            {
                var trackedEntity = _context.Set<TEntity>().Local.FirstOrDefault(localEntity => localEntity.Id == entity.Id);
                if (trackedEntity != null && !ReferenceEquals(trackedEntity, entity))
                {
                    _context.Entry(trackedEntity).CurrentValues.SetValues(entity);
                    _context.Entry(trackedEntity).State = EntityState.Modified;
                    continue;
                }

                entitiesToUpdate.Add(entity);
            }

            if (entitiesToUpdate.Count > 0)
                _context.Set<TEntity>().UpdateRange(entitiesToUpdate);

            return entitys;
        }

        public virtual TEntity Delete(TEntity entity)
        {
            _context.Set<TEntity>().Remove(entity);
            return entity;
        }

        public virtual TEntity Delete<TInclude>(TEntity entity, params Expression<Func<TEntity, TInclude>>[] includeProperties)
        {
            foreach (var includeProperty in includeProperties)
            {
                _context.Set<TEntity>().Include(includeProperty);
            }

            _context.Set<TEntity>().Remove(entity);
            return entity;
        }

        public virtual IEnumerable<TEntity> DeleteRange(IEnumerable<TEntity> entitys)
        {
            _context.Set<TEntity>().RemoveRange(entitys);
            return entitys;
        }

        public void DetachEntity<TDetach>(TDetach entity)
        {
            if (entity != null)
            {
                _context.Entry(entity).State = EntityState.Detached;
            }
        }

        private bool IsEntityTracked(object entity)
        {
            var entry = _context.ChangeTracker.Entries().FirstOrDefault(e => e.Entity == entity);
            return entry != null && entry.State != EntityState.Detached;
        }
        #endregion

        #region Async

        public virtual Task CommitAsync(CancellationToken cancellation = default)
        {
            return _context.SaveChangesAsync();
        }

        public virtual Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellation = default)
        {
            ArgumentNullException.ThrowIfNull(operation);

            var strategy = _context.Database.CreateExecutionStrategy();
            return strategy.ExecuteAsync(async (CancellationToken token) =>
            {
                _context.ChangeTracker.Clear();

                await using var transaction = await _context.Database.BeginTransactionAsync(token);
                await operation();
                await transaction.CommitAsync(token);
            }, cancellation);
        }

        public virtual Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellation = default)
        {
            ArgumentNullException.ThrowIfNull(operation);

            var strategy = _context.Database.CreateExecutionStrategy();
            return strategy.ExecuteAsync(async (CancellationToken token) =>
            {
                _context.ChangeTracker.Clear();

                await using var transaction = await _context.Database.BeginTransactionAsync(token);
                var result = await operation();
                await transaction.CommitAsync(token);
                return result;
            }, cancellation);
        }

        public virtual async Task<Guid> GetLastId()
        {
            var query = _context.Set<TEntity>().AsNoTracking();
            var lastEntity = await query.OrderByDescending(x => x.Id).FirstOrDefaultAsync();
            return lastEntity?.Id ?? Guid.NewGuid();
        }

        public virtual async Task<TEntity> GetByIdAsync(Guid id)
        {
            var query = _context.Set<TEntity>().Where(e => e.Id == id).AsNoTracking();

            var entity = await query.FirstOrDefaultAsync();
            return entity ?? null!;
        }

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync(Expression<Func<TEntity, bool>>? predicate = null)
        {
            IQueryable<TEntity> query = _context.Set<TEntity>().AsNoTracking();

            if (predicate != null)
                query = query.Where(predicate);

            return await query.ToListAsync();
        }

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync(int? pageNumber, int? pageSize, Expression<Func<TEntity, bool>>? predicate = null)
        {
            IQueryable<TEntity> query = _context.Set<TEntity>().AsNoTracking();

            if (predicate != null)
                query = query.Where(predicate);

            if (pageNumber == null || pageSize == null)
                return await query.ToListAsync();

            int skip = (pageNumber.Value - 1) * pageSize.Value;
            return await query
                .OrderBy(entity => entity.Id)
                .Skip(skip)
                .Take(pageSize.Value)
                .ToListAsync();
        }

        public virtual async Task<(IReadOnlyList<TEntity> Items, int TotalItems)> GetPagedAsync(
            Expression<Func<TEntity, bool>>? predicate = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
            int? pageNumber = null,
            int? pageSize = null)
        {
            IQueryable<TEntity> query = _context.Set<TEntity>().AsNoTracking();

            if (predicate != null)
                query = query.Where(predicate);

            var totalItems = await query.CountAsync();

            query = orderBy != null ? orderBy(query).ThenBy(entity => entity.Id) : query.OrderBy(entity => entity.Id);

            if (pageNumber == null || pageSize == null)
                return (await query.ToListAsync(), totalItems);

            int skip = (pageNumber.Value - 1) * pageSize.Value;
            var items = await query.Skip(skip).Take(pageSize.Value).ToListAsync();

            return (items, totalItems);
        }

        protected virtual async Task<IEnumerable<TEntity>> GetAllAsyncByFilterInternal(bool descending,
            int? pageNumber,
            int? pageSize,
            Expression<Func<TEntity, bool>>? predicate = null,
            Expression<Func<TEntity, object>>? orderBy = null
            )
        {
            IQueryable<TEntity> query = _context.Set<TEntity>().AsNoTracking();

            if (predicate != null || orderBy != null)
            {
                if (predicate != null)
                    query = query.Where(predicate);

                if (orderBy != null)
                    query = (descending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy)).ThenBy(entity => entity.Id);
            }

            if (pageNumber == null || pageSize == null)
                return await query.ToListAsync();

            if (orderBy == null)
                query = descending ? query.OrderByDescending(entity => entity.Id) : query.OrderBy(entity => entity.Id);

            int skip = (pageNumber.Value - 1) * pageSize.Value;
            return await query.Skip(skip).Take(pageSize.Value).ToListAsync();
        }

        protected virtual async Task<(IEnumerable<TEntity>, int)> GetAllAsyncByFilterWithCountInternal(bool descending,
            int? pageNumber,
            int? pageSize,
            Expression<Func<TEntity, bool>>? predicate = null,
            Expression<Func<TEntity, object>>? orderBy = null
            )
        {
            IQueryable<TEntity> query = _context.Set<TEntity>().AsNoTracking();

            if (predicate != null || orderBy != null)
            {
                if (predicate != null)
                    query = query.Where(predicate);

                if (orderBy != null)
                    query = (descending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy)).ThenBy(entity => entity.Id);
            }

            int totalItems = await query.CountAsync();
            if (pageNumber == null || pageSize == null)
                return (await query.ToListAsync(), totalItems);

            if (orderBy == null)
                query = descending ? query.OrderByDescending(entity => entity.Id) : query.OrderBy(entity => entity.Id);

            int skip = (pageNumber.Value - 1) * pageSize.Value;
            var result = await query.Skip(skip).Take(pageSize.Value).ToListAsync();

            return (result, totalItems);
        }

        /// <summary>
        /// Takes SQL Server's update lock on one row and holds it to the end of the caller's
        /// transaction, so two transactions that read-then-write around that row queue behind
        /// each other instead of both reading the state before either wrote. It is that one row:
        /// any other id contends with nothing.
        /// </summary>
        public virtual async Task LockForUpdateAsync(Guid id, CancellationToken cancellation = default)
        {
            if (_context.Database.ProviderName?.Contains("SqlServer", StringComparison.Ordinal) != true)
                return;

            var table = _context.Model.FindEntityType(typeof(TEntity))?.GetTableName();
            if (string.IsNullOrEmpty(table))
                return;

#pragma warning disable EF1002
            await _context.Database.ExecuteSqlRawAsync(
                $"SELECT TOP 1 1 FROM [{table}] WITH (UPDLOCK, HOLDLOCK) WHERE Id = {{0}}",
                [id],
                cancellation);
#pragma warning restore EF1002
        }

        public virtual async Task<TEntity> CreateAsync(TEntity entity)
        {
            foreach (var navigationEntry in _context.Entry(entity).Navigations)
            {
                if (navigationEntry.IsLoaded && navigationEntry.CurrentValue != null)
                {
                    var associatedEntities = navigationEntry.CurrentValue as IEnumerable<object>;
                    if (associatedEntities != null)
                    {
                        foreach (var associatedEntity in associatedEntities)
                        {
                            if (!IsEntityTracked(associatedEntity))
                            {
                                _context.Attach(associatedEntity);
                            }
                        }
                    }
                }
            }

            await _context.Set<TEntity>().AddAsync(entity);
            return entity;
        }
        public virtual async Task<IEnumerable<TEntity>> CreateRangeAsync(IEnumerable<TEntity> entitys)
        {
            await _context.Set<TEntity>().AddRangeAsync(entitys);
            return entitys;
        }
        #endregion
    }
}
