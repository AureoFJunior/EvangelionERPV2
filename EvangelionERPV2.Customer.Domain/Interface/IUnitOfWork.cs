namespace EvangelionERPV2.CustomerModule.Domain.Interface
{
    public interface IUnitOfWork<TContext> where TContext : class
    {
        void Commit(CancellationToken cancellationToken = default);
        Task CommitAsync(CancellationToken cancellationToken = default);
    }
}
