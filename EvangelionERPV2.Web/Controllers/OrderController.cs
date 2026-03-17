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
using System.Security.Claims;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class OrderController : Controller
    {
        private readonly IOrderRepository<Order> _orderRepository;
        private readonly IOrderService<Order> _orderService;
        private readonly IMapper _mapper;

        public OrderController(IOrderService<Order> orderService,
            IOrderRepository<Order> orderRepository,
            IMapper mapper)
        {
            _orderService = orderService;
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
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                IEnumerable<Order> orders = await _orderService.GetByEnterpriseIdAsync(enterpriseId, pageNumber, pageSize);
                if (!orders.Any())
                    return NoContent();

                IEnumerable<OrderDTO> orderDTO = _mapper.Map<IEnumerable<OrderDTO>>(orders);
                return Ok(orderDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error(exnf, "Orders not found");
                return NoContent();
            }
            catch (Exception)
            {
                throw;
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
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                order ??= new Order();
                order.EnterpriseId = enterpriseId;
                order.IsActive = true;

                (IEnumerable<Order> orders, int totalItems) = await _orderRepository.GetAllAsyncFiltering(descending, pageNumber, pageSize, order);
                if (!orders.Any())
                    return NoContent();

                IEnumerable<OrderDTO> orderDTO = _mapper.Map<IEnumerable<OrderDTO>>(orders);
                return Ok(orderDTO.ToPaginatedResult(pageNumber ?? 1, pageSize ?? 1, totalItems));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error(exnf, "Orders not found");
                return NoContent();
            }
            catch (Exception)
            {
                throw;
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
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                Order? order = await _orderService.GetByIdAsync(id, enterpriseId);
                if (order == null)
                    return NoContent();

                OrderDTO orderDTO = _mapper.Map<OrderDTO>(order);
                return Ok(orderDTO);
            }
            catch (Exception)
            {
                throw;
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
                if (!ModelState.IsValid || order == null) return BadRequest(ModelState);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                order.EnterpriseId = enterpriseId;
                await _orderService.InsertOrderInQueue(order);
                return Ok("Order enqueued successfully");
            }
            catch (Exception)
            {
                throw;
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
                if (!ModelState.IsValid || order == null) return BadRequest(ModelState);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                order.EnterpriseId = enterpriseId;
                await _orderService.CreateAsync(order);
                return Ok("Order created successfully");
            }
            catch (Exception)
            {
                throw;
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
                if (!ModelState.IsValid || order == null) return BadRequest(ModelState);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                Order updatedOrder = _orderService.Update(order, enterpriseId);

                if (updatedOrder == null)
                    return NoContent();

                return Ok(_mapper.Map<OrderDTO>(updatedOrder));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error(exnf, "Orders not found");
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        [HttpPost("{id}")]
        [ProducesResponseType(typeof(OrderDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> RefundOrder(Guid id, [FromBody] RefundRequestDTO request)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var reason = request?.Reason ?? string.Empty;
                var refundedOrder = await _orderService.RefundAsync(id, enterpriseId, reason);
                return Ok(_mapper.Map<OrderDTO>(refundedOrder));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error(ex, "Invalid refund action for order");
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (NotFoundDatabaseException ex)
            {
                Log.Logger.Error(ex, "Order not found for refund");
                return NoContent();
            }
            catch (Exception)
            {
                throw;
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
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                Order order = _orderService.Delete(id, enterpriseId);
                if (order == null)
                    return NoContent();

                return Ok(_mapper.Map<OrderDTO>(order));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error(exnf, "Orders not found");
                return NoContent();
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

        private static string GetSafeInsertErrorMessage(InsertDatabaseException ex)
        {
            return ex.InnerException == null
                ? ex.Message
                : "An internal error occurred. Please try again later.";
        }
    }
}
