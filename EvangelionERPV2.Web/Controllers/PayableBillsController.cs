using System.Security.Claims;
using AutoMapper;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
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
        private readonly IPayableBillService _payableBillService;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;

        public PayableBillsController(
            IPayableBillService payableBillService,
            IRepository<User> userRepository,
            IMapper mapper)
        {
            _payableBillService = payableBillService;
            _userRepository = userRepository;
            _mapper = mapper;
        }

        [HttpGet("{pageNumber?}/{pageSize?}")]
        public async Task<IActionResult> GetPayableBills(
            int? pageNumber = null,
            int? pageSize = null,
            [FromQuery] string? isActive = null,
            [FromQuery] int? billType = null)
        {
            try
            {
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

                var bills = await _payableBillService.GetByEnterpriseIdAsync(enterpriseId, pageNumber, pageSize, parsedIsActive, billType);
                return Ok(_mapper.Map<IEnumerable<PayableBillDTO>>(bills));
            }
            catch (Exception)
            {
                throw;
            }
        }

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

                return Ok(_mapper.Map<PayableBillDTO>(bill));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddPayableBill([FromBody] UpsertPayableBillRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid || request == null)
                    return BadRequest(ModelState);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var payableBill = MapRequestToPayableBill(request, enterpriseId);
                payableBill.EnterpriseId = enterpriseId;
                var created = await _payableBillService.CreateAsync(payableBill);
                return Ok(_mapper.Map<PayableBillDTO>(created));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error(ex, "Invalid payable bill payload");
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPut]
        public async Task<IActionResult> UpdatePayableBill([FromBody] UpsertPayableBillRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid || request == null)
                    return BadRequest(ModelState);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                if (!request.Id.HasValue || request.Id.Value == Guid.Empty)
                    return BadRequest("Payable bill id is required.");

                var payableBill = MapRequestToPayableBill(request, enterpriseId);
                var updated = await _payableBillService.UpdateAsync(payableBill, enterpriseId);
                return Ok(_mapper.Map<PayableBillDTO>(updated));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error(ex, "Invalid payable bill update");
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (NotFoundDatabaseException ex)
            {
                Log.Logger.Error(ex, "Payable bill not found");
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPost("{id}")]
        public async Task<IActionResult> MarkProductsReceived(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var updated = await _payableBillService.MarkProductsReceivedAsync(id, enterpriseId);
                return Ok(_mapper.Map<PayableBillDTO>(updated));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error(ex, "Invalid receive action for payable bill");
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (NotFoundDatabaseException ex)
            {
                Log.Logger.Error(ex, "Payable bill not found");
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPost("{id}")]
        public async Task<IActionResult> RefundPayableBill(Guid id, [FromBody] RefundRequestDTO request)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var reason = request?.Reason ?? string.Empty;
                var updated = await _payableBillService.RefundAsync(id, enterpriseId, reason);
                return Ok(_mapper.Map<PayableBillDTO>(updated));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error(ex, "Invalid refund action for payable bill");
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (NotFoundDatabaseException ex)
            {
                Log.Logger.Error(ex, "Payable bill not found");
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPost]
        public async Task<IActionResult> GetReplenishmentSuggestions([FromBody] ReplenishmentSuggestionRequestDTO? request)
        {
            try
            {
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

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePayableBill(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var userId = TryGetUserId();
                var accessLevel = await ResolveAccessLevelAsync(userId, enterpriseId);
                if (!IsAdminAccess(accessLevel))
                    return Forbid();

                var deleted = await _payableBillService.DeleteAsync(id, enterpriseId);
                return Ok(_mapper.Map<PayableBillDTO>(deleted));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error(ex, "Invalid payable bill delete");
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (NotFoundDatabaseException ex)
            {
                Log.Logger.Error(ex, "Payable bill not found");
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

        private Guid? TryGetUserId()
        {
            var claimValue = User.FindFirst(ClaimTypes.Sid)?.Value
                             ?? User.FindFirst("uid")?.Value;

            if (Guid.TryParse(claimValue, out var userId) && userId != Guid.Empty)
                return userId;

            return null;
        }

        private async Task<short?> ResolveAccessLevelAsync(Guid? userId, Guid enterpriseId)
        {
            if (!userId.HasValue || enterpriseId == Guid.Empty)
                return null;

            var user = await _userRepository.GetByIdAsync(userId.Value);
            if (user == null || user.IsActive != true || user.EnterpriseId != enterpriseId)
                return null;

            return user?.AccessLevel;
        }

        private static bool IsAdminAccess(short? accessLevel)
        {
            return accessLevel.HasValue && accessLevel.Value == (short)EnumAccessLevel.Admin;
        }

        private static string GetSafeInsertErrorMessage(InsertDatabaseException ex)
        {
            return ex.InnerException == null
                ? ex.Message
                : "An internal error occurred. Please try again later.";
        }
    }
}
