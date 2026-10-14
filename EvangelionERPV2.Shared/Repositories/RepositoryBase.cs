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

        public virtual IEnumerable<TEntity> GetByCondition(Func<TEntity, bool> condition)
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
            return strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellation);
                await operation();
                await transaction.CommitAsync(cancellation);
            });
        }

        public virtual Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellation = default)
        {
            ArgumentNullException.ThrowIfNull(operation);

            var strategy = _context.Database.CreateExecutionStrategy();
            return strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellation);
                var result = await operation();
                await transaction.CommitAsync(cancellation);
                return result;
            });
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

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync(Func<TEntity, bool>? predicate = null)
        {
            IQueryable<TEntity> query = _context.Set<TEntity>().AsNoTracking();

            if (predicate != null)
                return query.AsEnumerable().Where(predicate).ToList();

            return await query.ToListAsync();
        }

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync(int? pageNumber, int? pageSize, Func<TEntity, bool>? predicate = null)
        {
            IQueryable<TEntity> query = _context.Set<TEntity>().AsNoTracking();

            if (pageNumber == null || pageSize == null)
                return await GetAllAsync(predicate);

            if (predicate != null)
                return query.AsEnumerable()
                    .Where(predicate)
                    .Skip((pageNumber.Value - 1) * pageSize.Value)
                    .Take(pageSize.Value)
                    .ToList();

            int skip = (pageNumber.Value - 1) * pageSize.Value;
            return await query
                .OrderBy(entity => entity.Id)
                .Skip(skip)
                .Take(pageSize.Value)
                .ToListAsync();
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
                    query = descending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);
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
                    query = descending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);
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
