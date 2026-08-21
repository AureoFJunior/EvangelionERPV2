using System.Security.Claims;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EvangelionERPV2.Web.Controllers
{
    /// <summary>
    /// The morning check: what needs a human today, in one call.
    /// </summary>
    /// <remarks>
    /// One endpoint, deliberately. The client that renders this used to page every product, order
    /// and customer into the browser to compute four numbers; the cost of that grows with the
    /// tenant, and it could never show the receivables, fiscal or stock signals at all.
    /// </remarks>
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class DashboardController : Controller
    {
        private readonly IDailyPanelService _dailyPanelService;

        public DashboardController(IDailyPanelService dailyPanelService)
        {
            _dailyPanelService = dailyPanelService;
        }

        /// <summary>
        /// Receivables, payables, stock criticality, fiscal state and uncleared bank lines as of now.
        /// </summary>
        [Authorize(Policy = RbacPolicies.Dashboard.Read)]
        [HttpGet]
        [ProducesResponseType(typeof(DailyPanelDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Today()
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            // Failures bubble to ExceptionHandlingMiddleware, same as every other read here.
            return Ok(await _dailyPanelService.GetTodayAsync(enterpriseId, HttpContext.RequestAborted));
        }

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }
    }
}
