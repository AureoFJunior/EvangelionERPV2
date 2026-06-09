using Microsoft.AspNetCore.Mvc;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using AutoMapper;
using Serilog;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using System.Linq;
using System.Security.Claims;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class OrderController : Controller
    {
        private const int MaxOrderRequestBodySizeInBytes = 1 * 1024 * 1024;
        private const int MaxRefundReasonLength = 500;
        private const int DefaultPageNumber = 1;
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;

        private readonly IOrderService<Order> _orderService;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;

        public OrderController(IOrderService<Order> orderService,
            IRepository<User> userRepository,
            IMapper mapper)
        {
            _orderService = orderService;
            _userRepository = userRepository;
            _mapper = mapper;
        }

        /// <summary>
        /// Return all the orders (also works with pagination).
        /// </summary>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <returns></returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Orders.Read)]
        [HttpGet("{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<OrderDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetOrders(int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                if (!await HasActiveTenantMembershipAsync(TryGetUserId(), enterpriseId))
                    return Unauthorized();

                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
                IEnumerable<Order> orders = await _orderService.GetByEnterpriseIdAsync(
                    enterpriseId,
                    normalizedPageNumber,
                    normalizedPageSize);
                if (!orders.Any())
                    return NoContent();

                IEnumerable<OrderDTO> orderDTO = _mapper.Map<IEnumerable<OrderDTO>>(orders);
                return Ok(orderDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Orders not found. ErrorType={ErrorType}", exnf.GetType().Name);
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
        [Authorize(Policy = "rbac:" + RbacPermissions.Orders.Read)]
        [HttpPost("{descending}/{pageNumber?}/{pageSize?}")]
        [RequestSizeLimit(MaxOrderRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(IEnumerable<OrderDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetOrdersByFilter([FromBody] OrderFilterRequestDTO? filter, bool descending, int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                if (!await HasActiveTenantMembershipAsync(TryGetUserId(), enterpriseId))
                    return Unauthorized();

                filter ??= new OrderFilterRequestDTO();

                var statusFilter = filter.Status;
                var customerIdFilter = filter.CustomerId;
                var startDateFilter = filter.StartDate?.Date;
                var endDateFilter = filter.EndDate?.Date;
                var isActiveFilter = filter.IsActive;

                var orders = (await _orderService.GetByEnterpriseIdAsync(enterpriseId))
                    .Where(x =>
                        (!isActiveFilter.HasValue || x.IsActive == isActiveFilter.Value) &&
                        (!statusFilter.HasValue || x.Status == statusFilter.Value) &&
                        (!customerIdFilter.HasValue || x.CustomerId == customerIdFilter.Value) &&
                        (!startDateFilter.HasValue || x.CreatedAt.Date >= startDateFilter.Value) &&
                        (!endDateFilter.HasValue || x.CreatedAt.Date <= endDateFilter.Value))
                    .OrderByDescending(x => x.CreatedAt)
                    .ToList();

                if (!descending)
                    orders = orders.OrderBy(x => x.CreatedAt).ToList();

                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
                orders = orders
                    .Skip((normalizedPageNumber - 1) * normalizedPageSize)
                    .Take(normalizedPageSize)
                    .ToList();

                if (!orders.Any())
                    return NoContent();

                IEnumerable<OrderDTO> orderDTO = _mapper.Map<IEnumerable<OrderDTO>>(orders);
                return Ok(orderDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Orders not found. ErrorType={ErrorType}", exnf.GetType().Name);
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
        [Authorize(Policy = "rbac:" + RbacPermissions.Orders.Read)]
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
                if (!await HasActiveTenantMembershipAsync(TryGetUserId(), enterpriseId))
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
        [Authorize(Policy = "rbac:" + RbacPermissions.Orders.Create)]
        [HttpPost]
        [RequestSizeLimit(MaxOrderRequestBodySizeInBytes)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddOrder([FromBody] CreateOrderRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid || request == null) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var userId = TryGetUserId();
                if (!await HasActiveTenantMembershipAsync(userId, enterpriseId))
                    return Unauthorized();

                var order = MapCreateRequestToOrder(request, enterpriseId, userId);
                await _orderService.InsertOrderInQueue(order);
                return Ok("Order enqueued successfully");
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid order payload. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
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
        [Authorize(Policy = "rbac:" + RbacPermissions.Orders.Create)]
        [HttpPost]
        [RequestSizeLimit(MaxOrderRequestBodySizeInBytes)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> InsertOrder([FromBody] CreateOrderRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid || request == null) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var userId = TryGetUserId();
                if (!await HasActiveTenantMembershipAsync(userId, enterpriseId))
                    return Unauthorized();

                var order = MapCreateRequestToOrder(request, enterpriseId, userId);
                await _orderService.CreateAsync(order);
                return Ok("Order created successfully");
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid order payload. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
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
        [Authorize(Policy = "rbac:" + RbacPermissions.Orders.Update)]
        [HttpPut]
        [RequestSizeLimit(MaxOrderRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(OrderDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateOrder([FromBody] UpdateOrderRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid || request == null) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var order = MapUpdateRequestToOrder(request);

                Order updatedOrder = _orderService.Update(order, enterpriseId);

                if (updatedOrder == null)
                    return NoContent();

                return Ok(_mapper.Map<OrderDTO>(updatedOrder));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Orders not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Orders.Refund)]
        [HttpPost("{id}")]
        [RequestSizeLimit(MaxOrderRequestBodySizeInBytes)]
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
                if (reason.Length > MaxRefundReasonLength)
                    return BadRequest($"Reason must be {MaxRefundReasonLength} characters or fewer.");

                var refundedOrder = await _orderService.RefundAsync(id, enterpriseId, reason);
                return Ok(_mapper.Map<OrderDTO>(refundedOrder));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid refund action for order. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (NotFoundDatabaseException ex)
            {
                Log.Logger.Error("Order not found for refund. ErrorType={ErrorType}", ex.GetType().Name);
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
        [Authorize(Policy = "rbac:" + RbacPermissions.Orders.Delete)]
        [HttpDelete]
        [ProducesResponseType(typeof(OrderDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteOrder(Guid id)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                Order order = _orderService.Delete(id, enterpriseId);
                if (order == null)
                    return NoContent();

                return Ok(_mapper.Map<OrderDTO>(order));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Orders not found. ErrorType={ErrorType}", exnf.GetType().Name);
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


        private static Order MapCreateRequestToOrder(CreateOrderRequestDTO request, Guid enterpriseId, Guid? userId)
        {
            var orderedProducts = (request.Items ?? Enumerable.Empty<OrderLineItemRequestDTO>())
                .Select(item => new OrderedProduct
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    Value = item.Value,
                    IsActive = true
                })
                .ToList();

            return new Order
            {
                EnterpriseId = enterpriseId,
                CustomerId = request.CustomerId,
                UserId = userId,
                PaymentScheduledDate = request.PaymentScheduledDate,
                Status = request.Status,
                OrderedProduct = orderedProducts
            };
        }

        private static Order MapUpdateRequestToOrder(UpdateOrderRequestDTO request)
        {
            var order = new Order
            {
                Id = request.Id,
                CustomerId = request.CustomerId,
                PaymentScheduledDate = request.PaymentScheduledDate ?? default,
                Status = request.Status,
                Payday = request.Payday
            };

            return order;
        }

        private static string GetSafeInsertErrorMessage(InsertDatabaseException ex)
        {
            if (ex.InnerException != null)
                return "An internal error occurred. Please try again later.";

            var message = ex.Message?.Trim() ?? string.Empty;

            if (string.Equals(message, "Refund reason is required.", StringComparison.OrdinalIgnoreCase))
                return "Refund reason is required.";

            if (string.Equals(message, "Orders with status Finished or Refund cannot be edited.", StringComparison.OrdinalIgnoreCase))
                return "Orders with status Finished or Refund cannot be edited.";

            if (string.Equals(message, "Order has no items to refund.", StringComparison.OrdinalIgnoreCase))
                return "Order has no items to refund.";

            if (string.Equals(message, "Some products for this order were not found in the inventory.", StringComparison.OrdinalIgnoreCase))
                return "Some products for this order were not found in the inventory.";

            if (message.StartsWith("Insufficient stock for product [", StringComparison.OrdinalIgnoreCase) &&
                message.EndsWith("].", StringComparison.OrdinalIgnoreCase))
            {
                return "Insufficient stock for one or more products in this order.";
            }

            if (message.StartsWith("Product [", StringComparison.OrdinalIgnoreCase) &&
                message.EndsWith("] was not found.", StringComparison.OrdinalIgnoreCase))
            {
                return "A product for this order was not found.";
            }

            return "An internal error occurred. Please try again later.";
        }
    }
}
