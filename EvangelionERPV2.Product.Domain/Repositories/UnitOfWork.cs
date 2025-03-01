using EvangelionERPV2.ProductModule.Domain.Interface;
using EvangelionERPV2.ProductModule.Infra.Context;

namespace EvangelionERPV2.ProductModule.Domain.Repositories
{
    public class UnitOfWork<TContext> : IUnitOfWork<TContext> where TContext : ProductModuleDbContext
    {
        private readonly ProductModuleDbContext _context;

        public UnitOfWork(ProductModuleDbContext context)
        {
            _context = context;
        }

        public void Commit(CancellationToken cancellationToken = default)
        {
            if (_context.ChangeTracker.HasChanges())
                _context.SaveChanges();
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_context.ChangeTracker.HasChanges())
                await _context.SaveChangesAsync();
        }
    }
}
