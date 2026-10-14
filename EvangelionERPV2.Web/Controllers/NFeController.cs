using AutoMapper;
using EvangelionERPV2.NFeModule.Application.Interface;
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
using System.Text.RegularExpressions;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class NFeController : Controller
    {
        private const int MaxNFeCancelRequestBodySizeInBytes = 16 * 1024;
        private const int NFeAccessKeyLength = 44;
        private const int MaxNFeCancelReasonLength = 500;
        private static readonly Regex NFeAccessKeyRegex = new($"^[0-9]{{{NFeAccessKeyLength}}}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly INFeService<NFeDocument> _nfeService;
        private readonly IOrderService<Order> _orderService;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;

        public NFeController(
            INFeService<NFeDocument> nfeService,
            IOrderService<Order> orderService,
            IRepository<User> userRepository,
            IMapper mapper)
        {
            _nfeService = nfeService;
            _orderService = orderService;
            _userRepository = userRepository;
            _mapper = mapper;
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.NFe.Read)]
        [HttpGet("{orderId}")]
        [ProducesResponseType(typeof(NFeDocumentDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetByOrder(Guid orderId, [FromQuery] NFeDocumentType? type = null)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var order = await _orderService.GetByIdAsync(orderId, enterpriseId);
                if (order == null)
                    return NoContent();

                var document = await _nfeService.GetByOrderIdAsync(orderId, enterpriseId, type);
                if (document == null)
                    return NoContent();

                var dto = _mapper.Map<NFeDocumentDTO>(document);
                return Ok(dto);
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.NFe.Issue)]
        [HttpPost("{orderId}")]
        [ProducesResponseType(typeof(NFeDocumentDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Issue(Guid orderId, [FromQuery] NFeDocumentType type = NFeDocumentType.NFe)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var order = await _orderService.GetByIdAsync(orderId, enterpriseId);
                if (order == null)
                    return NoContent();

                var document = await _nfeService.IssueAsync(orderId, enterpriseId, type);
                if (document == null)
                    return NoContent();

                var dto = _mapper.Map<NFeDocumentDTO>(document);
                return Ok(dto);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Order not found for NFe issuance. OrderId={OrderId} ErrorType={ErrorType}", orderId, exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.NFe.Read)]
        [HttpGet("{accessKey}")]
        [ProducesResponseType(typeof(NFeDocumentDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Consult(string accessKey)
        {
            try
            {
                if (!IsValidAccessKey(accessKey))
                    return BadRequest($"accessKey must be exactly {NFeAccessKeyLength} numeric characters.");

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var document = await _nfeService.ConsultAsync(accessKey, enterpriseId);
                if (document == null)
                    return NoContent();

                var order = await _orderService.GetByIdAsync(document.OrderId, enterpriseId);
                if (order == null)
                    return NoContent();

                var dto = _mapper.Map<NFeDocumentDTO>(document);
                return Ok(dto);
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.NFe.Cancel)]
        [HttpPost("{accessKey}")]
        [RequestSizeLimit(MaxNFeCancelRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(NFeDocumentDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Cancel(string accessKey, [FromBody] NFeCancelRequestDTO request)
        {
            try
            {
                if (!IsValidAccessKey(accessKey))
                    return BadRequest($"accessKey must be exactly {NFeAccessKeyLength} numeric characters.");

                var reason = (request?.Reason ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(reason))
                    return BadRequest("Reason is required.");

                if (reason.Length > MaxNFeCancelReasonLength)
                    return BadRequest($"Reason must be {MaxNFeCancelReasonLength} characters or fewer.");

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var existingDocument = await _nfeService.ConsultAsync(accessKey, enterpriseId);
                if (existingDocument == null)
                    return NoContent();

                var order = await _orderService.GetByIdAsync(existingDocument.OrderId, enterpriseId);
                if (order == null)
                    return NoContent();

                var document = await _nfeService.CancelAsync(accessKey, enterpriseId, reason);
                if (document == null)
                    return NoContent();

                var dto = _mapper.Map<NFeDocumentDTO>(document);
                return Ok(dto);
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




        private static bool IsValidAccessKey(string accessKey)
        {
            var normalizedAccessKey = (accessKey ?? string.Empty).Trim();
            return NFeAccessKeyRegex.IsMatch(normalizedAccessKey);
        }
    }
}
