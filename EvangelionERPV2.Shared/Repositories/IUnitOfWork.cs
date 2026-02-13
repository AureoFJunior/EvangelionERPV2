using EvangelionERPV2.Shared.Context;

namespace EvangelionERPV2.Shared.Repositories
{
    public interface IUnitOfWork<TContext> where TContext : AppDbContext
    {
        void Commit(CancellationToken cancellationToken = default);
        Task CommitAsync(CancellationToken cancellationToken = default);
    }
}
