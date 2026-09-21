using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Security.Claims;

namespace EvangelionERPV2.Web.Controllers
{
    /// <summary>
    /// Metas e comissão de vendedores: how the month is going, per seller.
    /// </summary>
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class SalesPerformanceController : Controller
    {
        private const int MaxSalesGoalRequestBodySizeInBytes = 8 * 1024;

        private readonly ISalesPerformanceService _salesPerformanceService;

        public SalesPerformanceController(ISalesPerformanceService salesPerformanceService)
        {
            _salesPerformanceService = salesPerformanceService;
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.SalesPerformance.Read)]
        [HttpGet]
        [ProducesResponseType(typeof(SalesPerformanceDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetPerformance(int year, int month)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                var performance = await _salesPerformanceService.GetPerformanceAsync(
                    enterpriseId, year, month, HttpContext.RequestAborted);

                return Ok(performance);
            }
            catch (InsertDatabaseException ex)
            {
                return BadRequest(GetSafeErrorMessage(ex));
            }
        }

        /// <summary>
        /// One seller's row. A seller reaches their own through <c>sales.performance.read.self</c>;
        /// anyone holding <c>sales.performance.read</c> reaches any of them.
        /// </summary>
        [Authorize(Policy = RbacPolicies.SalesPerformance.ReadSelfOrRead)]
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(SalesPerformanceRowDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetSellerPerformance(Guid id, int year, int month)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                var row = await _salesPerformanceService.GetSellerPerformanceAsync(
                    enterpriseId, id, year, month, HttpContext.RequestAborted);

                return row == null ? NoContent() : Ok(row);
            }
            catch (InsertDatabaseException ex)
            {
                return BadRequest(GetSafeErrorMessage(ex));
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.SalesPerformance.Read)]
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<SalesGoalDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetGoals(int year)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                var goals = await _salesPerformanceService.GetGoalsAsync(enterpriseId, year, HttpContext.RequestAborted);
                return Ok(goals);
            }
            catch (InsertDatabaseException ex)
            {
                return BadRequest(GetSafeErrorMessage(ex));
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.SalesPerformance.ManageGoals)]
        [HttpPut]
        [RequestSizeLimit(MaxSalesGoalRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(SalesGoalDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> UpsertGoal([FromBody] UpsertSalesGoalRequestDTO request)
        {
            if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
            if (request == null) return BadRequest();
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                var goal = await _salesPerformanceService.UpsertGoalAsync(enterpriseId, request, HttpContext.RequestAborted);

                return Ok(new SalesGoalDTO
                {
                    Id = goal.Id,
                    UserId = goal.UserId,
                    Year = goal.Year,
                    Month = goal.Month,
                    TargetAmount = goal.TargetAmount
                });
            }
            catch (NotFoundDatabaseException)
            {
                return NoContent();
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Sales goal refused. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeErrorMessage(ex));
            }
        }

        /// <summary>
        /// Echoes back only messages this code wrote, never a submitted value — the same allow-list
        /// shape QuoteController uses.
        /// </summary>
        private static string GetSafeErrorMessage(InsertDatabaseException ex)
        {
            if (ex.InnerException != null)
                return "An internal error occurred. Please try again later.";

            var message = ex.Message?.Trim() ?? string.Empty;

            string[] allowed =
            [
                "Year must be between 2000 and 2100.",
                "Month must be between 1 and 12.",
                "Target amount cannot be negative."
            ];

            return allowed.Contains(message) ? message : "An internal error occurred. Please try again later.";
        }

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }
    }
}
