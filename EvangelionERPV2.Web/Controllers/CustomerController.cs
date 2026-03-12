using System.Security.Claims;
using AutoMapper;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.CustomerModule.Domain.Interface;
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
    public class CustomerController : Controller
    {
        private readonly ICustomerService<Customer> _customerService;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Customer> _repository;
        private readonly ICustomerRepository<Customer> _customerRepository;
        private readonly IMapper _mapper;

        public CustomerController(ICustomerService<Customer> customerService,
            EvangelionERPV2.Shared.Repositories.IRepository<Customer> repository,
            ICustomerRepository<Customer> customerRepository,
            IMapper mapper)
        {
            _customerService = customerService;
            _repository = repository;
            _customerRepository = customerRepository;
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
        public async Task<IActionResult> GetCustomersByFilter([FromBody] Customer customer, bool descending, int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                customer.EnterpriseId = enterpriseId;

                IEnumerable<Customer> customers = await _customerRepository.GetAllAsyncFiltering(descending, pageNumber, pageSize, customer);
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
                Customer customer = await _repository.GetByIdAsync(id);
                if (customer == null)
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
        public async Task<IActionResult> AddCustomer([FromBody] Customer customer)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                Customer createdCustomer = await _customerService.CreateAsync(customer);
                return Ok(createdCustomer);
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
        public async Task<IActionResult> UpdateCustomer([FromBody] Customer customer)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                Customer updatedCustomer = _customerService.Update(customer);

                if (updatedCustomer == null)
                    return NoContent();

                return Ok(customer);
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

                Customer customer = _customerService.Delete(id);
                if (customer == null)
                    return NoContent();

                return Ok(customer);
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
