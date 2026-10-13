using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface IVacationPayService
    {
        /// <summary>
        /// Bills every scheduled stretch of one período aquisitivo that has not been paid yet.
        /// </summary>
        Task<IEnumerable<VacationPayDTO>> GenerateAsync(Guid enterpriseId, Guid accrualId);

        /// <summary>Férias payments already issued against one período aquisitivo.</summary>
        Task<IEnumerable<VacationPayDTO>> GetAsync(Guid enterpriseId, Guid accrualId);
    }
}
