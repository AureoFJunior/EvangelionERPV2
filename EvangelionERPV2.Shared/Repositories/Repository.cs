using System.Linq.Expressions;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Shared.Repositories
{
    public class Repository<TEntity> : RepositoryBase<TEntity>, IRepository<TEntity> where TEntity : BaseEntity
    {
        public Repository(AppDbContext context) : base(context)
        {
        }

        public virtual Task<IEnumerable<TEntity>> GetAllAsyncByFilter(bool descending,
            int? pageNumber,
            int? pageSize,
            Expression<Func<TEntity, bool>> predicate = null,
            Expression<Func<TEntity, object>> orderBy = null)
        {
            return GetAllAsyncByFilterInternal(descending, pageNumber, pageSize, predicate, orderBy);
        }
    }
}
