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
                Log.Logger.Warning(ex, "Unable to list reports");
                return BadRequest(GetSafeErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPost]
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
                Log.Logger.Warning(ex, "Unable to generate report");
                return BadRequest(GetSafeErrorMessage(ex));
            }
            catch (NotFoundDatabaseException ex)
            {
                Log.Logger.Warning(ex, "Unable to generate report due to missing data");
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
                Log.Logger.Warning(ex, "Unable to fetch report detail");
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
            return ex.InnerException == null
                ? ex.Message
                : "An internal error occurred. Please try again later.";
        }
    }
}
