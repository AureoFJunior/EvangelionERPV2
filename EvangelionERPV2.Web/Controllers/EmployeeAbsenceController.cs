using System.Security.Claims;
using AutoMapper;
using EvangelionERPV2.EmployeeModule.Application.Interface;
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

namespace EvangelionERPV2.Web.Controllers
{
    /// <summary>
    /// CRUD over the absence spine: faltas, atestados and advertências.
    /// </summary>
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class EmployeeAbsenceController : Controller
    {
        private const int MaxPageSize = 200;
        private const int MaxAbsenceWriteRequestBodySizeInBytes = 32 * 1024;

        private readonly IEmployeeAbsenceService<EmployeeAbsence> _absenceService;
        private readonly IRepository<EmployeeAbsence> _repository;
        private readonly IRepository<Employee> _employeeRepository;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;

        public EmployeeAbsenceController(IEmployeeAbsenceService<EmployeeAbsence> absenceService,
            IRepository<EmployeeAbsence> repository,
            IRepository<Employee> employeeRepository,
            IRepository<User> userRepository,
            IMapper mapper)
        {
            _absenceService = absenceService;
            _repository = repository;
            _employeeRepository = employeeRepository;
            _userRepository = userRepository;
            _mapper = mapper;
        }

        /// <summary>
        /// Return the enterprise's absences, optionally narrowed to one employee.
        /// </summary>
        /// <param name="employeeId">Optional employee filter.</param>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        [Authorize(Policy = "rbac:" + RbacPermissions.EmployeeAbsences.Read)]
        [HttpGet("{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<EmployeeAbsenceDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEmployeeAbsences(int? pageNumber = null, int? pageSize = null, [FromQuery] Guid? employeeId = null)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
                IEnumerable<EmployeeAbsence> absences = await _repository.GetAllAsync(
                    normalizedPageNumber,
                    normalizedPageSize,
                    x => x.IsActive == true
                         && x.EnterpriseId != null
                         && x.EnterpriseId == enterpriseId
                         && (!employeeId.HasValue || x.EmployeeId == employeeId.Value));

                if (absences == null)
                    return NoContent();

                return Ok(_mapper.Map<IEnumerable<EmployeeAbsenceDTO>>(absences));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Employee absences not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Get one absence.
        /// </summary>
        /// <param name="id">Id of the absence</param>
        [Authorize(Policy = "rbac:" + RbacPermissions.EmployeeAbsences.Read)]
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(EmployeeAbsenceDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEmployeeAbsence(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                EmployeeAbsence absence = await _repository.GetByIdAsync(id);
                if (absence == null || absence.EnterpriseId != enterpriseId || absence.IsActive != true)
                    return NoContent();

                return Ok(_mapper.Map<EmployeeAbsenceDTO>(absence));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Employee absence not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Record an absence, atestado, or advertência.
        /// </summary>
        /// <param name="request">Absence to be added</param>
        [Authorize(Policy = "rbac:" + RbacPermissions.EmployeeAbsences.Create)]
        [HttpPost]
        [RequestSizeLimit(MaxAbsenceWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(EmployeeAbsenceDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddEmployeeAbsence([FromBody] CreateEmployeeAbsenceRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest();
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                if (!await HasActiveTenantMembershipAsync(TryGetUserId(), enterpriseId))
                    return Unauthorized();
                if (!await BelongsToTenantAsync(request.EmployeeId, enterpriseId))
                    return NoContent();

                var absence = new EmployeeAbsence
                {
                    EmployeeId = request.EmployeeId,
                    Type = request.Type,
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    IsJustified = request.IsJustified ?? DefaultIsJustified(request.Type),
                    Reason = request.Reason?.Trim(),
                    EnterpriseId = enterpriseId,
                    IsActive = true
                };

                EmployeeAbsence createdAbsence = await _absenceService.CreateAsync(absence, request.Cid);
                return Ok(_mapper.Map<EmployeeAbsenceDTO>(createdAbsence));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Update an absence. The employee it belongs to cannot be changed.
        /// </summary>
        /// <param name="request">Absence to be updated</param>
        [Authorize(Policy = "rbac:" + RbacPermissions.EmployeeAbsences.Update)]
        [HttpPut]
        [RequestSizeLimit(MaxAbsenceWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(EmployeeAbsenceDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateEmployeeAbsence([FromBody] UpdateEmployeeAbsenceRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest();
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var existingAbsence = await _repository.GetByIdAsync(request.Id);
                if (existingAbsence == null || existingAbsence.EnterpriseId != enterpriseId || existingAbsence.IsActive != true)
                    return NoContent();

                existingAbsence.Type = request.Type;
                existingAbsence.StartDate = request.StartDate;
                existingAbsence.EndDate = request.EndDate;
                if (request.IsJustified.HasValue)
                    existingAbsence.IsJustified = request.IsJustified.Value;
                existingAbsence.Reason = request.Reason?.Trim();

                EmployeeAbsence updatedAbsence = _absenceService.Update(existingAbsence, request.Cid);
                if (updatedAbsence == null)
                    return NoContent();

                return Ok(_mapper.Map<EmployeeAbsenceDTO>(updatedAbsence));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Employee absence not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Delete an absence (soft delete).
        /// </summary>
        /// <param name="id">Absence's Id</param>
        [Authorize(Policy = "rbac:" + RbacPermissions.EmployeeAbsences.Delete)]
        [HttpDelete("{id}")]
        [ProducesResponseType(typeof(EmployeeAbsenceDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteEmployeeAbsence(Guid id)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var absence = await _repository.GetByIdAsync(id);
                if (absence == null || absence.EnterpriseId != enterpriseId || absence.IsActive != true)
                    return NoContent();

                EmployeeAbsence deletedAbsence = _absenceService.Delete(id);
                if (deletedAbsence == null)
                    return NoContent();

                return Ok(_mapper.Map<EmployeeAbsenceDTO>(deletedAbsence));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Employee absence not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// A falta is unjustified until someone says otherwise; an atestado is justified until
        /// someone rejects it. Advertência carries no unworked day either way.
        /// </summary>
        private static bool DefaultIsJustified(int type)
        {
            return type != (int)EnumEmployeeAbsenceType.Falta;
        }

        private async Task<bool> BelongsToTenantAsync(Guid employeeId, Guid enterpriseId)
        {
            if (employeeId == Guid.Empty)
                return false;

            try
            {
                var employee = await _employeeRepository.GetByIdAsync(employeeId);
                return employee != null && employee.EnterpriseId == enterpriseId && employee.IsActive == true;
            }
            catch (NotFoundDatabaseException)
            {
                return false;
            }
        }

        private Guid? TryGetUserId()
        {
            var claimValue = User.FindFirst(ClaimTypes.Sid)?.Value
                             ?? User.FindFirst("uid")?.Value;

            if (Guid.TryParse(claimValue, out var userId) && userId != Guid.Empty)
                return userId;

            return null;
        }

        private async Task<bool> HasActiveTenantMembershipAsync(Guid? userId, Guid enterpriseId)
        {
            if (!userId.HasValue || enterpriseId == Guid.Empty)
                return false;

            var user = await _userRepository.GetByIdAsync(userId.Value);
            return user != null && user.IsActive == true && user.EnterpriseId == enterpriseId;
        }

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }
    }
}
