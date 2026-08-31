using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.OrderModule.Application.Interface
{
    public interface ISalesPerformanceService
    {
        /// <summary>One competência of selling, per seller, with the enterprise's own row.</summary>
        Task<SalesPerformanceDTO> GetPerformanceAsync(Guid enterpriseId, int year, int month, CancellationToken cancellation = default);

        /// <summary>
        /// One seller's row for the competência, or <c>null</c> when they sold nothing and have no
        /// target. Never carries the enterprise row: that is the company's whole month, and a
        /// seller looking at their own commission has no business seeing it.
        /// </summary>
        Task<SalesPerformanceRowDTO?> GetSellerPerformanceAsync(Guid enterpriseId, Guid userId, int year, int month, CancellationToken cancellation = default);

        /// <summary>Every target set for a year, so the screen can render a month grid in one call.</summary>
        Task<IReadOnlyList<SalesGoalDTO>> GetGoalsAsync(Guid enterpriseId, int year, CancellationToken cancellation = default);

        /// <summary>
        /// Sets the target for a (seller, competência), replacing whatever was there. Upsert rather
        /// than create/update because the screen edits a cell in a grid and has no id to send the
        /// first time.
        /// </summary>
        Task<SalesGoal> UpsertGoalAsync(Guid enterpriseId, UpsertSalesGoalRequestDTO request, CancellationToken cancellation = default);
    }
}
