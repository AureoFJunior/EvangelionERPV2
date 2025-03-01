using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AutoMapper;
using Serilog;
using EvangelionERPV2.OrderModule.Domain.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class OrderController : Controller
    {
        private readonly IOrderRepository<Order> _orderRepository;
        private readonly IOrderService<Order> _orderService;
        private readonly IRepository<Order> _repository;
        private readonly IMapper _mapper;

        public OrderController(IOrderService<Order> orderService,
            IRepository<Order> repository,
            IOrderRepository<Order> orderRepository,
            IMapper mapper)
        {
            _orderService = orderService;
            _repository = repository;
            _orderRepository = orderRepository;
            _mapper = mapper;
        }

        /// <summary>
        /// Return all the orders (also works with pagination).
        /// </summary>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <returns></returns>
        [HttpGet("{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<OrderDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetOrders(int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                IEnumerable<Order> orders = await _repository.GetAllAsync(pageNumber, pageSize);
                if (orders == null)
                    return NoContent();

                IEnumerable<OrderDTO> orderDTO = _mapper.Map<IEnumerable<OrderDTO>>(orders);
                return Ok(orderDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Orders not found", exnf);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when getting Orders", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Return all the orders that matches the filter (also works with pagination).
        /// </summary>
        /// <param name="descending">Order by type.</param>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <param name="order">Object used to filter data.</param>
        /// <returns></returns>
        [HttpPost("{descending}/{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<OrderDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetOrdersByFilter([FromBody] Order order, bool descending, int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                (IEnumerable<Order> orders, int totalItems)= await _orderRepository.GetAllAsyncFiltering(descending, pageNumber, pageSize, order);
                if (orders == null)
                    return NoContent();

                IEnumerable<OrderDTO> orderDTO = _mapper.Map<IEnumerable<OrderDTO>>(orders);
                return Ok(orderDTO.ToPaginatedResult(pageNumber ?? 1, pageSize ?? 1, totalItems));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Orders not found", exnf);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when getting Orders", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Get a order.
        /// </summary>
        /// <param name="id">Id of the order</param>
        /// <returns>The order that match with the id parameter.</returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(OrderDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetOrder(Guid id)
        {
            try
            {
                Order order = await _repository.GetByIdAsync(id);
                if (order == null)
                    return NoContent();

                OrderDTO orderDTO = _mapper.Map<OrderDTO>(order);
                return Ok(orderDTO);
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when getting Order ID {id}", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Add a new order in the queue
        /// </summary>
        /// <param name="order">Order to be added</param>
        /// <returns>The added order</returns>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddOrder([FromBody] Order order)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                await _orderService.InsertOrderInQueue(order);
                return Ok("Order enqueued successfully");
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when enqueing Order for {order.Enterprise?.Name ?? ""}{(order.Customer == null ? "" : $"/{order.Customer.Name}")}", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Add a new order
        /// </summary>
        /// <param name="order">Order to be added</param>
        /// <returns>The added order</returns>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> InsertOrder([FromBody] Order order)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                await _orderService.CreateAsync(order);
                return Ok("Order created successfully");
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when creating Order for {order.Enterprise?.Name ?? ""}{(order.Customer == null ? "" : $"/{order.Customer.Name}")}", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Update an order
        /// </summary>
        /// <param name="order">Order to be updated</param>
        /// <returns>The updated order</returns>
        [HttpPut]
        [ProducesResponseType(typeof(OrderDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateOrder([FromBody] Order order)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                Order updatedOrder = _orderService.Update(order);

                if (updatedOrder == null)
                    return NoContent();

                return Ok(order);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Orders not found", exnf);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when updating Order: {order.Id}", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Delete an order (soft delete)
        /// </summary>
        /// <param name="id">Order's Id</param>
        /// <returns>The deleted order</returns>
        [HttpDelete]
        [ProducesResponseType(typeof(OrderDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteOrder(Guid id)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                Order order = _orderService.Delete(id);
                if (order == null)
                    return NoContent();

                return Ok(order);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Orders not found", exnf);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when deleting Order ID {id}", ex);
                return Problem(ex.Message);
            }
        }
    }
}
