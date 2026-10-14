using System.Security.Claims;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class CashFlowForecastController : Controller
    {
        private const int MaxSimulationRequestBodySizeInBytes = 128 * 1024;
        private readonly ICashFlowForecastService _cashFlowForecastService;
        private readonly IRepository<User> _userRepository;

        public CashFlowForecastController(
            ICashFlowForecastService cashFlowForecastService,
            IRepository<User> userRepository)
        {
            _cashFlowForecastService = cashFlowForecastService;
            _userRepository = userRepository;
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.CashFlowForecast.Read)]
        [HttpGet("{horizonInDays}")]
        public async Task<IActionResult> GetForecast(int horizonInDays)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                if (!TryGetUserId(out var userId))
                    return Unauthorized();

                if (horizonInDays is not (30 or 60 or 90))
                    return BadRequest("Horizon must be 30, 60 or 90 days.");

                var result = await _cashFlowForecastService.GetForecastAsync(enterpriseId, horizonInDays);
                return Ok(result);
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.CashFlowForecast.Read)]
        [HttpGet("{horizonInDays}/{currentBalance}")]
        public async Task<IActionResult> GetForecastWithBalanceOverride(int horizonInDays, double currentBalance)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                if (!TryGetUserId(out var userId))
                    return Unauthorized();

                if (horizonInDays is not (30 or 60 or 90))
                    return BadRequest("Horizon must be 30, 60 or 90 days.");

                if (!double.IsFinite(currentBalance))
                    return BadRequest("Current balance is invalid.");

                var result = await _cashFlowForecastService.GetForecastAsync(enterpriseId, horizonInDays, currentBalance);
                return Ok(result);
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.CashFlowForecast.Simulate)]
        [HttpPost]
        [RequestSizeLimit(MaxSimulationRequestBodySizeInBytes)]
        public async Task<IActionResult> RunSimulation([FromBody] RunSimulationRequestDTO request)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                if (!TryGetUserId(out var userId))
                    return Unauthorized();

                var simulationResult = await _cashFlowForecastService.RunSimulationAsync(enterpriseId, userId, request);
                return Ok(simulationResult);
            }
            catch (ArgumentException ex)
            {
                Log.Logger.Warning("Invalid cash flow simulation request. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeArgumentErrorMessage(ex));
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

        private bool TryGetUserId(out Guid userId)
        {
            var claimValue = User.FindFirst(ClaimTypes.Sid)?.Value
                             ?? User.FindFirst("uid")?.Value
                             ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(claimValue, out userId) && userId != Guid.Empty;
        }



        private static string GetSafeArgumentErrorMessage(ArgumentException ex)
        {
            if (ex.InnerException != null)
                return "Invalid simulation request.";

            var message = ex.Message?.Trim() ?? string.Empty;
            if (string.Equals(message, "At least two scenarios are required.", StringComparison.OrdinalIgnoreCase))
                return "At least two scenarios are required.";

            return "Invalid simulation request.";
        }
    }
}
