using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface IPayrollService
    {
        Task<EnterprisePayrollSettings> GetSettingsAsync(Guid enterpriseId);
        Task<EnterprisePayrollSettings> UpsertSettingsAsync(Guid enterpriseId, EnterprisePayrollSettings settings);
        Task<IEnumerable<PayrollEmployeeBreakdownDTO>> GeneratePayrollAsync(Guid enterpriseId, int year, int month, Guid? employeeId = null);
        Task<IEnumerable<PayrollEmployeeBreakdownDTO>> GetPayrollAsync(Guid enterpriseId, int year, int month);
    }
}
