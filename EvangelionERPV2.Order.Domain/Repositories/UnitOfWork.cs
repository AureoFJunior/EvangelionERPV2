using EvangelionERPV2.OrderModule.Domain.Interface;
using EvangelionERPV2.OrderModule.Infra.Context;

namespace EvangelionERPV2.OrderModule.Domain.Repositories
{
    public class UnitOfWork<TContext> : IUnitOfWork<TContext> where TContext : OrderModuleDbContext
    {
        private readonly OrderModuleDbContext _context;

        public UnitOfWork(OrderModuleDbContext context)
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
