using EvangelionERPV2.PdvModule.Application.Interface;
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
    /// What a till talks to. Every action is written to be safe to call twice, because a terminal
    /// on a bad connection cannot tell a lost response from a refused one and will resend.
    /// </summary>
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class PdvController : Controller
    {
        // A sale carries its lines and its tenders and nothing else — no images, no attachments.
        // Small enough that anything larger is a bug or an attack, either way not worth reading.
        private const int MaxSaleRequestBodySizeInBytes = 256 * 1024;
        private const int MaxBatchRequestBodySizeInBytes = 2 * 1024 * 1024;

        private const int DefaultCatalogPageSize = 500;
        private const int MaxCatalogPageSize = 1000;

        // A terminal that has been off for a week sends its backlog in one call; more than this and
        // it takes two, which keeps one bad batch from occupying a request thread indefinitely.
        private const int MaxSalesPerBatch = 200;

        private readonly IPdvService _pdvService;

        public PdvController(IPdvService pdvService)
        {
            _pdvService = pdvService;
        }

        [HttpPost]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.OpenSession)]
        [ProducesResponseType(typeof(CashSessionDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> OpenSession([FromBody] OpenCashSessionRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId == null)
                return Unauthorized();

            var session = await _pdvService.OpenSessionAsync(request, enterpriseId, userId.Value);
            return Ok(ToDto(session));
        }

        [HttpPost]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.CloseSession)]
        [ProducesResponseType(typeof(CashSessionSummaryDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(204)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> CloseSession([FromBody] CloseCashSessionRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId == null)
                return Unauthorized();

            try
            {
                return Ok(await _pdvService.CloseSessionAsync(request, enterpriseId, userId.Value));
            }
            catch (NotFoundDatabaseException)
            {
                return NoContent();
            }
        }

        [HttpPost]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.CashMovement)]
        [ProducesResponseType(typeof(CashMovementDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(204)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> RegisterMovement([FromBody] CashMovementRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId == null)
                return Unauthorized();

            try
            {
                var movement = await _pdvService.RegisterMovementAsync(request, enterpriseId, userId.Value);

                return Ok(new CashMovementDTO
                {
                    Id = movement.Id,
                    CashSessionId = movement.CashSessionId,
                    Type = movement.Type,
                    Amount = movement.Amount,
                    Reason = movement.Reason,
                    UserId = movement.UserId,
                    ReferenceId = movement.ReferenceId,
                    CreatedAt = movement.CreatedAt
                });
            }
            catch (NotFoundDatabaseException)
            {
                return NoContent();
            }
        }

        /// <summary>
        /// Records one till sale. Idempotent on <c>ClientSaleId</c>.
        /// </summary>
        /// <remarks>
        /// A resend of a sale that already landed answers 200 with the same order id and
        /// <c>WasAlreadyRecorded</c> set, not a conflict — the terminal did the right thing by
        /// retrying and should be told so plainly.
        /// </remarks>
        [HttpPost]
        [RequestSizeLimit(MaxSaleRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(PdvSaleSyncResultDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(409)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> SyncSale([FromBody] PdvSaleSyncRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId == null)
                return Unauthorized();

            return Ok(await _pdvService.SyncSaleAsync(request, enterpriseId, userId.Value));
        }

        /// <summary>
        /// A terminal's backlog after it comes back online.
        /// </summary>
        /// <remarks>
        /// Sales are processed in the order given and a failure stops the batch rather than
        /// skipping past it: they are a queue, and the ones behind may depend on the ones in front.
        /// Whatever succeeded before the stop is still reported, so the terminal can clear those
        /// and resend the rest.
        /// </remarks>
        [HttpPost]
        [RequestSizeLimit(MaxBatchRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(PdvSaleSyncBatchResultDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> SyncSaleBatch([FromBody] PdvSaleSyncBatchRequestDTO request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var userId = TryGetUserId();
            if (userId == null)
                return Unauthorized();

            var sales = (request.Sales ?? []).ToList();
            if (sales.Count > MaxSalesPerBatch)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            var results = new List<PdvSaleSyncResultDTO>(sales.Count);

            foreach (var sale in sales)
            {
                try
                {
                    results.Add(await _pdvService.SyncSaleAsync(sale, enterpriseId, userId.Value));
                }
                catch (Exception ex)
                {
                    Log.Logger.Error("PDV batch stopped at sale [{ClientSaleId}]. ErrorType={ErrorType}",
                        sale.ClientSaleId, ex.GetType().Name);

                    // Report what did land rather than failing the whole call: those sales are
                    // recorded, and the terminal must be able to stop resending them.
                    break;
                }
            }

            return Ok(new PdvSaleSyncBatchResultDTO { Results = results });
        }

        [HttpGet("{clientSessionId}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.ReadReports)]
        [ProducesResponseType(typeof(CashSessionSummaryDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(204)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetSessionSummary(Guid clientSessionId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                // The reading is for a supervisor, who is not the person counting the drawer, so it
                // carries the expected figure. The blind count is enforced at CloseSession, which
                // takes the operator's number before it answers with this.
                return Ok(await _pdvService.GetSessionSummaryAsync(clientSessionId, enterpriseId, includeExpectedCash: true));
            }
            catch (NotFoundDatabaseException)
            {
                return NoContent();
            }
        }

        [HttpGet("{terminalId}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(CashSessionDTO), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(204)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetOpenSession(string terminalId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var session = await _pdvService.GetOpenSessionAsync(terminalId, enterpriseId);
            return session == null ? NoContent() : Ok(ToDto(session));
        }

        /// <summary>
        /// Products changed since the terminal last asked, for its local mirror.
        /// </summary>
        /// <remarks>
        /// No cost or margin here at any page size. The till needs a sale price to charge and the
        /// fiscal fields to build an NFC-e line; what the shop pays its suppliers has no business
        /// sitting on a machine in the shop floor, behind a login every cashier knows.
        /// </remarks>
        [HttpGet]
        [Authorize(Policy = "rbac:" + RbacPermissions.Pdv.Operate)]
        [ProducesResponseType(typeof(PdvCatalogPageDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetCatalog([FromQuery] DateTime? since, [FromQuery] int? pageSize)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var size = Math.Clamp(pageSize ?? DefaultCatalogPageSize, 1, MaxCatalogPageSize);
            return Ok(await _pdvService.GetCatalogAsync(since, enterpriseId, size));
        }

        private static CashSessionDTO ToDto(Shared.Entities.CashSession session) => new()
        {
            Id = session.Id,
            TerminalId = session.TerminalId,
            UserId = session.UserId,
            OpenedAt = session.OpenedAt,
            ClosedAt = session.ClosedAt,
            OpeningFloat = session.OpeningFloat,
            CountedAmount = session.CountedAmount,
            ExpectedAmount = session.ExpectedAmount,
            Difference = session.Difference,
            Status = session.Status
        };

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }

        private Guid? TryGetUserId()
        {
            var claimValue = User.FindFirst(ClaimTypes.Sid)?.Value
                ?? User.FindFirst("uid")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (Guid.TryParse(claimValue, out var userId) && userId != Guid.Empty)
                return userId;

            return null;
        }
    }
}
