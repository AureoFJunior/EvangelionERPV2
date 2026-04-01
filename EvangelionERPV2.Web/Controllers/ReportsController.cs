using System.Security.Claims;
using EvangelionERPV2.ReportsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace EvangelionERPV2.Web.Controllers
{
    [ApiController]
    [Authorize]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/reports")]
    public class ReportsController : ControllerBase
    {
        private const int MaxGenerateReportRequestBodySizeInBytes = 16 * 1024;
        private readonly IReportsService _reportsService;

        public ReportsController(IReportsService reportsService)
        {
            _reportsService = reportsService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<ReportListItemDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetReports()
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                if (!TryGetUserId(out var userId))
                    return Unauthorized();

                var reports = await _reportsService.GetUserReportsAsync(enterpriseId, userId);
                return Ok(reports);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Warning("Unable to list reports. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPost]
        [RequestSizeLimit(MaxGenerateReportRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(ReportListItemDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GenerateReport([FromBody] CreateReportRequestDTO request)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                if (!TryGetUserId(out var userId))
                    return Unauthorized();

                if (request == null)
                    return BadRequest("Invalid report request.");

                var report = await _reportsService.GenerateAsync(enterpriseId, userId, request.Type);
                return Ok(report);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Warning("Unable to generate report. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeErrorMessage(ex));
            }
            catch (NotFoundDatabaseException ex)
            {
                Log.Logger.Warning("Unable to generate report due to missing data. ErrorType={ErrorType}", ex.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ReportDetailDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetReportById(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                if (!TryGetUserId(out var userId))
                    return Unauthorized();

                var report = await _reportsService.GetByIdAsync(enterpriseId, userId, id);
                if (report == null)
                    return NoContent();

                return Ok(report);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Warning("Unable to fetch report detail. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeErrorMessage(ex));
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
                             ?? User.FindFirst("uid")?.Value;

            return Guid.TryParse(claimValue, out userId) && userId != Guid.Empty;
        }

        private static string GetSafeErrorMessage(InsertDatabaseException ex)
        {
            if (ex.InnerException != null)
                return "An internal error occurred. Please try again later.";

            var message = ex.Message?.Trim() ?? string.Empty;

            if (string.Equals(message, "No data available to generate report.", StringComparison.OrdinalIgnoreCase))
                return "No data available to generate report.";

            if (string.Equals(message, "Invalid report type.", StringComparison.OrdinalIgnoreCase))
                return "Invalid report type.";

            if (string.Equals(message, "No payable bills found in the current month to generate this report.", StringComparison.OrdinalIgnoreCase))
                return "No payable bills found in the current month to generate this report.";

            if (string.Equals(message, "No orders found in the current month to generate this report.", StringComparison.OrdinalIgnoreCase))
                return "No orders found in the current month to generate this report.";

            if (string.Equals(message, "Enterprise context is required.", StringComparison.OrdinalIgnoreCase))
                return "Enterprise context is required.";

            if (string.Equals(message, "User context is required.", StringComparison.OrdinalIgnoreCase))
                return "User context is required.";

            return "An internal error occurred. Please try again later.";
        }
    }
}
