using System.Linq.Expressions;
using EvangelionERPV2.Domain.Interfaces.Repositories;
using EvangelionERPV2.Domain.Models;
using EvangelionERPV2.Infra.Context;
using Microsoft.EntityFrameworkCore;


namespace EvangelionERPV2.Infra.Repositories
{
    public class Repository<TEntity> : IRepository<TEntity> where TEntity : BaseEntity
    {
        protected readonly AppDbContext _context;

        public Repository(AppDbContext context)
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

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync()
        {
            var query = _context.Set<TEntity>().AsNoTracking();

            if (await query.AnyAsync())
                return await query.ToListAsync();

            return new List<TEntity>();
        }

        public virtual async Task<TEntity> CreateAsync(TEntity entity)
        {
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