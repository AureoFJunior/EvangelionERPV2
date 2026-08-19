using System.Security.Claims;
using AutoMapper;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Linq;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class PayableBillsController : Controller
    {
        private const int MaxPageSize = 200;
        private const int MaxPayableBillWriteRequestBodySizeInBytes = 1 * 1024 * 1024;
        private const int MaxRefundReasonLength = 500;

        private readonly IPayableBillService _payableBillService;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;
        private readonly IAuthorizationService _authorizationService;

        public PayableBillsController(
            IPayableBillService payableBillService,
            IRepository<User> userRepository,
            IMapper mapper,
            IAuthorizationService authorizationService)
        {
            _payableBillService = payableBillService;
            _userRepository = userRepository;
            _mapper = mapper;
            _authorizationService = authorizationService;
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.PayableBills.Read)]
        [HttpGet("{pageNumber?}/{pageSize?}")]
        public async Task<IActionResult> GetPayableBills(
            int? pageNumber = null,
            int? pageSize = null,
            [FromQuery] string? isActive = null,
            [FromQuery] int? billType = null,
            [FromQuery] int? year = null,
            [FromQuery] int? month = null)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (year.HasValue != month.HasValue)
                    return BadRequest("year and month must be provided together.");
                if (month is < 1 or > 12)
                    return BadRequest("month must be between 1 and 12.");
                if (year is < 2000 or > 2100)
                    return BadRequest("year must be between 2000 and 2100.");

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                bool? parsedIsActive = true;
                if (Request.Query.ContainsKey("isActive"))
                {
                    var normalized = isActive?.Trim();
                    if (string.IsNullOrWhiteSpace(normalized) || normalized.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        parsedIsActive = null;
                    }
                    else if (bool.TryParse(normalized, out var parsed))
                    {
                        parsedIsActive = parsed;
                    }
                    else
                    {
                        return BadRequest("isActive must be true, false or all.");
                    }
                }

                // LGPD: payroll bills carry individual salary data — visible only with payroll.read.
                // The filter is pushed into the query: stripping them after paging would leak
                // how many payroll bills exist through the returned-vs-requested page size.
                var canAccessPayroll = await CanAccessPayrollAsync();
                if (!canAccessPayroll && billType == (int)EnumPayableBillType.Payroll)
                    return Forbid();

                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
                var bills = await _payableBillService.GetByEnterpriseIdAsync(
                    enterpriseId,
                    normalizedPageNumber,
                    normalizedPageSize,
                    parsedIsActive,
                    billType,
                    year,
                    month,
                    canAccessPayroll);

                return Ok(_mapper.Map<IEnumerable<PayableBillDTO>>(bills));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.PayableBills.Read)]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetPayableBill(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var bill = await _payableBillService.GetByIdAsync(id, enterpriseId);
                if (bill == null)
                    return NoContent();

                // LGPD: hide payroll bills (salary data) from users without payroll.read.
                if (IsPayrollBill(bill.BillType) && !await CanAccessPayrollAsync())
                    return NoContent();

                return Ok(_mapper.Map<PayableBillDTO>(bill));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.PayableBills.Create)]
        [HttpPost]
        [RequestSizeLimit(MaxPayableBillWriteRequestBodySizeInBytes)]
        public async Task<IActionResult> AddPayableBill([FromBody] UpsertPayableBillRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid || request == null)
                    return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                // Payroll bills are only created through PayrollController (payroll.generate).
                if (IsPayrollBill(request.BillType) && !await CanAccessPayrollAsync())
                    return Forbid();

                var payableBill = MapRequestToPayableBill(request, enterpriseId);
                payableBill.EnterpriseId = enterpriseId;
                var created = await _payableBillService.CreateAsync(payableBill);
                return Ok(_mapper.Map<PayableBillDTO>(created));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid payable bill payload. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.PayableBills.Update)]
        [HttpPut]
        [RequestSizeLimit(MaxPayableBillWriteRequestBodySizeInBytes)]
        public async Task<IActionResult> UpdatePayableBill([FromBody] UpsertPayableBillRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid || request == null)
                    return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                if (!request.Id.HasValue || request.Id.Value == Guid.Empty)
                    return BadRequest("Payable bill id is required.");

                if (!await CanAccessPayrollAsync())
                {
                    if (IsPayrollBill(request.BillType))
                        return Forbid();

                    // Same response as a missing bill so payroll bill ids are not disclosed.
                    var existing = await _payableBillService.GetByIdAsync(request.Id.Value, enterpriseId);
                    if (existing != null && IsPayrollBill(existing.BillType))
                        return NoContent();
                }

                var payableBill = MapRequestToPayableBill(request, enterpriseId);
                var updated = await _payableBillService.UpdateAsync(payableBill, enterpriseId);
                return Ok(_mapper.Map<PayableBillDTO>(updated));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid payable bill update. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (NotFoundDatabaseException ex)
            {
                Log.Logger.Error("Payable bill not found. ErrorType={ErrorType}", ex.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.PayableBills.MarkProductsReceived)]
        [HttpPost("{id}")]
        public async Task<IActionResult> MarkProductsReceived(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                if (await IsHiddenPayrollBillAsync(id, enterpriseId))
                    return NoContent();

                var updated = await _payableBillService.MarkProductsReceivedAsync(id, enterpriseId);
                return Ok(_mapper.Map<PayableBillDTO>(updated));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid receive action for payable bill. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (NotFoundDatabaseException ex)
            {
                Log.Logger.Error("Payable bill not found. ErrorType={ErrorType}", ex.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.PayableBills.Refund)]
        [HttpPost("{id}")]
        [RequestSizeLimit(MaxPayableBillWriteRequestBodySizeInBytes)]
        public async Task<IActionResult> RefundPayableBill(Guid id, [FromBody] RefundRequestDTO request)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var reason = request?.Reason ?? string.Empty;
                if (reason.Length > MaxRefundReasonLength)
                    return BadRequest($"Reason must be {MaxRefundReasonLength} characters or fewer.");

                if (await IsHiddenPayrollBillAsync(id, enterpriseId))
                    return NoContent();

                var updated = await _payableBillService.RefundAsync(id, enterpriseId, reason);
                return Ok(_mapper.Map<PayableBillDTO>(updated));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid refund action for payable bill. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (NotFoundDatabaseException ex)
            {
                Log.Logger.Error("Payable bill not found. ErrorType={ErrorType}", ex.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.PayableBills.Read)]
        [HttpPost]
        [RequestSizeLimit(MaxPayableBillWriteRequestBodySizeInBytes)]
        public async Task<IActionResult> GetReplenishmentSuggestions([FromBody] ReplenishmentSuggestionRequestDTO? request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var suggestions = await _payableBillService.GetReplenishmentSuggestionsAsync(
                    enterpriseId,
                    request ?? new ReplenishmentSuggestionRequestDTO());

                if (!suggestions.Any())
                    return NoContent();

                return Ok(suggestions);
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.PayableBills.Delete)]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePayableBill(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                if (await IsHiddenPayrollBillAsync(id, enterpriseId))
                    return NoContent();

                var deleted = await _payableBillService.DeleteAsync(id, enterpriseId);
                return Ok(_mapper.Map<PayableBillDTO>(deleted));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid payable bill delete. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (NotFoundDatabaseException ex)
            {
                Log.Logger.Error("Payable bill not found. ErrorType={ErrorType}", ex.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        private static PayableBill MapRequestToPayableBill(UpsertPayableBillRequestDTO request, Guid enterpriseId)
        {
            var items = request.Items?.Select(item => new PayableBillProduct
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitValue = item.UnitValue
            }).ToList();

            return new PayableBill
            {
                Id = request.Id ?? Guid.Empty,
                Description = request.Description,
                BillType = request.BillType,
                DueDate = request.DueDate,
                IsPaid = request.IsPaid,
                PaidAt = request.PaidAt,
                Amount = request.Amount ?? 0,
                EnterpriseId = enterpriseId,
                Items = items
            };
        }

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }

        private static bool IsPayrollBill(int billType)
        {
            return billType == (int)EnumPayableBillType.Payroll;
        }

        private async Task<bool> CanAccessPayrollAsync()
        {
            var result = await _authorizationService.AuthorizeAsync(User, RbacPolicies.Payroll.Read);
            return result.Succeeded;
        }

        private async Task<bool> IsHiddenPayrollBillAsync(Guid id, Guid enterpriseId)
        {
            var existing = await _payableBillService.GetByIdAsync(id, enterpriseId);
            return existing != null
                   && IsPayrollBill(existing.BillType)
                   && !await CanAccessPayrollAsync();
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

            if (message.StartsWith("Product [", StringComparison.OrdinalIgnoreCase) &&
                message.EndsWith("] was not found.", StringComparison.OrdinalIgnoreCase))
            {
                return "A product for this payable bill was not found.";
            }

            if (string.Equals(message, "Payable bill enterprise is required.", StringComparison.OrdinalIgnoreCase))
                return "Payable bill enterprise is required.";

            if (string.Equals(message, "Payable bill is refunded and cannot be edited.", StringComparison.OrdinalIgnoreCase))
                return "Payable bill is refunded and cannot be edited.";

            if (string.Equals(message, "Products were already received. Item lines cannot be changed.", StringComparison.OrdinalIgnoreCase))
                return "Products were already received. Item lines cannot be changed.";

            if (string.Equals(message, "Refunded payable bills cannot receive products.", StringComparison.OrdinalIgnoreCase))
                return "Refunded payable bills cannot receive products.";

            if (string.Equals(message, "Products were already received for this payable bill.", StringComparison.OrdinalIgnoreCase))
                return "Products were already received for this payable bill.";

            if (string.Equals(message, "Payable bill has no product items to receive.", StringComparison.OrdinalIgnoreCase))
                return "Payable bill has no product items to receive.";

            if (string.Equals(message, "Some products were not found for this payable bill.", StringComparison.OrdinalIgnoreCase))
                return "Some products were not found for this payable bill.";

            if (string.Equals(message, "Payable bill was already refunded.", StringComparison.OrdinalIgnoreCase))
                return "Payable bill was already refunded.";

            if (string.Equals(message, "Refund reason is required.", StringComparison.OrdinalIgnoreCase))
                return "Refund reason is required.";

            if (string.Equals(message, "Refund is only allowed when payable bill has product items.", StringComparison.OrdinalIgnoreCase))
                return "Refund is only allowed when payable bill has product items.";

            if (string.Equals(message, "Payable bill refund failed.", StringComparison.OrdinalIgnoreCase))
                return "Payable bill refund failed.";

            if (string.Equals(message, "Some products for this payable bill were not found.", StringComparison.OrdinalIgnoreCase))
                return "Some products for this payable bill were not found.";

            if (string.Equals(message, "Payable bill deletion failed.", StringComparison.OrdinalIgnoreCase))
                return "Payable bill deletion failed.";

            if (string.Equals(message, "Each payable item must have quantity greater than zero.", StringComparison.OrdinalIgnoreCase))
                return "Each payable item must have quantity greater than zero.";

            if (string.Equals(message, "Payable bill has duplicate products.", StringComparison.OrdinalIgnoreCase))
                return "Payable bill has duplicate products.";

            if (string.Equals(message, "Some payable bill products were not found in enterprise inventory.", StringComparison.OrdinalIgnoreCase))
                return "Some payable bill products were not found in enterprise inventory.";

            if (string.Equals(message, "Invalid payable bill type.", StringComparison.OrdinalIgnoreCase))
                return "Invalid payable bill type.";

            if (string.Equals(message, "Enterprise was not found for balance update.", StringComparison.OrdinalIgnoreCase))
                return "Enterprise was not found for balance update.";

            return "An internal error occurred. Please try again later.";
        }
    }
}
