using System.Security.Claims;
using AutoMapper;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The credit-limit override is a permission check the controller performs and hands down, not
    /// something the service can read for itself.
    /// </summary>
    /// <remarks>
    /// It has to be asked of the authorization service: PermissionAuthorizationHandler derives
    /// permissions from the user's access level, so there is no claim in the token to inspect.
    /// </remarks>
    public class OrderControllerCreditOverrideTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task InsertOrder_PassesTheOverrideItWasAuthorizedFor(bool authorized)
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            bool? overrideSeen = null;

            var orderService = new Mock<IOrderService<Order>>();
            orderService
                .Setup(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .Callback<Order, bool, bool, string?>((_, allowOverride, _, _) => overrideSeen = allowOverride)
                .ReturnsAsync((Order order, bool _, bool _, string? _messageId) => order);

            var controller = CreateController(orderService.Object, enterpriseId, userId, authorized);

            var result = await controller.InsertOrder(new CreateOrderRequestDTO
            {
                CustomerId = Guid.NewGuid(),
                PaymentScheduledDate = DateTime.UtcNow.Date.AddDays(30),
                Items = [new OrderLineItemRequestDTO { ProductId = Guid.NewGuid(), Quantity = 1, Value = 10 }]
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(authorized, overrideSeen);
        }

        [Fact]
        public async Task InsertQueuedOrder_NeverOverrides()
        {
            // The machine user persists what the queue carried. Nobody is standing there to make
            // the call, so the limit is enforced even if the enqueuing user could have overridden.
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            bool? overrideSeen = null;

            var orderService = new Mock<IOrderService<Order>>();
            orderService
                .Setup(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .Callback<Order, bool, bool, string?>((_, allowOverride, _, _) => overrideSeen = allowOverride)
                .ReturnsAsync((Order order, bool _, bool _, string? _messageId) => order);

            var controller = CreateController(orderService.Object, enterpriseId, userId, authorized: true);

            var result = await controller.InsertQueuedOrder(new CreateQueuedOrderRequestDTO
            {
                EnterpriseId = enterpriseId,
                UserId = userId,
                CustomerId = Guid.NewGuid(),
                PaymentScheduledDate = DateTime.UtcNow.Date.AddDays(30),
                Items = [new OrderLineItemRequestDTO { ProductId = Guid.NewGuid(), Quantity = 1, Value = 10 }]
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.False(overrideSeen);
        }

        private static OrderController CreateController(
            IOrderService<Order> orderService,
            Guid enterpriseId,
            Guid userId,
            bool authorized)
        {
            var userRepository = new Mock<IRepository<User>>();
            userRepository
                .Setup(x => x.GetByIdAsync(userId))
                .ReturnsAsync(new User { Id = userId, EnterpriseId = enterpriseId, IsActive = true });

            var enterpriseRepository = new Mock<IRepository<Enterprise>>();
            enterpriseRepository
                .Setup(x => x.GetByIdAsync(enterpriseId))
                .ReturnsAsync(new Enterprise { Id = enterpriseId, IsActive = true });

            var authorization = new Mock<IAuthorizationService>();
            authorization
                .Setup(x => x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object>(),
                    RbacPolicies.Customers.CreditOverride))
                .ReturnsAsync(authorized ? AuthorizationResult.Success() : AuthorizationResult.Failed());

            var controller = new OrderController(
                orderService,
                userRepository.Object,
                authorization.Object,
                Mock.Of<IMapper>());

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                new Claim(ClaimTypes.Sid, userId.ToString())
            ], "TestAuth"));

            // InsertQueuedOrder re-checks the payload tenant by resolving the enterprise
            // repository off RequestServices, so the context needs a real provider behind it.
            var services = new ServiceCollection();
            services.AddSingleton(enterpriseRepository.Object);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = principal,
                    RequestServices = services.BuildServiceProvider()
                }
            };

            return controller;
        }
    }
}
