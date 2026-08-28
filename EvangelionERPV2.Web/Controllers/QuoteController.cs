using AutoMapper;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Security.Claims;

namespace EvangelionERPV2.Web.Controllers
{
    /// <summary>
    /// Orçamentos. A quote reserves nothing; converting one is what creates an order.
    /// </summary>
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class QuoteController : Controller
    {
        private const int MaxQuoteRequestBodySizeInBytes = 512 * 1024;

        private readonly IQuoteService _quoteService;
        private readonly IAuthorizationService _authorizationService;
        private readonly IMapper _mapper;

        public QuoteController(
            IQuoteService quoteService,
            IAuthorizationService authorizationService,
            IMapper mapper)
        {
            _quoteService = quoteService;
            _authorizationService = authorizationService;
            _mapper = mapper;
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Quotes.Read)]
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<QuoteDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetQuotes(int? status = null)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var quotes = await _quoteService.GetByEnterpriseAsync(enterpriseId, status, HttpContext.RequestAborted);
            return Ok(quotes.Select(ToDto).ToList());
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Quotes.Read)]
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(QuoteDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetQuote(Guid id)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var quote = await _quoteService.GetByIdAsync(id, enterpriseId, HttpContext.RequestAborted);
            return quote == null ? NoContent() : Ok(ToDto(quote));
        }

        /// <summary>
        /// The quote as a PDF, ready to send. Gated by the same read permission as the quote
        /// itself: the document states nothing the caller could not already see.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Quotes.Read)]
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetQuotePdf(Guid id)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var document = await _quoteService.GetPdfAsync(id, enterpriseId, HttpContext.RequestAborted);
            if (document == null)
                return NoContent();

            return File(document.Value.Pdf, "application/pdf", $"orcamento-{document.Value.Number}.pdf");
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Quotes.Create)]
        [HttpPost]
        [RequestSizeLimit(MaxQuoteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(QuoteDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> AddQuote([FromBody] UpsertQuoteRequestDTO request)
        {
            if (!ModelState.IsValid || request == null)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                var quote = await _quoteService.CreateAsync(enterpriseId, request, HttpContext.RequestAborted);
                return Ok(ToDto(quote));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid quote payload. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeErrorMessage(ex));
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Quotes.Update)]
        [HttpPut("{id}")]
        [RequestSizeLimit(MaxQuoteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(QuoteDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> UpdateQuote(Guid id, [FromBody] UpsertQuoteRequestDTO request)
        {
            if (!ModelState.IsValid || request == null)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                var quote = await _quoteService.UpdateAsync(id, enterpriseId, request, HttpContext.RequestAborted);
                return Ok(ToDto(quote));
            }
            catch (NotFoundDatabaseException)
            {
                return NoContent();
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid quote update. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeErrorMessage(ex));
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Quotes.Update)]
        [HttpPut("{id}")]
        [RequestSizeLimit(MaxQuoteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(QuoteDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> SetQuoteStatus(Guid id, [FromBody] QuoteStatusRequestDTO request)
        {
            if (!ModelState.IsValid || request == null)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                var quote = await _quoteService.SetStatusAsync(id, enterpriseId, (EnumQuoteStatus)request.Status, HttpContext.RequestAborted);
                return Ok(ToDto(quote));
            }
            catch (NotFoundDatabaseException)
            {
                return NoContent();
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid quote status change. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeErrorMessage(ex));
            }
        }

        /// <summary>
        /// Turns the quote into an order. Every gate a typed order passes applies here, so this can
        /// answer 409 for a closed period or a credit limit just as order creation does.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Quotes.Convert)]
        [HttpPost("{id}")]
        [RequestSizeLimit(MaxQuoteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(QuoteDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> ConvertQuote(Guid id, [FromBody] ConvertQuoteRequestDTO? request)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                var allowOverride = (await _authorizationService.AuthorizeAsync(
                    User, RbacPolicies.Customers.CreditOverride)).Succeeded;

                var quote = await _quoteService.ConvertAsync(
                    id, enterpriseId, request ?? new ConvertQuoteRequestDTO(), allowOverride, HttpContext.RequestAborted);

                return Ok(ToDto(quote));
            }
            catch (NotFoundDatabaseException)
            {
                return NoContent();
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Quote conversion refused. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeErrorMessage(ex));
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Quotes.Delete)]
        [HttpDelete("{id}")]
        [ProducesResponseType(typeof(QuoteDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> DeleteQuote(Guid id)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                var quote = await _quoteService.DeleteAsync(id, enterpriseId, HttpContext.RequestAborted);
                return Ok(ToDto(quote));
            }
            catch (NotFoundDatabaseException)
            {
                return NoContent();
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Quote delete refused. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeErrorMessage(ex));
            }
        }

        private static QuoteDTO ToDto(Quote quote) => new()
        {
            Id = quote.Id,
            CreatedAt = quote.CreatedAt,
            UpdatedAt = quote.UpdatedAt,
            IsActive = quote.IsActive,
            Number = quote.Number,
            EnterpriseId = quote.EnterpriseId,
            CustomerId = quote.CustomerId,
            CustomerName = quote.Customer?.Name ?? string.Empty,
            Status = quote.Status,
            EffectiveStatus = (int)quote.StatusAsOf(DateTime.UtcNow),
            IssuedAt = quote.IssuedAt,
            ValidUntil = quote.ValidUntil,
            TotalValue = quote.TotalValue,
            Notes = quote.Notes,
            ConvertedOrderId = quote.ConvertedOrderId,
            Items = quote.Items?.Select(item => new QuoteItemDTO
            {
                Id = item.Id,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt,
                IsActive = item.IsActive,
                ProductId = item.ProductId,
                ProductName = item.Product?.Name ?? string.Empty,
                Quantity = item.Quantity,
                UnitValue = item.UnitValue,
                LineAmount = item.LineAmount,
                UnitOfMeasure = item.UnitOfMeasure
            }).ToList() ?? []
        };

        /// <summary>
        /// The refusals this controller is allowed to repeat back. Everything else is generic:
        /// the same whitelist discipline the other controllers use, so a message never leaks a
        /// value the caller did not already send.
        /// </summary>
        private static string GetSafeErrorMessage(InsertDatabaseException ex)
        {
            if (ex.InnerException != null)
                return "An internal error occurred. Please try again later.";

            var message = ex.Message?.Trim() ?? string.Empty;

            string[] allowed =
            [
                "A quote needs at least one item.",
                "Each quote item must have quantity greater than zero.",
                "The quote has duplicate products.",
                "Some quote products were not found in enterprise inventory.",
                "Only a draft quote can be edited.",
                "A quote can only be marked as sent, accepted or rejected.",
                "This quote is closed and cannot change status.",
                "This quote has already been converted.",
                "This quote has expired and cannot be converted.",
                "This quote is closed and cannot be converted.",
                "A quote with no items cannot be converted.",
                "A converted quote cannot be deleted; delete the order it became.",
                "Enterprise is required."
            ];

            return allowed.Contains(message, StringComparer.OrdinalIgnoreCase)
                ? message
                : "An internal error occurred. Please try again later.";
        }

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }
    }
}
