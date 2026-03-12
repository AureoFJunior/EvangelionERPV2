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
    public class BillsController : Controller
    {
        private readonly IBillsService<Bill> _billService;
        private readonly IMapper _mapper;

        public BillsController(IBillsService<Bill> billService, IMapper mapper)
        {
            _billService = billService;
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
                var bill = await _billService.GenerateAsync(orderId);
                if (bill == null)
                    return NoContent();

                var dto = _mapper.Map<BillDTO>(bill);
                return Ok(dto);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error(exnf, $"Order not found for bill generation: {orderId}");
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
                var pdfBytes = await _billService.GetPdfAsync(orderId);
                if (pdfBytes == null || pdfBytes.Length == 0)
                    return NoContent();

                return File(pdfBytes, "application/pdf", $"bill-{orderId}.pdf");
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error(exnf, $"Order not found for bill PDF generation: {orderId}");
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}

