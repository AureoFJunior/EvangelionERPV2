using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.OpportunityRadarModule.Application.Interface
{
    public interface IOpportunityRadarService
    {
        Task<PaginatedResultDTO<OpportunityDTO>> GetOpportunitiesAsync(Guid enterpriseId, OpportunityFilterDTO filter);
        Task<OpportunityDTO?> GetOpportunityByIdAsync(Guid id, Guid enterpriseId);
        Task<OpportunityFeedbackDTO> AddFeedbackAsync(Guid enterpriseId, Guid opportunityId, Guid? userId, OpportunityFeedbackRequestDTO request, bool canApproveExecution);
        Task<OpportunityRunLogDTO> RecomputeAsync(Guid enterpriseId, Guid? requestedByUserId, OpportunityRecomputeRequestDTO request, string triggerType);
        Task<OpportunitySummaryDTO> GetSummaryAsync(Guid enterpriseId);
    }
}

