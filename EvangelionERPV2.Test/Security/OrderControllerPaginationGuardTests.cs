using AutoMapper;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    public class OrderControllerPaginationGuardTests
    {
        [Fact]
        public async Task GetOrders_WhenPaginationMissing_UsesSafeDefaults()
        {
            var enterpriseId = Guid.NewGuid();
            var orderService = new Mock<IOrderService<Order>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            orderService
                .Setup(s => s.GetByEnterpriseIdAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<int?>()))
                .Callback<Guid, int?, int?>((_, pageNumber, pageSize) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateOrder(enterpriseId)]);

            var controller = CreateController(orderService.Object, userRepository.Object, mapper.Object, enterpriseId);

            var result = await controller.GetOrders();

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, capturedPageNumber);
            Assert.Equal(50, capturedPageSize);
        }

        [Fact]
        public async Task GetOrders_WhenPageSizeTooLarge_ClampsToMaximum()
        {
            var enterpriseId = Guid.NewGuid();
            var orderService = new Mock<IOrderService<Order>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var mapper = CreateMapper();
            int? capturedPageNumber = null;
            int? capturedPageSize = null;

            orderService
                .Setup(s => s.GetByEnterpriseIdAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<int?>()))
                .Callback<Guid, int?, int?>((_, pageNumber, pageSize) =>
                {
                    capturedPageNumber = pageNumber;
                    capturedPageSize = pageSize;
                })
                .ReturnsAsync([CreateOrder(enterpriseId)]);

            var controller = CreateController(orderService.Object, userRepository.Object, mapper.Object, enterpriseId);

            var result = await controller.GetOrders(pageNumber: 3, pageSize: 5000);

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(3, capturedPageNumber);
            Assert.Equal(200, capturedPageSize);
        }

        [Fact]
        public async Task GetOrdersByFilter_WhenPaginationMissing_UsesSafeDefaults()
        {
            var enterpriseId = Guid.NewGuid();
            var orderService = new Mock<IOrderService<Order>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var mapper = CreateMapper();

            orderService
                .Setup(s => s.GetByEnterpriseIdAsync(enterpriseId, null, null))
                .ReturnsAsync(CreateOrders(enterpriseId, 120));

            var controller = CreateController(orderService.Object, userRepository.Object, mapper.Object, enterpriseId);

            var result = await controller.GetOrdersByFilter(new OrderFilterRequestDTO(), descending: true);

            var ok = Assert.IsType<OkObjectResult>(result);
            var orders = Assert.IsAssignableFrom<IEnumerable<OrderDTO>>(ok.Value);
            Assert.Equal(50, orders.Count());
        }

        [Fact]
        public async Task GetOrdersByFilter_WhenPageSizeTooLarge_ClampsToMaximum()
        {
            var enterpriseId = Guid.NewGuid();
            var orderService = new Mock<IOrderService<Order>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var mapper = CreateMapper();

            orderService
                .Setup(s => s.GetByEnterpriseIdAsync(enterpriseId, null, null))
                .ReturnsAsync(CreateOrders(enterpriseId, 400));

            var controller = CreateController(orderService.Object, userRepository.Object, mapper.Object, enterpriseId);

            var result = await controller.GetOrdersByFilter(new OrderFilterRequestDTO(), descending: true, pageNumber: 1, pageSize: 5000);

            var ok = Assert.IsType<OkObjectResult>(result);
            var orders = Assert.IsAssignableFrom<IEnumerable<OrderDTO>>(ok.Value);
            Assert.Equal(200, orders.Count());
        }

        private static Mock<IMapper> CreateMapper()
        {
            var mapper = new Mock<IMapper>(MockBehavior.Strict);
            mapper.Setup(m => m.Map<IEnumerable<OrderDTO>>(It.IsAny<IEnumerable<Order>>()))
                .Returns<IEnumerable<Order>>(orders =>
                    orders.Select(order => new OrderDTO
                    {
                        Id = order.Id,
                        Status = order.Status
                    }));

            return mapper;
        }

        private static OrderController CreateController(
            IOrderService<Order> orderService,
            IRepository<User> userRepository,
            IMapper mapper,
            Guid enterpriseId)
        {
            var controller = new OrderController(orderService, userRepository, mapper);
            var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())
            ], "TestAuth"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = claims
                }
            };

            return controller;
        }

        private static Order CreateOrder(Guid enterpriseId)
        {
            return new Order
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Status = 0,
                IsActive = true,
                OrderedProduct = []
            };
        }

        private static IEnumerable<Order> CreateOrders(Guid enterpriseId, int count)
        {
            var now = DateTime.UtcNow;
            return Enumerable.Range(1, count)
                .Select(index => new Order
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId,
                    Status = 0,
                    IsActive = true,
                    CreatedAt = now.AddSeconds(-index),
                    OrderedProduct = []
                })
                .ToList();
        }
    }
}
