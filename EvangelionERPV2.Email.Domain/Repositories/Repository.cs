using System.Linq.Expressions;
using EvangelionERPV2.EmailModule.Domain.Interface;
using EvangelionERPV2.EmailModule.Infra.Context;
using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;


namespace EvangelionERPV2.EmailModule.Domain.Repositories
{
    public class Repository<TEntity> : IRepository<TEntity> where TEntity : BaseEntity
    {
        protected readonly EmailModuleDbContext _context;

        public Repository(EmailModuleDbContext context)
        {
            _context = context;
        }

        #region Sync

        public void Commit(CancellationToken cancellation = default)
        {
            _context.SaveChanges();
            return;
        }

        public virtual TEntity GetById(Guid id)
        {
            var query = _context.Set<TEntity>().Where(e => e.Id == id).AsNoTracking();

            if (query.Any())
                return query.FirstOrDefault();

            return null;
        }

        public virtual IEnumerable<TEntity> GetAll()
        {
            var query = _context.Set<TEntity>().AsNoTracking();

            if (query.Any())
                return query.AsNoTracking().ToList();

            return new List<TEntity>();
        }

        public virtual IEnumerable<TEntity> GetByCondition(Func<TEntity, bool> condition)
        {
            var query = _context.Set<TEntity>().AsNoTracking().Where(condition);

            if (query.Any())
                return query.ToList();

            return new List<TEntity>();
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
            _context.Set<TEntity>().Update(entity);
            return entity;
        }

        public virtual IEnumerable<TEntity> UpdateRange(IEnumerable<TEntity> entitys)
        {
            _context.Set<TEntity>().UpdateRange(entitys);
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

        private bool IsEntityTracked(object entity)
        {
            var entry = _context.ChangeTracker.Entries().FirstOrDefault(e => e.Entity == entity);
            return entry != null && entry.State != EntityState.Detached;
        }
        #endregion

        #region Async

        public Task CommitAsync(CancellationToken cancellation = default)
        {
            return _context.SaveChangesAsync();
        }

        public virtual async Task<Guid> GetLastId()
        {
            var query = _context.Set<TEntity>().AsNoTracking();
            if (await query.AnyAsync())
                return query.OrderByDescending(x => x.Id).FirstOrDefault().Id;

            return Guid.NewGuid();
        }

        public virtual async Task<TEntity> GetByIdAsync(Guid id)
        {
            var query = _context.Set<TEntity>().Where(e => e.Id == id).AsNoTracking();

            if (await query.AnyAsync())
                return await query.FirstOrDefaultAsync();

            return null;
        }

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync(Func<TEntity, bool> predicate = null)
        {
            IQueryable<TEntity> query = _context.Set<TEntity>().AsNoTracking();

            if (predicate != null)
                query = query.Where(entity => predicate(entity));

            if (await query?.AnyAsync())
                return await query.ToListAsync();

            return new List<TEntity>();
        }

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync(int? pageNumber, int? pageSize, Func<TEntity, bool> predicate = null)
        {
            IQueryable<TEntity> query = _context.Set<TEntity>().AsNoTracking();

            if (predicate != null)
                query = query.Where(entity => predicate(entity));

            int? skip = (pageNumber - 1) * pageSize ?? 1;
            List<TEntity>? result = null;

            if (await query.AnyAsync())
                result = await query.Skip(skip ?? 0).Take(pageSize ?? 0).ToListAsync();

            if (result?.Any() == false)
                return result;

            return new List<TEntity>();
        }

        public virtual async Task<IEnumerable<TEntity>> GetAllAsyncByFilter(bool descending,
            int? pageNumber,
            int? pageSize,
            Expression<Func<TEntity, bool>> predicate = null,
            Expression<Func<TEntity, object>> orderBy = null
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

            int? skip = (pageNumber - 1) * pageSize ?? 1;
            List<TEntity>? result = null;

            if (await query.AnyAsync())
                result = await query.Skip(skip ?? 0).Take(pageSize ?? 0).ToListAsync();

            if (result?.Any() != false)
                return result;

            return new List<TEntity>();
        }

        public virtual async Task<TEntity> CreateAsync(TEntity entity)
        {
            // Iterate over navigation properties
            foreach (var navigationEntry in _context.Entry(entity).Navigations)
            {
                // If the navigation property is loaded and points to an existing entity
                if (navigationEntry.IsLoaded && navigationEntry.CurrentValue != null)
                {
                    // Cast the current value to its appropriate type
                    var associatedEntities = navigationEntry.CurrentValue as IEnumerable<object>;
                    if (associatedEntities != null)
                    {
                        // Check if the associated entity is not tracked, then attach it
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