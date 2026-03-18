using System.Security.Claims;
using EvangelionERPV2.OpportunityRadarModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace EvangelionERPV2.Web.Controllers
{
    [ApiController]
    [Authorize]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/opportunities")]
    public class OpportunitiesController : ControllerBase
    {
        private readonly IOpportunityRadarService _opportunityRadarService;
        private readonly IRepository<User> _userRepository;

        public OpportunitiesController(IOpportunityRadarService opportunityRadarService, IRepository<User> userRepository)
        {
            _opportunityRadarService = opportunityRadarService;
            _userRepository = userRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetOpportunities([FromQuery] OpportunityFilterDTO filter)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var result = await _opportunityRadarService.GetOpportunitiesAsync(enterpriseId, filter ?? new OpportunityFilterDTO());
                return Ok(result);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Warning(ex, "Opportunity radar list was blocked by feature flag");
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetOpportunity(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var result = await _opportunityRadarService.GetOpportunityByIdAsync(id, enterpriseId);
                if (result == null)
                    return NoContent();

                return Ok(result);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Warning(ex, "Opportunity radar detail was blocked by feature flag");
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPost("{id:guid}/feedback")]
        public async Task<IActionResult> AddFeedback(Guid id, [FromBody] OpportunityFeedbackRequestDTO request)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var userId = TryGetUserId();
                var accessLevel = await ResolveAccessLevelAsync(userId, enterpriseId);
                var canApproveExecution = IsManagerialApprovalAccess(accessLevel);

                var result = await _opportunityRadarService.AddFeedbackAsync(
                    enterpriseId,
                    id,
                    userId,
                    request ?? new OpportunityFeedbackRequestDTO(),
                    canApproveExecution);

                return Ok(result);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Warning(ex, "Opportunity feedback failed validation");
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (NotFoundDatabaseException ex)
            {
                Log.Logger.Warning(ex, "Opportunity feedback target was not found");
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPost("recompute")]
        public async Task<IActionResult> Recompute([FromBody] OpportunityRecomputeRequestDTO request)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var userId = TryGetUserId();
                var accessLevel = await ResolveAccessLevelAsync(userId, enterpriseId);
                if (!IsManagerialApprovalAccess(accessLevel))
                    return Forbid();

                var result = await _opportunityRadarService.RecomputeAsync(
                    enterpriseId,
                    userId,
                    request ?? new OpportunityRecomputeRequestDTO(),
                    "manual");

                return Ok(result);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Warning(ex, "Opportunity recompute was blocked or invalid");
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var result = await _opportunityRadarService.GetSummaryAsync(enterpriseId);
                return Ok(result);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Warning(ex, "Opportunity summary was blocked by feature flag");
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }

        private Guid? TryGetUserId()
        {
            var claimValue = User.FindFirst(ClaimTypes.Sid)?.Value
                             ?? User.FindFirst("uid")?.Value;

            if (Guid.TryParse(claimValue, out var userId) && userId != Guid.Empty)
                return userId;

            return null;
        }

        private async Task<short?> ResolveAccessLevelAsync(Guid? userId, Guid enterpriseId)
        {
            if (!userId.HasValue || enterpriseId == Guid.Empty)
                return null;

            var user = await _userRepository.GetByIdAsync(userId.Value);
            if (user == null || user.IsActive != true || user.EnterpriseId != enterpriseId)
                return null;

            return user?.AccessLevel;
        }

        private static bool IsManagerialApprovalAccess(short? accessLevel)
        {
            return accessLevel.HasValue && accessLevel.Value <= (short)EnumAccessLevel.Manager;
        }

        private static string GetSafeInsertErrorMessage(InsertDatabaseException ex)
        {
            return ex.InnerException == null
                ? ex.Message
                : "An internal error occurred. Please try again later.";
        }
    }
}

