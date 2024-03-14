namespace EvangelionERPV2.Domain.Interfaces
{
    public interface IUnitOfWork<TContext> where TContext : class
    {
        void Commit(CancellationToken cancellationToken = default);
        Task CommitAsync(CancellationToken cancellationToken = default);
    }
}
