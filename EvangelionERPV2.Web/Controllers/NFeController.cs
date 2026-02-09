using AutoMapper;
using EvangelionERPV2.NFeModule.Application.Interface;
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
    public class NFeController : Controller
    {
        private readonly INFeService<NFeDocument> _nfeService;
        private readonly IMapper _mapper;

        public NFeController(INFeService<NFeDocument> nfeService, IMapper mapper)
        {
            _nfeService = nfeService;
            _mapper = mapper;
        }

        [HttpGet("{orderId}")]
        [ProducesResponseType(typeof(NFeDocumentDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetByOrder(Guid orderId, [FromQuery] NFeDocumentType? type = null)
        {
            try
            {
                var document = await _nfeService.GetByOrderIdAsync(orderId, type);
                if (document == null)
                    return NoContent();

                var dto = _mapper.Map<NFeDocumentDTO>(document);
                return Ok(dto);
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when getting NFe document for Order {orderId}", ex);
                return Problem(ex.Message);
            }
        }

        [HttpPost("{orderId}")]
        [ProducesResponseType(typeof(NFeDocumentDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Issue(Guid orderId, [FromQuery] NFeDocumentType type = NFeDocumentType.NFe)
        {
            try
            {
                var document = await _nfeService.IssueAsync(orderId, type);
                if (document == null)
                    return NoContent();

                var dto = _mapper.Map<NFeDocumentDTO>(document);
                return Ok(dto);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error($"Order not found for NFe issuance: {orderId}", exnf);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when issuing NFe document for Order {orderId}", ex);
                return Problem(ex.Message);
            }
        }

        [HttpGet("{accessKey}")]
        [ProducesResponseType(typeof(NFeDocumentDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Consult(string accessKey)
        {
            try
            {
                var document = await _nfeService.ConsultAsync(accessKey);
                if (document == null)
                    return NoContent();

                var dto = _mapper.Map<NFeDocumentDTO>(document);
                return Ok(dto);
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when consulting NFe document {accessKey}", ex);
                return Problem(ex.Message);
            }
        }

        [HttpPost("{accessKey}")]
        [ProducesResponseType(typeof(NFeDocumentDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Cancel(string accessKey, [FromBody] NFeCancelRequestDTO request)
        {
            try
            {
                var reason = request?.Reason ?? string.Empty;
                var document = await _nfeService.CancelAsync(accessKey, reason);
                if (document == null)
                    return NoContent();

                var dto = _mapper.Map<NFeDocumentDTO>(document);
                return Ok(dto);
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when cancelling NFe document {accessKey}", ex);
                return Problem(ex.Message);
            }
        }
    }
}
