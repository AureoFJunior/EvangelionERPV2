using System.Security.Claims;
using AutoMapper;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class PayableBillsController : Controller
    {
        private readonly IPayableBillService _payableBillService;
        private readonly IMapper _mapper;

        public PayableBillsController(IPayableBillService payableBillService, IMapper mapper)
        {
            _payableBillService = payableBillService;
            _mapper = mapper;
        }

        [HttpGet("{pageNumber?}/{pageSize?}")]
        public async Task<IActionResult> GetPayableBills(int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var bills = await _payableBillService.GetByEnterpriseIdAsync(enterpriseId, pageNumber, pageSize);
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
        public async Task<IActionResult> AddPayableBill([FromBody] PayableBill payableBill)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                payableBill.EnterpriseId = enterpriseId;
                var created = await _payableBillService.CreateAsync(payableBill);
                return Ok(_mapper.Map<PayableBillDTO>(created));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error(ex, "Invalid payable bill payload");
                return BadRequest(ex.Message);
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPut]
        public async Task<IActionResult> UpdatePayableBill([FromBody] PayableBill payableBill)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var updated = await _payableBillService.UpdateAsync(payableBill, enterpriseId);
                return Ok(_mapper.Map<PayableBillDTO>(updated));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error(ex, "Invalid payable bill update");
                return BadRequest(ex.Message);
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
                return BadRequest(ex.Message);
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

                var deleted = await _payableBillService.DeleteAsync(id, enterpriseId);
                return Ok(_mapper.Map<PayableBillDTO>(deleted));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error(ex, "Invalid payable bill delete");
                return BadRequest(ex.Message);
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

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }
    }
}
