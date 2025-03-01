using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.EnterpriseModule.Infra.Context;

namespace EvangelionERPV2.EnterpriseModule.Domain.Repositories
{
    public class UnitOfWork<TContext> : IUnitOfWork<TContext> where TContext : EnterpriseModuleDbContext
    {
        private readonly EnterpriseModuleDbContext _context;

        public UnitOfWork(EnterpriseModuleDbContext context)
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
