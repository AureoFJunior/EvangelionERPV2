using System.Security.Claims;
using AutoMapper;
using EvangelionERPV2.EmployeeModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
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
    /// The férias entitlement ledger: read a period, book leave against it, or sell days.
    /// </summary>
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class EmployeeVacationController : Controller
    {
        private const int MaxPageSize = 200;
        private const int MaxVacationWriteRequestBodySizeInBytes = 8 * 1024;

        private readonly IEmployeeVacationService<EmployeeVacationAccrual> _vacationService;
        private readonly IRepository<EmployeeVacationAccrual> _repository;
        private readonly IRepository<Employee> _employeeRepository;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;

        public EmployeeVacationController(
            IEmployeeVacationService<EmployeeVacationAccrual> vacationService,
            IRepository<EmployeeVacationAccrual> repository,
            IRepository<Employee> employeeRepository,
            IRepository<User> userRepository,
            IMapper mapper)
        {
            _vacationService = vacationService;
            _repository = repository;
            _employeeRepository = employeeRepository;
            _userRepository = userRepository;
            _mapper = mapper;
        }

        /// <summary>
        /// Return the enterprise's vacation accruals, optionally narrowed to one employee.
        /// </summary>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <param name="employeeId">Optional employee filter.</param>
        [Authorize(Policy = "rbac:" + RbacPermissions.EmployeeVacations.Read)]
        [HttpGet("{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<EmployeeVacationAccrualDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetVacationAccruals(int? pageNumber = null, int? pageSize = null, [FromQuery] Guid? employeeId = null)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
                IEnumerable<EmployeeVacationAccrual> accruals = await _repository.GetAllAsync(
                    normalizedPageNumber,
                    normalizedPageSize,
                    x => x.IsActive == true
                         && x.EnterpriseId != null
                         && x.EnterpriseId == enterpriseId
                         && (!employeeId.HasValue || x.EmployeeId == employeeId.Value));

                if (accruals == null)
                    return NoContent();

                return Ok(Map(accruals));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Employee vacation accruals not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Recompute one employee's ledger: open any period their tenure has reached, recalculate
        /// each entitlement from the absences inside it, and settle leave that has since elapsed.
        /// </summary>
        /// <param name="employeeId">Employee whose ledger to rebuild</param>
        [Authorize(Policy = "rbac:" + RbacPermissions.EmployeeVacations.Schedule)]
        [HttpPost("{employeeId}")]
        [ProducesResponseType(typeof(IEnumerable<EmployeeVacationAccrualDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> RefreshVacationAccruals(Guid employeeId)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                if (!await HasActiveTenantMembershipAsync(TryGetUserId(), enterpriseId))
                    return Unauthorized();
                if (!await BelongsToTenantAsync(employeeId, enterpriseId))
                    return NoContent();

                var ledger = await _vacationService.RefreshAsync(employeeId, enterpriseId);
                return Ok(Map(ledger));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Employee not found while refreshing vacation accruals. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (InsertDatabaseException exdb)
            {
                Log.Logger.Error("Employee vacation ledger refresh refused. ErrorType={ErrorType}", exdb.GetType().Name);
                return BadRequest(exdb.Message);
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Book a stretch of férias against a período aquisitivo.
        /// </summary>
        /// <param name="request">Period to be scheduled</param>
        [Authorize(Policy = "rbac:" + RbacPermissions.EmployeeVacations.Schedule)]
        [HttpPost]
        [RequestSizeLimit(MaxVacationWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(EmployeeVacationAccrualDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ScheduleVacation([FromBody] ScheduleVacationRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest();
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                if (!await HasActiveTenantMembershipAsync(TryGetUserId(), enterpriseId))
                    return Unauthorized();

                var accrual = await _vacationService.ScheduleAsync(request.AccrualId, enterpriseId, request.StartDate, request.Days);
                return Ok(Map(accrual));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Vacation accrual not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (InsertDatabaseException exdb)
            {
                Log.Logger.Error("Férias schedule refused. ErrorType={ErrorType}", exdb.GetType().Name);
                return BadRequest(exdb.Message);
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Record an abono pecuniário — days sold instead of taken (CLT art. 143).
        /// </summary>
        /// <param name="request">Days to be sold</param>
        [Authorize(Policy = "rbac:" + RbacPermissions.EmployeeVacations.Schedule)]
        [HttpPost]
        [RequestSizeLimit(MaxVacationWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(EmployeeVacationAccrualDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SellVacationDays([FromBody] SellVacationDaysRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest();
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                if (!await HasActiveTenantMembershipAsync(TryGetUserId(), enterpriseId))
                    return Unauthorized();

                var accrual = await _vacationService.SellDaysAsync(request.AccrualId, enterpriseId, request.Days);
                return Ok(Map(accrual));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Vacation accrual not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (InsertDatabaseException exdb)
            {
                Log.Logger.Error("Abono pecuniário refused. ErrorType={ErrorType}", exdb.GetType().Name);
                return BadRequest(exdb.Message);
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// AutoMapper cannot fill <c>RemainingDays</c> from a <c>[NotMapped]</c> expression-bodied
        /// property by convention, so the projection is written out once here.
        /// </summary>
        private EmployeeVacationAccrualDTO Map(EmployeeVacationAccrual accrual)
        {
            var dto = _mapper.Map<EmployeeVacationAccrualDTO>(accrual);
            dto.RemainingDays = accrual.RemainingDays;
            return dto;
        }

        private IEnumerable<EmployeeVacationAccrualDTO> Map(IEnumerable<EmployeeVacationAccrual> accruals) =>
            accruals.Select(Map).ToList();

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
