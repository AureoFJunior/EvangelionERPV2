using AutoMapper;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Security;
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

        [Authorize(Policy = "rbac:" + RbacPermissions.Bills.Read)]
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
                var order = await _orderService.GetByIdAsync(orderId, enterpriseId);
                if (order == null)
                    return NoContent();

                var bill = await _billService.GetByOrderIdAsync(orderId, enterpriseId);
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

        [Authorize(Policy = "rbac:" + RbacPermissions.Bills.Generate)]
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
                var order = await _orderService.GetByIdAsync(orderId, enterpriseId);
                if (order == null)
                    return NoContent();

                var bill = await _billService.GenerateAsync(orderId, enterpriseId);
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

        [Authorize(Policy = "rbac:" + RbacPermissions.Bills.DownloadPdf)]
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

                var pdfBytes = await _billService.GetPdfAsync(orderId, enterpriseId);
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




    }
}
