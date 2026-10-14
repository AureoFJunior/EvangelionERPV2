using System.Security.Claims;
using AutoMapper;
using EvangelionERPV2.CustomerModule.Application.Interface;
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
using System.Linq;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class CustomerController : Controller
    {
        private const int DefaultPageNumber = 1;
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;
        private const int MaxCustomerWriteRequestBodySizeInBytes = 64 * 1024;

        private readonly ICustomerService<Customer> _customerService;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Customer> _repository;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;

        public CustomerController(ICustomerService<Customer> customerService,
            EvangelionERPV2.Shared.Repositories.IRepository<Customer> repository,
            IRepository<User> userRepository,
            IMapper mapper)
        {
            _customerService = customerService;
            _repository = repository;
            _userRepository = userRepository;
            _mapper = mapper;
        }

        /// <summary>
        /// Return all the customers (also works with pagination).
        /// </summary>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <returns></returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Customers.Read)]
        [HttpGet("{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<CustomerDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetCustomers(int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
                IEnumerable<Customer> customers = await _repository.GetAllAsync(
                    normalizedPageNumber,
                    normalizedPageSize,
                    x => x.EnterpriseId != null && (x.EnterpriseId != default(Guid) && x.EnterpriseId == enterpriseId));
                if (customers == null)
                    return NoContent();

                IEnumerable<CustomerDTO> customerDTO = _mapper.Map<IEnumerable<CustomerDTO>>(customers);
                return Ok(customerDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Customers not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Return all the customers that matches the filter (also works with pagination).
        /// </summary>
        /// <param name="descending">Order by type.</param>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <param name="customer">Object used to filter data.</param>
        /// <returns></returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Customers.Read)]
        [HttpPost("{descending}/{pageNumber?}/{pageSize?}")]
        [RequestSizeLimit(MaxCustomerWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(IEnumerable<CustomerDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetCustomersByFilter([FromBody] CustomerFilterRequestDTO? filter, bool descending, int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                filter ??= new CustomerFilterRequestDTO();
                var nameFilter = filter.Name?.Trim();
                var emailFilter = filter.Email?.Trim();
                var documentFilter = filter.Document?.Trim();
                var phoneFilter = filter.PhoneNumber?.Trim();
                var isActiveFilter = filter.IsActive;
                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);

                var customers = await _repository.GetAllAsync(
                    normalizedPageNumber,
                    normalizedPageSize,
                    x =>
                        x.EnterpriseId == enterpriseId &&
                        (!isActiveFilter.HasValue || x.IsActive == isActiveFilter.Value) &&
                        (string.IsNullOrWhiteSpace(nameFilter) || x.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)) &&
                        (string.IsNullOrWhiteSpace(emailFilter) || x.Email.Contains(emailFilter, StringComparison.OrdinalIgnoreCase)) &&
                        (string.IsNullOrWhiteSpace(documentFilter) || (x.Document != null && x.Document.Contains(documentFilter, StringComparison.OrdinalIgnoreCase))) &&
                        (string.IsNullOrWhiteSpace(phoneFilter) || x.PhoneNumber.Contains(phoneFilter, StringComparison.OrdinalIgnoreCase)));

                if (customers == null || !customers.Any())
                    return NoContent();

                IEnumerable<CustomerDTO> customerDTO = _mapper.Map<IEnumerable<CustomerDTO>>(customers);
                return Ok(customerDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Customers not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Get a customer.
        /// </summary>
        /// <param name="id">Id of the customer</param>
        /// <returns>The customer that match with the id parameter.</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Customers.Read)]
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(CustomerDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetCustomer(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                Customer customer = await _repository.GetByIdAsync(id);
                if (customer == null || customer.EnterpriseId != enterpriseId || customer.IsActive != true)
                    return NoContent();

                CustomerDTO customerDTO = _mapper.Map<CustomerDTO>(customer);
                return Ok(customerDTO);
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Add a new customer
        /// </summary>
        /// <param name="customer">Customer to be added</param>
        /// <returns>The added customer</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Customers.Create)]
        [HttpPost]
        [RequestSizeLimit(MaxCustomerWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(CustomerDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddCustomer([FromBody] CreateCustomerRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest();
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                if (!await HasActiveTenantMembershipAsync(TryGetUserId(), enterpriseId))
                    return Unauthorized();

                var customer = MapCreateCustomerRequest(request, enterpriseId);
                Customer createdCustomer = await _customerService.CreateAsync(customer);
                return Ok(_mapper.Map<CustomerDTO>(createdCustomer));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Update an customer
        /// </summary>
        /// <param name="customer">Customer to be updated</param>
        /// <returns>The updated customer</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Customers.Update)]
        [HttpPut]
        [RequestSizeLimit(MaxCustomerWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(CustomerDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateCustomer([FromBody] UpdateCustomerRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest();
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var existingCustomer = await _repository.GetByIdAsync(request.Id);
                if (existingCustomer == null || existingCustomer.EnterpriseId != enterpriseId || existingCustomer.IsActive != true)
                    return NoContent();

                existingCustomer.Name = request.Name.Trim();
                existingCustomer.Email = request.Email.Trim();
                existingCustomer.PhoneNumber = request.PhoneNumber.Trim();
                existingCustomer.Adress = request.Adress.Trim();
                existingCustomer.Document = request.Document?.Trim();
                existingCustomer.IsActive = request.IsActive;
                existingCustomer.UpdatedAt = DateTime.UtcNow;

                Customer updatedCustomer = _customerService.Update(existingCustomer);

                if (updatedCustomer == null)
                    return NoContent();

                return Ok(_mapper.Map<CustomerDTO>(updatedCustomer));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Delete an customer (soft delete)
        /// </summary>
        /// <param name="id">Customer's Id</param>
        /// <returns>The deleted customer</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Customers.Delete)]
        [HttpDelete("{id}")]
        [ProducesResponseType(typeof(CustomerDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteCustomer(Guid id)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var customer = await _repository.GetByIdAsync(id);
                if (customer == null || customer.EnterpriseId != enterpriseId || customer.IsActive != true)
                    return NoContent();

                Customer deletedCustomer = _customerService.Delete(id);
                if (deletedCustomer == null)
                    return NoContent();

                return Ok(_mapper.Map<CustomerDTO>(deletedCustomer));
            }
            catch (Exception)
            {
                throw;
            }
        }

        private static Customer MapCreateCustomerRequest(CreateCustomerRequestDTO request, Guid enterpriseId)
        {
            return new Customer
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
