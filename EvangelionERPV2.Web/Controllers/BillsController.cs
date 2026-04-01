using AutoMapper;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Security.Claims;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class BillsController : Controller
    {
        private readonly IBillsService<Bill> _billService;
        private readonly IOrderService<Order> _orderService;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;

        public BillsController(
            IBillsService<Bill> billService,
            IOrderService<Order> orderService,
            IRepository<User> userRepository,
            IMapper mapper)
        {
            _billService = billService;
            _orderService = orderService;
            _userRepository = userRepository;
            _mapper = mapper;
        }

        [HttpGet("{orderId}")]
        [ProducesResponseType(typeof(BillDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetByOrder(Guid orderId)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var accessLevel = await ResolveAccessLevelAsync(TryGetUserId(), enterpriseId);
                if (!IsManagementAccess(accessLevel))
                    return Forbid();

                var order = await _orderService.GetByIdAsync(orderId, enterpriseId);
                if (order == null)
                    return NoContent();

                var bill = await _billService.GetByOrderIdAsync(orderId);
                if (bill == null)
                    return NoContent();

                var dto = _mapper.Map<BillDTO>(bill);
                return Ok(dto);
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPost("{orderId}")]
        [ProducesResponseType(typeof(BillDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Generate(Guid orderId)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var accessLevel = await ResolveAccessLevelAsync(TryGetUserId(), enterpriseId);
                if (!IsManagementAccess(accessLevel))
                    return Forbid();

                var order = await _orderService.GetByIdAsync(orderId, enterpriseId);
                if (order == null)
                    return NoContent();

                var bill = await _billService.GenerateAsync(orderId);
                if (bill == null)
                    return NoContent();

                var dto = _mapper.Map<BillDTO>(bill);
                return Ok(dto);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Order not found for bill generation. OrderId={OrderId} ErrorType={ErrorType}", orderId, exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpGet("{orderId}")]
        [Produces("application/pdf")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Pdf(Guid orderId)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var order = await _orderService.GetByIdAsync(orderId, enterpriseId);
                if (order == null)
                    return NoContent();

                var pdfBytes = await _billService.GetPdfAsync(orderId);
                if (pdfBytes == null || pdfBytes.Length == 0)
                    return NoContent();

                return File(pdfBytes, "application/pdf", $"bill-{orderId}.pdf");
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Order not found for bill PDF generation. OrderId={OrderId} ErrorType={ErrorType}", orderId, exnf.GetType().Name);
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

            return user.AccessLevel;
        }

        private static bool IsManagementAccess(short? accessLevel)
        {
            return accessLevel.HasValue && accessLevel.Value <= (short)EnumAccessLevel.Supervisor;
        }
    }
}

