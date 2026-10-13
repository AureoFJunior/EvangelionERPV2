using Asp.Versioning;
using System.Security.Claims;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace EvangelionERPV2.Web.Controllers
{
    /// <summary>
    /// Bank statement import and the variance it produces against the app-computed balance.
    /// </summary>
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class BankReconciliationController : Controller
    {
        private const long MaxStatementUploadSizeInBytes = 8 * 1024 * 1024;

        private const int MaxPageSize = 200;

        private readonly IBankReconciliationService _bankReconciliationService;

        public BankReconciliationController(IBankReconciliationService bankReconciliationService)
        {
            _bankReconciliationService = bankReconciliationService;
        }

        /// <summary>
        /// Upload an OFX statement. Safe to call twice with the same file: FITID is the idempotency
        /// key, so a re-import reports zero new lines instead of double-counting.
        /// </summary>
        [Authorize(Policy = RbacPolicies.BankReconciliation.Import)]
        [HttpPost]
        [ProducesResponseType(typeof(BankStatementImportResultDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> ImportStatement([FromForm] ImportBankStatementRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            var file = request?.File;

            if (file == null || file.Length == 0)
                return BadRequest("Statement file is required.");

            if (file.Length > MaxStatementUploadSizeInBytes)
                return BadRequest("Statement file is too large.");

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, HttpContext.RequestAborted);

            try
            {
                var result = await _bankReconciliationService.ImportAsync(
                    enterpriseId, buffer.ToArray(), HttpContext.RequestAborted);

                return Ok(result);
            }
            catch (FormatException ex)
            {
                Log.Logger.Error("Bank statement import rejected. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest("The file could not be read as an OFX statement.");
            }
        }

        /// <summary>
        /// Statement lines a human still has to clear: no candidate in the ERP, or more than one.
        /// </summary>
        [Authorize(Policy = RbacPolicies.BankReconciliation.Read)]
        [HttpGet("{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<BankStatementLineDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetUnclearedLines(int? pageNumber = null, int? pageSize = null)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var (normalizedPageNumber, normalizedPageSize) =
                Shared.Utils.PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);

            return Ok(await _bankReconciliationService.GetUnclearedAsync(
                enterpriseId, normalizedPageNumber, normalizedPageSize));
        }

        /// <summary>
        /// Point one statement line at the document it settles — an Order for money in, a
        /// PayableBill for money out.
        /// </summary>
        [Authorize(Policy = RbacPolicies.BankReconciliation.Resolve)]
        [HttpPut]
        [ProducesResponseType(typeof(BankStatementLineDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> MatchLine([FromBody] ResolveBankStatementLineRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            return Ok(await _bankReconciliationService.ResolveAsync(
                enterpriseId, request.LineId, request.MatchedReferenceId, HttpContext.RequestAborted));
        }

        /// <summary>
        /// Clear a line that corresponds to no ERP document — a bank fee, a tax debit, a transfer
        /// between the company's own accounts.
        /// </summary>
        [Authorize(Policy = RbacPolicies.BankReconciliation.Resolve)]
        [HttpPut]
        [ProducesResponseType(typeof(BankStatementLineDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> IgnoreLine([FromBody] IgnoreBankStatementLineRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            return Ok(await _bankReconciliationService.ResolveAsync(
                enterpriseId, request.LineId, matchedReferenceId: null, HttpContext.RequestAborted));
        }

        /// <summary>
        /// The app-computed balance, the bank's closing balance, and the gap between them.
        /// </summary>
        [Authorize(Policy = RbacPolicies.BankReconciliation.Read)]
        [HttpGet]
        [ProducesResponseType(typeof(BankReconciliationVarianceDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetVariance()
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            return Ok(await _bankReconciliationService.GetVarianceAsync(enterpriseId));
        }

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }
    }
}
