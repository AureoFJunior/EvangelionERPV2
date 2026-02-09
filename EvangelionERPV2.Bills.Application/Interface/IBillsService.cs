using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface IBillsService<TEntity> where TEntity : class
    {
        Task<Bill?> GetByOrderIdAsync(Guid orderId);
        Task<Bill?> GenerateAsync(Guid orderId);
        Task<byte[]?> GetPdfAsync(Guid orderId);
    }
}

