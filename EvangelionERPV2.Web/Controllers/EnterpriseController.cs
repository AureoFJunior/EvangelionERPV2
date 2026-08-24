using Microsoft.AspNetCore.Mvc;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using AutoMapper;
using Serilog;
using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.EnterpriseModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using System.Security.Claims;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class EnterpriseController : Controller
    {
        private const int DefaultPageNumber = 1;
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;
        private const int MaxEnterpriseWriteRequestBodySizeInBytes = 64 * 1024;

        private readonly IEnterpriseRepository<Enterprise> _enterpriseRepository;
        private readonly IEnterpriseService<Enterprise> _enterpriseService;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> _repository;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;
        private readonly IAuthorizationService _authorizationService;

        public EnterpriseController(IEnterpriseService<Enterprise> enterpriseService,
            EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> repository,
            IEnterpriseRepository<Enterprise> enterpriseRepository,
            IRepository<User> userRepository,
            IMapper mapper,
            IAuthorizationService authorizationService)
        {
            _enterpriseService = enterpriseService;
            _repository = repository;
            _enterpriseRepository = enterpriseRepository;
            _userRepository = userRepository;
            _mapper = mapper;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Return all the enterprises (also works with pagination).
        /// </summary>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <returns></returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Enterprise.Read)]
        [HttpGet("{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<EnterpriseDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEnterprises(int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);

                IEnumerable<Enterprise> enterprises = await _repository.GetAllAsync(
                    normalizedPageNumber,
                    normalizedPageSize,
                    x => x.IsActive == true && x.Id == enterpriseId);
                if (enterprises == null)
                    return NoContent();

                IEnumerable<EnterpriseDTO> enterpriseDTO = _mapper.Map<IEnumerable<EnterpriseDTO>>(enterprises);
                return Ok(enterpriseDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Enterprises not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Return all the enterprises that matches the filter (also works with pagination).
        /// </summary>
        /// <param name="descending">Order by type.</param>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <param name="enterprise">Object used to filter data.</param>
        /// <returns></returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Enterprise.Read)]
        [HttpPost("{descending}/{pageNumber?}/{pageSize?}")]
        [RequestSizeLimit(MaxEnterpriseWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(IEnumerable<EnterpriseDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEnterprisesByFilter([FromBody] Enterprise enterprise, bool descending, int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                enterprise ??= new Enterprise();
                enterprise.Id = enterpriseId;

                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
                IEnumerable<Enterprise> enterprises = await _enterpriseRepository.GetAllAsyncFiltering(
                    descending,
                    normalizedPageNumber,
                    normalizedPageSize,
                    enterprise);
                if (enterprises == null)
                    return NoContent();

                IEnumerable<EnterpriseDTO> enterpriseDTO = _mapper.Map<IEnumerable<EnterpriseDTO>>(enterprises);
                return Ok(enterpriseDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Enterprises not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Get the logged user's own enterprise (tenant resolved from the token claim).
        /// </summary>
        /// <returns>The details of the enterprise the user belongs to.</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Enterprise.ReadSelf)]
        [HttpGet]
        [ProducesResponseType(typeof(EnterpriseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetMyEnterprise()
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                Enterprise enterprise = await _repository.GetByIdAsync(enterpriseId);
                if (enterprise == null || enterprise.IsActive != true)
                    return NoContent();

                EnterpriseDTO enterpriseDTO = _mapper.Map<EnterpriseDTO>(enterprise);

                // Balance is financial data already visible through the cash flow forecast;
                // roles without that permission get the enterprise details with balance hidden.
                var canSeeBalance = (await _authorizationService.AuthorizeAsync(User, RbacPolicies.CashFlowForecast.Read)).Succeeded;
                if (!canSeeBalance)
                    enterpriseDTO.CurrentBalance = 0;

                return Ok(enterpriseDTO);
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Get a enterprise.
        /// </summary>
        /// <param name="id">Id of the enterprise</param>
        /// <returns>The enterprise that match with the id parameter.</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Enterprise.Read)]
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(EnterpriseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEnterprise(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                if (id != enterpriseId)
                    return Forbid();

                Enterprise enterprise = await _repository.GetByIdAsync(id);
                if (enterprise == null || enterprise.IsActive != true)
                    return NoContent();

                EnterpriseDTO enterpriseDTO = _mapper.Map<EnterpriseDTO>(enterprise);
                return Ok(enterpriseDTO);
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Add a new enterprise
        /// </summary>
        /// <param name="enterprise">Enterprise to be added</param>
        /// <returns>The added enterprise</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Enterprise.Create)]
        [HttpPost]
        [RequestSizeLimit(MaxEnterpriseWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(EnterpriseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddEnterprise([FromBody] CreateEnterpriseRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest("Enterprise payload is required.");
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                // Built field by field rather than bound: the entity carries CurrentBalance and the
                // audit columns, and a request body has no business setting either.
                var enterprise = new Enterprise
                {
                    Name = request.Name.Trim(),
                    Email = request.Email.Trim(),
                    PhoneNumber = request.PhoneNumber.Trim(),
                    Adress = request.Adress.Trim(),
                    Currency = request.Currency?.Trim() ?? string.Empty,
                    ShouldSendMonthlyBilling = request.ShouldSendMonthlyBilling,
                    ShouldSendDunning = request.ShouldSendDunning,
                    IsActive = true
                };

                Enterprise createdEnterprise = await _enterpriseService.CreateAsync(enterprise);
                return Ok(_mapper.Map<EnterpriseDTO>(createdEnterprise));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Update an enterprise
        /// </summary>
        /// <param name="enterprise">Enterprise to be updated</param>
        /// <returns>The updated enterprise</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Enterprise.Update)]
        [HttpPut]
        [RequestSizeLimit(MaxEnterpriseWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(EnterpriseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateEnterprise([FromBody] UpdateEnterpriseRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest("Enterprise payload is required.");
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                if (request.Id == Guid.Empty)
                    return BadRequest("Enterprise id is required.");

                if (request.Id != enterpriseId)
                    return Forbid();

                // The stored row is the base, so CurrentBalance keeps whatever the cash flow
                // computed. Binding the entity used to let the caller write it directly, and
                // CashFlowForecastService reads it.
                Enterprise existingEnterprise = await _repository.GetByIdAsync(request.Id);
                if (existingEnterprise == null || existingEnterprise.IsActive != true)
                    return NoContent();

                existingEnterprise.Name = request.Name.Trim();
                existingEnterprise.Email = request.Email.Trim();
                existingEnterprise.PhoneNumber = request.PhoneNumber.Trim();
                existingEnterprise.Adress = request.Adress.Trim();
                existingEnterprise.Currency = request.Currency?.Trim() ?? existingEnterprise.Currency;
                existingEnterprise.ShouldSendMonthlyBilling = request.ShouldSendMonthlyBilling;
                existingEnterprise.ShouldSendDunning = request.ShouldSendDunning;

                Enterprise updatedEnterprise = _enterpriseService.Update(existingEnterprise);

                if (updatedEnterprise == null)
                    return NoContent();

                return Ok(_mapper.Map<EnterpriseDTO>(updatedEnterprise));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Closes the books through a date, or reopens them when the date is null.
        /// </summary>
        /// <remarks>
        /// Admin-only, reusing enterprise.manage: freezing what the accountant already received is
        /// the same class of act as the other settings behind that permission, and reopening one
        /// matters more than closing it — which is why Enterprise is audited from this milestone on.
        ///
        /// It is a dedicated action rather than a field on the general update so that whoever can
        /// edit a phone number cannot move the lock.
        /// </remarks>
        [Authorize(Policy = "rbac:" + RbacPermissions.Enterprise.Manage)]
        [HttpPut]
        [RequestSizeLimit(MaxEnterpriseWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(EnterpriseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> CloseThrough([FromBody] ClosePeriodRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest("Closing payload is required.");
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                Enterprise existingEnterprise = await _repository.GetByIdAsync(enterpriseId);
                if (existingEnterprise == null || existingEnterprise.IsActive != true)
                    return NoContent();

                // Date only: the lock is a day, and a stray time component would make "closed
                // through the 31st" mean 31st 00:00 for one caller and 31st 23:59 for the next.
                existingEnterprise.ClosedThroughUtc = request.ClosedThroughUtc?.Date;

                Enterprise updatedEnterprise = _enterpriseService.Update(existingEnterprise);
                if (updatedEnterprise == null)
                    return NoContent();

                return Ok(_mapper.Map<EnterpriseDTO>(updatedEnterprise));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Delete an enterprise (soft delete)
        /// </summary>
        /// <param name="id">Enterprise's Id</param>
        /// <returns>The deleted enterprise</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Enterprise.Delete)]
        [HttpDelete]
        [ProducesResponseType(typeof(EnterpriseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteEnterprise(Guid id)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                if (id != enterpriseId)
                    return Forbid();

                Enterprise enterprise = _enterpriseService.Delete(id);
                if (enterprise == null)
                    return NoContent();

                return Ok(_mapper.Map<EnterpriseDTO>(enterprise));
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
