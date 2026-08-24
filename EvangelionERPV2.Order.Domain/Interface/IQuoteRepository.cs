using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.OrderModule.Domain.Interface
{
    public interface IQuoteRepository : IRepository<Quote>
    {
        /// <summary>The next per-tenant quote number, seeding the counter on first use.</summary>
        Task<long> AllocateNextNumberAsync(Guid enterpriseId, CancellationToken cancellationToken = default);

        Task<Quote?> GetWithItemsAsync(Guid id, Guid enterpriseId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Quote>> GetByEnterpriseAsync(Guid enterpriseId, int? status, CancellationToken cancellationToken = default);
    }
}
