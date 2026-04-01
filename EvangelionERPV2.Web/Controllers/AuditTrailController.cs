using AutoMapper;
using EvangelionERPV2.AuditModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Security.Claims;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class AuditTrailController : Controller
    {
        private const int MaxAuditTrailFilterRequestBodySizeInBytes = 16 * 1024;
        private readonly IAuditTrailService _auditTrailService;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;

        public AuditTrailController(
            IAuditTrailService auditTrailService,
            IRepository<User> userRepository,
            IMapper mapper)
        {
            _auditTrailService = auditTrailService;
            _userRepository = userRepository;
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
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var accessLevel = await ResolveAccessLevelAsync(TryGetUserId(), enterpriseId);
                if (!IsManagementAccess(accessLevel))
                    return Forbid();

                var (auditTrails, totalItems) = await _auditTrailService.GetAllAsyncFiltering(
                    enterpriseId: enterpriseId,
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
        [RequestSizeLimit(MaxAuditTrailFilterRequestBodySizeInBytes)]
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
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var accessLevel = await ResolveAccessLevelAsync(TryGetUserId(), enterpriseId);
                if (!IsManagementAccess(accessLevel))
                    return Forbid();

                var (auditTrails, totalItems) = await _auditTrailService.GetAllAsyncFiltering(
                    enterpriseId: enterpriseId,
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
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var accessLevel = await ResolveAccessLevelAsync(TryGetUserId(), enterpriseId);
                if (!IsManagementAccess(accessLevel))
                    return Forbid();

                AuditTrail? auditTrail = await _auditTrailService.GetByIdAsync(id, enterpriseId);
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
