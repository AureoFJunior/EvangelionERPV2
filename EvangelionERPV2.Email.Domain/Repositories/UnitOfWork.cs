using EvangelionERPV2.EmailModule.Domain.Interface;
using EvangelionERPV2.EmailModule.Infra.Context;

namespace EvangelionERPV2.EmailModule.Domain.Repositories
{
    public class UnitOfWork<TContext> : IUnitOfWork<TContext> where TContext : EmailModuleDbContext
    {
        private readonly EmailModuleDbContext _context;

        public UnitOfWork(EmailModuleDbContext context)
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
