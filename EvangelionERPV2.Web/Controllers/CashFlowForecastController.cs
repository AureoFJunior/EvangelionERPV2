using System.Security.Claims;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
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

        public CashFlowForecastController(ICashFlowForecastService cashFlowForecastService)
        {
            _cashFlowForecastService = cashFlowForecastService;
        }

        [HttpGet("{horizonInDays}")]
        public async Task<IActionResult> GetForecast(int horizonInDays)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
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

        [HttpGet("{horizonInDays}/{currentBalance}")]
        public async Task<IActionResult> GetForecastWithBalanceOverride(int horizonInDays, double currentBalance)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                if (horizonInDays is not (30 or 60 or 90))
                    return BadRequest("Horizon must be 30, 60 or 90 days.");

                var result = await _cashFlowForecastService.GetForecastAsync(enterpriseId, horizonInDays, currentBalance);
                return Ok(result);
            }
            catch (Exception)
            {
                throw;
            }
        }

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
