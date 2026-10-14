using AutoMapper;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    public class OrderControllerInsertQueuedOrderTenantTests
    {
        [Fact]
        public async Task InsertQueuedOrder_WithValidTenantContext_CreatesOrderUnderQueuedTenant()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var orderService = new Mock<IOrderService<Order>>(MockBehavior.Strict);
            Order? capturedOrder = null;

            orderService
                .Setup(s => s.CreateAsync(It.IsAny<Order>()))
                .Callback<Order>(order => capturedOrder = order)
                .ReturnsAsync((Order order) => order);

            var controller = CreateController(
                orderService.Object,
                BuildUserRepository(userId, enterpriseId),
                BuildEnterpriseRepository(enterpriseId, isActive: true));

            var result = await controller.InsertQueuedOrder(BuildRequest(enterpriseId, userId));

            Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(capturedOrder);
            Assert.Equal(enterpriseId, capturedOrder!.EnterpriseId);
            Assert.Equal(userId, capturedOrder.UserId);
        }

        [Fact]
        public async Task InsertQueuedOrder_WhenUserBelongsToAnotherTenant_ReturnsBadRequestWithoutCreating()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var orderService = new Mock<IOrderService<Order>>(MockBehavior.Strict);

            var controller = CreateController(
                orderService.Object,
                BuildUserRepository(userId, Guid.NewGuid()),
                new Mock<IRepository<Enterprise>>(MockBehavior.Strict).Object);

            var result = await controller.InsertQueuedOrder(BuildRequest(enterpriseId, userId));

            Assert.IsType<BadRequestObjectResult>(result);
            orderService.Verify(s => s.CreateAsync(It.IsAny<Order>()), Times.Never);
        }

        [Fact]
        public async Task InsertQueuedOrder_WhenEnterpriseInactive_ReturnsBadRequestWithoutCreating()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var orderService = new Mock<IOrderService<Order>>(MockBehavior.Strict);

            var controller = CreateController(
                orderService.Object,
                BuildUserRepository(userId, enterpriseId),
                BuildEnterpriseRepository(enterpriseId, isActive: false));

            var result = await controller.InsertQueuedOrder(BuildRequest(enterpriseId, userId));

            Assert.IsType<BadRequestObjectResult>(result);
            orderService.Verify(s => s.CreateAsync(It.IsAny<Order>()), Times.Never);
        }

        [Fact]
        public async Task InsertQueuedOrder_WhenTenantContextMissing_ReturnsBadRequest()
        {
            var orderService = new Mock<IOrderService<Order>>(MockBehavior.Strict);

            var controller = CreateController(
                orderService.Object,
                new Mock<IRepository<User>>(MockBehavior.Strict).Object,
                new Mock<IRepository<Enterprise>>(MockBehavior.Strict).Object);

            var result = await controller.InsertQueuedOrder(BuildRequest(Guid.Empty, Guid.Empty));

            Assert.IsType<BadRequestObjectResult>(result);
            orderService.Verify(s => s.CreateAsync(It.IsAny<Order>()), Times.Never);
        }

        private static CreateQueuedOrderRequestDTO BuildRequest(Guid enterpriseId, Guid userId)
        {
            return new CreateQueuedOrderRequestDTO
            {
                EnterpriseId = enterpriseId,
                UserId = userId,
                CustomerId = Guid.NewGuid(),
                PaymentScheduledDate = DateTime.UtcNow.AddDays(30),
                Status = 0,
                Items =
                [
                    new OrderLineItemRequestDTO
                    {
                        ProductId = Guid.NewGuid(),
                        Quantity = 1,
                        Value = 10
                    }
                ]
            };
        }

        private static IRepository<User> BuildUserRepository(Guid userId, Guid enterpriseId)
        {
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            userRepository
                .Setup(x => x.GetByIdAsync(userId))
                .ReturnsAsync(new User
                {
                    Id = userId,
                    EnterpriseId = enterpriseId,
                    IsActive = true
                });
            return userRepository.Object;
        }

        private static IRepository<Enterprise> BuildEnterpriseRepository(Guid enterpriseId, bool isActive)
        {
            var enterpriseRepository = new Mock<IRepository<Enterprise>>(MockBehavior.Strict);
            enterpriseRepository
                .Setup(x => x.GetByIdAsync(enterpriseId))
                .ReturnsAsync(new Enterprise
                {
                    Id = enterpriseId,
                    IsActive = isActive
                });
            return enterpriseRepository.Object;
        }

        private static OrderController CreateController(
            IOrderService<Order> orderService,
            IRepository<User> userRepository,
            IRepository<Enterprise> enterpriseRepository)
        {
            var mapper = new Mock<IMapper>(MockBehavior.Strict).Object;

            var services = new ServiceCollection();
            services.AddSingleton(enterpriseRepository);

            return new OrderController(orderService, userRepository, mapper)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        RequestServices = services.BuildServiceProvider()
                    }
                }
            };
        }
    }
}
