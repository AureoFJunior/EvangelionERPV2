using EvangelionERPV2.UserModule.Domain.Interface;
using EvangelionERPV2.UserModule.Infra.Context;

namespace EvangelionERPV2.UserModule.Domain.Repositories
{
    public class UnitOfWork<TContext> : IUnitOfWork<TContext> where TContext : UserModuleDbContext
    {
        private readonly UserModuleDbContext _context;

        public UnitOfWork(UserModuleDbContext context)
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
