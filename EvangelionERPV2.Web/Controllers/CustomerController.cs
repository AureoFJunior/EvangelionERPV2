using System.Security.Claims;
using AutoMapper;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
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
        [HttpGet("{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<CustomerDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetCustomers(int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                IEnumerable<Customer> customers = await _repository.GetAllAsync(pageNumber, pageSize, x => x.EnterpriseId != null && (x.EnterpriseId != default(Guid) && x.EnterpriseId == enterpriseId));
                if (customers == null)
                    return NoContent();

                IEnumerable<CustomerDTO> customerDTO = _mapper.Map<IEnumerable<CustomerDTO>>(customers);
                return Ok(customerDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error(exnf, "Customers not found");
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
        [HttpPost("{descending}/{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<CustomerDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetCustomersByFilter([FromBody] CustomerFilterRequestDTO? filter, bool descending, int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                filter ??= new CustomerFilterRequestDTO();
                var nameFilter = filter.Name?.Trim();
                var emailFilter = filter.Email?.Trim();
                var documentFilter = filter.Document?.Trim();
                var phoneFilter = filter.PhoneNumber?.Trim();
                var isActiveFilter = filter.IsActive;

                var customers = await _repository.GetAllAsync(
                    pageNumber,
                    pageSize,
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
                Log.Logger.Error(exnf, "Customers not found");
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
        [HttpPost]
        [ProducesResponseType(typeof(CustomerDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddCustomer([FromBody] CreateCustomerRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);
                if (request == null) return BadRequest();
                if (!TryGetEnterpriseId(out var enterpriseId))
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
        [HttpPut]
        [ProducesResponseType(typeof(CustomerDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateCustomer([FromBody] UpdateCustomerRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);
                if (request == null) return BadRequest();
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var accessLevel = await ResolveAccessLevelAsync(TryGetUserId(), enterpriseId);
                if (!IsManagementAccess(accessLevel))
                    return Forbid();

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
        [HttpDelete("{id}")]
        [ProducesResponseType(typeof(CustomerDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteCustomer(Guid id)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var accessLevel = await ResolveAccessLevelAsync(TryGetUserId(), enterpriseId);
                if (!IsManagementAccess(accessLevel))
                    return Forbid();

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

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }
    }
}
