using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AutoMapper;
using Serilog;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.CustomerModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Exceptions;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class CustomerController : Controller
    {
        private readonly ICustomerService<Customer> _customerService;
        private readonly IRepository<Customer> _repository;
        private readonly ICustomerRepository<Customer> _customerRepository;
        private readonly IMapper _mapper;

        public CustomerController(ICustomerService<Customer> customerService,
            IRepository<Customer> repository,
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

                IEnumerable<Customer> customers = await _repository.GetAllAsync(pageNumber, pageSize);
                if (customers == null)
                    return NoContent();

                IEnumerable<CustomerDTO> customerDTO = _mapper.Map<IEnumerable<CustomerDTO>>(customers);
                return Ok(customerDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Customers not found", exnf);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when getting Customers", ex);
                return Problem(ex.Message);
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
                IEnumerable<Customer> customers = await _customerRepository.GetAllAsyncFiltering(descending, pageNumber, pageSize, customer);
                if (customers == null)
                    return NoContent();

                IEnumerable<CustomerDTO> customerDTO = _mapper.Map<IEnumerable<CustomerDTO>>(customers);
                return Ok(customerDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Customers not found", exnf);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when getting Customers", ex);
                return Problem(ex.Message);
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
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when getting Customer ID {id}", ex);
                return Problem(ex.Message);
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
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when creating Customer: {customer.Name}", ex);
                return Problem(ex.Message);
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
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when updating Customer: {customer.Name}", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Delete an customer (soft delete)
        /// </summary>
        /// <param name="id">Customer's Id</param>
        /// <returns>The deleted customer</returns>
        [HttpDelete]
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
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when deleting Customer ID {id}", ex);
                return Problem(ex.Message);
            }
        }
    }
}
