using EvangelionERPV2.CustomerModule.Domain.Interface;
using EvangelionERPV2.CustomerModule.Infra.Context;

namespace EvangelionERPV2.CustomerModule.Domain.Repositories
{
    public class UnitOfWork<TContext> : IUnitOfWork<TContext> where TContext : CustomerModuleDbContext
    {
        private readonly CustomerModuleDbContext _context;

        public UnitOfWork(CustomerModuleDbContext context)
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
