using System.Security.Claims;
using EvangelionERPV2.OpportunityRadarModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Security;
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
        private const int MaxOpportunityWriteRequestBodySizeInBytes = 64 * 1024;
        private readonly IOpportunityRadarService _opportunityRadarService;
        private readonly IRepository<User> _userRepository;

        public OpportunitiesController(IOpportunityRadarService opportunityRadarService, IRepository<User> userRepository)
        {
            _opportunityRadarService = opportunityRadarService;
            _userRepository = userRepository;
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Opportunities.Read)]
        [HttpGet]
        public async Task<IActionResult> GetOpportunities([FromQuery] OpportunityFilterDTO filter)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var userId = TryGetUserId();
                var result = await _opportunityRadarService.GetOpportunitiesAsync(enterpriseId, filter ?? new OpportunityFilterDTO());
                return Ok(result);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Warning("Opportunity radar list was blocked by feature flag. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Opportunities.Read)]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetOpportunity(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var userId = TryGetUserId();
                var result = await _opportunityRadarService.GetOpportunityByIdAsync(id, enterpriseId);
                if (result == null)
                    return NoContent();

                return Ok(result);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Warning("Opportunity radar detail was blocked by feature flag. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Opportunities.Feedback)]
        [HttpPost("{id:guid}/feedback")]
        [RequestSizeLimit(MaxOpportunityWriteRequestBodySizeInBytes)]
        public async Task<IActionResult> AddFeedback(Guid id, [FromBody] OpportunityFeedbackRequestDTO request)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var userId = TryGetUserId();
                var canApproveExecution = true;

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
                Log.Logger.Warning("Opportunity feedback failed validation. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (NotFoundDatabaseException ex)
            {
                Log.Logger.Warning("Opportunity feedback target was not found. ErrorType={ErrorType}", ex.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Opportunities.Recompute)]
        [HttpPost("recompute")]
        [RequestSizeLimit(MaxOpportunityWriteRequestBodySizeInBytes)]
        public async Task<IActionResult> Recompute([FromBody] OpportunityRecomputeRequestDTO request)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var userId = TryGetUserId();
                var result = await _opportunityRadarService.RecomputeAsync(
                    enterpriseId,
                    userId,
                    request ?? new OpportunityRecomputeRequestDTO(),
                    "manual");

                return Ok(result);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Warning("Opportunity recompute was blocked or invalid. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Opportunities.Read)]
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var userId = TryGetUserId();
                var result = await _opportunityRadarService.GetSummaryAsync(enterpriseId);
                return Ok(result);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Warning("Opportunity summary was blocked by feature flag. ErrorType={ErrorType}", ex.GetType().Name);
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



        private static string GetSafeInsertErrorMessage(InsertDatabaseException ex)
        {
            if (ex.InnerException != null)
                return "An internal error occurred. Please try again later.";

            var message = ex.Message?.Trim() ?? string.Empty;
            if (string.Equals(message, "Invalid feedback status. Use in_analysis, accepted, rejected, ignored or implemented.", StringComparison.OrdinalIgnoreCase))
                return "Invalid feedback status. Use in_analysis, accepted, rejected, ignored or implemented.";

            if (string.Equals(message, "Only managers can accept or implement opportunities.", StringComparison.OrdinalIgnoreCase))
                return "Only managers can accept or implement opportunities.";

            if (string.Equals(message, "Opportunity radar is disabled by feature flag.", StringComparison.OrdinalIgnoreCase))
                return "Opportunity radar is disabled by feature flag.";

            return "An internal error occurred. Please try again later.";
        }
    }
}
