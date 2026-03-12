using AutoMapper;
using EvangelionERPV2.AuditModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class AuditTrailController : Controller
    {
        private readonly IAuditTrailService _auditTrailService;
        private readonly IMapper _mapper;

        public AuditTrailController(IAuditTrailService auditTrailService, IMapper mapper)
        {
            _auditTrailService = auditTrailService;
            _mapper = mapper;
        }

        [HttpGet("{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(PaginatedResultDTO<AuditTrailDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAuditTrails(int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                var (auditTrails, totalItems) = await _auditTrailService.GetAllAsyncFiltering(
                    descending: true,
                    pageNumber: pageNumber,
                    pageSize: pageSize);

                var data = auditTrails.ToList();
                if (data.Count == 0)
                    return NoContent();

                var dto = _mapper.Map<IEnumerable<AuditTrailDTO>>(data);
                return Ok(dto.ToPaginatedResult(pageNumber ?? 1, pageSize ?? 50, totalItems));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPost("{descending}/{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(PaginatedResultDTO<AuditTrailDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAuditTrailsByFilter(
            [FromBody] AuditTrailFilterDTO filter,
            bool descending = true,
            int? pageNumber = null,
            int? pageSize = null)
        {
            try
            {
                var (auditTrails, totalItems) = await _auditTrailService.GetAllAsyncFiltering(
                    descending: descending,
                    pageNumber: pageNumber,
                    pageSize: pageSize,
                    filter: filter);

                var data = auditTrails.ToList();
                if (data.Count == 0)
                    return NoContent();

                var dto = _mapper.Map<IEnumerable<AuditTrailDTO>>(data);
                return Ok(dto.ToPaginatedResult(pageNumber ?? 1, pageSize ?? 50, totalItems));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(AuditTrailDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAuditTrail(Guid id)
        {
            try
            {
                AuditTrail? auditTrail = await _auditTrailService.GetByIdAsync(id);
                if (auditTrail == null)
                    return NoContent();

                var dto = _mapper.Map<AuditTrailDTO>(auditTrail);
                return Ok(dto);
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
