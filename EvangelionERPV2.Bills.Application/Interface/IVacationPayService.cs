using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface IVacationPayService
    {
        /// <summary>
        /// Bills every scheduled stretch of one período aquisitivo that has not been paid yet.
        /// </summary>
        /// <remarks>
        /// Idempotent: a stretch already carrying a live férias bill is returned untouched rather
        /// than billed again.
        /// </remarks>
        Task<IEnumerable<VacationPayDTO>> GenerateAsync(Guid enterpriseId, Guid accrualId);

        /// <summary>Férias payments already issued against one período aquisitivo.</summary>
        Task<IEnumerable<VacationPayDTO>> GetAsync(Guid enterpriseId, Guid accrualId);
    }
}
