using Asp.Versioning;
using System.Security.Claims;
using AutoMapper;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.SupplierModule.Application.Interface;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class SupplierController : Controller
    {
        private const int MaxPageSize = 200;
        private const int MaxSupplierWriteRequestBodySizeInBytes = 64 * 1024;

        private readonly ISupplierService<Supplier> _supplierService;
        private readonly IRepository<Supplier> _repository;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;

        public SupplierController(ISupplierService<Supplier> supplierService,
            IRepository<Supplier> repository,
            IRepository<User> userRepository,
            IMapper mapper)
        {
            _supplierService = supplierService;
            _repository = repository;
            _userRepository = userRepository;
            _mapper = mapper;
        }

        /// <summary>
        /// Return all the suppliers (also works with pagination).
        /// </summary>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <returns></returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Suppliers.Read)]
        [HttpGet("{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<SupplierDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetSuppliers(int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
                IEnumerable<Supplier> suppliers = await _repository.GetAllAsync(
                    normalizedPageNumber,
                    normalizedPageSize,
                    x => x.EnterpriseId != null && x.EnterpriseId == enterpriseId);

                if (suppliers == null)
                    return NoContent();

                IEnumerable<SupplierDTO> supplierDTO = _mapper.Map<IEnumerable<SupplierDTO>>(suppliers);
                return Ok(supplierDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Suppliers not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Get a supplier.
        /// </summary>
        /// <param name="id">Id of the supplier</param>
        /// <returns>The supplier that match with the id parameter.</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Suppliers.Read)]
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(SupplierDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetSupplier(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                Supplier supplier = await _repository.GetByIdAsync(id);
                if (supplier == null || supplier.EnterpriseId != enterpriseId || supplier.IsActive != true)
                    return NoContent();

                return Ok(_mapper.Map<SupplierDTO>(supplier));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Supplier not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Add a new supplier
        /// </summary>
        /// <param name="request">Supplier to be added</param>
        /// <returns>The added supplier</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Suppliers.Create)]
        [HttpPost]
        [RequestSizeLimit(MaxSupplierWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(SupplierDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddSupplier([FromBody] CreateSupplierRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest();
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                if (!await HasActiveTenantMembershipAsync(TryGetUserId(), enterpriseId))
                    return Unauthorized();

                var supplier = MapCreateSupplierRequest(request, enterpriseId);
                Supplier createdSupplier = await _supplierService.CreateAsync(supplier);
                return Ok(_mapper.Map<SupplierDTO>(createdSupplier));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Update a supplier
        /// </summary>
        /// <param name="request">Supplier to be updated</param>
        /// <returns>The updated supplier</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Suppliers.Update)]
        [HttpPut]
        [RequestSizeLimit(MaxSupplierWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(SupplierDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateSupplier([FromBody] UpdateSupplierRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest();
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var existingSupplier = await _repository.GetByIdAsync(request.Id);
                if (existingSupplier == null || existingSupplier.EnterpriseId != enterpriseId || existingSupplier.IsActive != true)
                    return NoContent();

                existingSupplier.Name = request.Name.Trim();
                existingSupplier.Email = request.Email.Trim();
                existingSupplier.PhoneNumber = request.PhoneNumber.Trim();
                existingSupplier.Adress = request.Adress.Trim();
                existingSupplier.Document = request.Document?.Trim();
                existingSupplier.IsActive = request.IsActive;
                existingSupplier.UpdatedAt = DateTime.UtcNow;

                Supplier updatedSupplier = _supplierService.Update(existingSupplier);

                if (updatedSupplier == null)
                    return NoContent();

                return Ok(_mapper.Map<SupplierDTO>(updatedSupplier));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Supplier not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Delete a supplier (soft delete)
        /// </summary>
        /// <param name="id">Supplier's Id</param>
        /// <returns>The deleted supplier</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Suppliers.Delete)]
        [HttpDelete("{id}")]
        [ProducesResponseType(typeof(SupplierDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteSupplier(Guid id)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var supplier = await _repository.GetByIdAsync(id);
                if (supplier == null || supplier.EnterpriseId != enterpriseId || supplier.IsActive != true)
                    return NoContent();

                Supplier deletedSupplier = _supplierService.Delete(id);
                if (deletedSupplier == null)
                    return NoContent();

                return Ok(_mapper.Map<SupplierDTO>(deletedSupplier));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Supplier not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        private static Supplier MapCreateSupplierRequest(CreateSupplierRequestDTO request, Guid enterpriseId)
        {
            return new Supplier
            {
                Name = request.Name.Trim(),
                Email = request.Email.Trim(),
                PhoneNumber = request.PhoneNumber.Trim(),
                Adress = request.Adress.Trim(),
                Document = request.Document?.Trim(),
                EnterpriseId = enterpriseId,
                IsActive = request.IsActive ?? true
            };
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
