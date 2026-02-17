using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    public class PayableBillService : IPayableBillService
    {
        private readonly IRepository<PayableBill> _payableBillRepository;

        public PayableBillService(IRepository<PayableBill> payableBillRepository)
        {
            _payableBillRepository = payableBillRepository;
        }

        public async Task<PayableBill> CreateAsync(PayableBill payableBill)
        {
            payableBill.Id = payableBill.Id == Guid.Empty ? Guid.NewGuid() : payableBill.Id;
            payableBill.CreatedAt = DateTime.UtcNow;
            payableBill.UpdatedAt = DateTime.UtcNow;
            payableBill.IsActive = true;

            var created = await _payableBillRepository.CreateAsync(payableBill);
            await _payableBillRepository.CommitAsync();
            return created;
        }

        public async Task<IEnumerable<PayableBill>> GetByEnterpriseIdAsync(Guid enterpriseId, int? pageNumber = null, int? pageSize = null)
        {
            return await _payableBillRepository.GetAllAsync(pageNumber, pageSize, x => x.EnterpriseId == enterpriseId && x.IsActive == true);
        }

        public async Task<PayableBill?> GetByIdAsync(Guid id, Guid enterpriseId)
        {
            var bill = await _payableBillRepository.GetByIdAsync(id);
            if (bill == null || bill.EnterpriseId != enterpriseId || bill.IsActive != true)
                return null;

            return bill;
        }

        public async Task<PayableBill> UpdateAsync(PayableBill payableBill, Guid enterpriseId)
        {
            var existing = await _payableBillRepository.GetByIdAsync(payableBill.Id);
            if (existing == null || existing.EnterpriseId != enterpriseId || existing.IsActive != true)
                throw new NotFoundDatabaseException($"{nameof(PayableBill)} was not found in database.");

            existing.Description = payableBill.Description;
            existing.DueDate = payableBill.DueDate;
            existing.PaidAt = payableBill.PaidAt;
            existing.Amount = payableBill.Amount;
            existing.IsPaid = payableBill.IsPaid;
            existing.UpdatedAt = DateTime.UtcNow;

            _payableBillRepository.Update(existing);
            await _payableBillRepository.CommitAsync();
            return existing;
        }

        public async Task<PayableBill> DeleteAsync(Guid id, Guid enterpriseId)
        {
            var existing = await _payableBillRepository.GetByIdAsync(id);
            if (existing == null || existing.EnterpriseId != enterpriseId || existing.IsActive != true)
                throw new NotFoundDatabaseException($"{nameof(PayableBill)} was not found in database.");

            existing.IsActive = false;
            existing.UpdatedAt = DateTime.UtcNow;
            _payableBillRepository.Update(existing);
            await _payableBillRepository.CommitAsync();
            return existing;
        }
    }
}
