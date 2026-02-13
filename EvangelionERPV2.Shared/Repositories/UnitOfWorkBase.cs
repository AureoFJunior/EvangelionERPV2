using EvangelionERPV2.Shared.Context;

namespace EvangelionERPV2.Shared.Repositories
{
    public abstract class UnitOfWorkBase
    {
        protected readonly AppDbContext _context;

        protected UnitOfWorkBase(AppDbContext context)
        {
            _context = context;
        }

        public void Commit(CancellationToken cancellationToken = default)
        {
            if (_context.ChangeTracker.HasChanges())
                _context.SaveChanges();
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_context.ChangeTracker.HasChanges())
                return _context.SaveChangesAsync();

            return Task.CompletedTask;
        }
    }
}
