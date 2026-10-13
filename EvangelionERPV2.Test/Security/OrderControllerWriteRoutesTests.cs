using AutoMapper;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The order write paths. Two of them are unusual and that is what is asserted: `InsertQueuedOrder`
    /// runs under a machine token and therefore takes its tenant from the *payload* rather than the
    /// caller, and every response strips cost unless the caller may read it.
    /// </summary>
    public class OrderControllerWriteRoutesTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Mock<IOrderService<Order>> _service = new();
        private readonly Mock<IRepository<User>> _userRepository = new();
        private readonly Mock<IRepository<Enterprise>> _enterpriseRepository = new();

        private OrderController Controller(bool canReadCost = false, bool withUser = true)
        {
            var controller = Build(canReadCost).WithTenant(_enterpriseId, withUser ? _userId : null);
            AttachEnterpriseRepository(controller);
            return controller;
        }

        private OrderController Anonymous()
        {
            var controller = Build(false).WithoutTenant();
            AttachEnterpriseRepository(controller);
            return controller;
        }

        private OrderController Build(bool authorizationSucceeds)
        {
            var authorization = new Mock<IAuthorizationService>();
            authorization
                .Setup(a => a.AuthorizeAsync(
                    It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>()))
                .ReturnsAsync(authorizationSucceeds ? AuthorizationResult.Success() : AuthorizationResult.Failed());

            return new OrderController(_service.Object, _userRepository.Object, authorization.Object, Mapper());
        }

        /// <summary>
        /// `InsertQueuedOrder` resolves its enterprise repository off the request services rather
        /// than the constructor, so a test that forgets this gets a false "tenant is invalid".
        /// </summary>
        private void AttachEnterpriseRepository(OrderController controller)
        {
            var services = new ServiceCollection();
            services.AddSingleton(_enterpriseRepository.Object);
            controller.ControllerContext.HttpContext.RequestServices = services.BuildServiceProvider();
        }

        private static IMapper Mapper()
        {
            var mapper = new Mock<IMapper>();
            mapper.Setup(m => m.Map<OrderDTO>(It.IsAny<Order>()))
                .Returns<Order>(o => new OrderDTO
                {
                    Id = o.Id,
                    TotalCost = 40m,
                    MarginAmount = 60m,
                    MarginPercent = 0.6m
                });
            return mapper.Object;
        }

        private Order NewOrder() => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = _enterpriseId,
            IsActive = true
        };

        private void MembershipIsGood(Guid? userId = null, Guid? enterpriseId = null) =>
            _userRepository.Setup(r => r.GetByIdAsync(userId ?? _userId))
                .ReturnsAsync(new User
                {
                    Id = userId ?? _userId,
                    IsActive = true,
                    EnterpriseId = enterpriseId ?? _enterpriseId
                });

        private void EnterpriseIsActive(Guid? enterpriseId = null, bool isActive = true) =>
            _enterpriseRepository.Setup(r => r.GetByIdAsync(enterpriseId ?? _enterpriseId))
                .ReturnsAsync(new Enterprise { Id = enterpriseId ?? _enterpriseId, IsActive = isActive });

        private static CreateOrderRequestDTO CreateRequest() => new()
        {
            CustomerId = Guid.NewGuid(),
            PaymentScheduledDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
            Items = [new OrderLineItemRequestDTO { ProductId = Guid.NewGuid(), Quantity = 1, Value = 100m }]
        };

        [Fact]
        public async Task AddOrder_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().AddOrder(CreateRequest()));
        }

        [Fact]
        public async Task AddOrder_WhenTheCallerNoLongerBelongsToTheTenant_IsUnauthorized()
        {
            MembershipIsGood(enterpriseId: Guid.NewGuid());

            Assert.IsType<UnauthorizedResult>(await Controller().AddOrder(CreateRequest()));
            _service.Verify(s => s.InsertOrderInQueue(It.IsAny<Order>()), Times.Never);
        }

        [Fact]
        public async Task AddOrder_EnqueuesItAgainstTheCallersTenantAndUser()
        {
            MembershipIsGood();

            Order? enqueued = null;
            _service.Setup(s => s.InsertOrderInQueue(It.IsAny<Order>()))
                .Callback<Order>(o => enqueued = o)
                .Returns(Task.CompletedTask);

            Assert.IsType<OkObjectResult>(await Controller().AddOrder(CreateRequest()));

            Assert.Equal(_enterpriseId, enqueued!.EnterpriseId);
            Assert.Equal(_userId, enqueued.UserId);
        }

        [Fact]
        public async Task InsertOrder_WhenTheCallerNoLongerBelongsToTheTenant_IsUnauthorized()
        {
            MembershipIsGood(enterpriseId: Guid.NewGuid());

            Assert.IsType<UnauthorizedResult>(await Controller().InsertOrder(CreateRequest()));
            _service.Verify(
                s => s.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()),
                Times.Never);
        }

        /// <summary>
        /// Whether the caller may push an order past a customer's credit limit is a separate
        /// permission, resolved here and passed down — never assumed by the service.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InsertOrder_PassesDownWhetherTheCallerMayOverrideTheCreditLimit(bool canOverride)
        {
            MembershipIsGood();

            bool? forwarded = null;
            _service.Setup(s => s.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .Callback((Order _, bool allowOverride, bool _, string? _) => forwarded = allowOverride)
                .ReturnsAsync(NewOrder());

            Assert.IsType<OkObjectResult>(await Controller(canReadCost: canOverride).InsertOrder(CreateRequest()));

            Assert.Equal(canOverride, forwarded);
        }

        /// <summary>
        /// The credit-limit refusal is one of the messages this code is willing to echo; anything
        /// carrying an inner exception is answered generically.
        /// </summary>
        [Fact]
        public async Task InsertOrder_WhenTheDatabaseRefuses_DoesNotEchoTheInnerDetail()
        {
            MembershipIsGood();
            _service.Setup(s => s.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .ThrowsAsync(new InsertDatabaseException(
                    "outer", new Exception("Violation of FK constraint 'FK_Order_Secret'")));

            var result = Assert.IsType<BadRequestObjectResult>(await Controller().InsertOrder(CreateRequest()));

            Assert.DoesNotContain("FK_Order_Secret", result.Value?.ToString());
        }

        /// <summary>
        /// The order worker authenticates as the machine user, so deriving the tenant from the
        /// caller's token would re-scope every queued order to the service account's enterprise.
        /// The payload carries the original context and it is validated here instead.
        /// </summary>
        [Fact]
        public async Task InsertQueuedOrder_CreatesAgainstThePayloadsTenantNotTheCallersToken()
        {
            var originalEnterprise = Guid.NewGuid();
            var originalUser = Guid.NewGuid();
            MembershipIsGood(originalUser, originalEnterprise);
            EnterpriseIsActive(originalEnterprise);

            Order? created = null;
            _service.Setup(s => s.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .Callback((Order order, bool _, bool _, string? _) => created = order)
                .ReturnsAsync(NewOrder());

            var result = await Controller().InsertQueuedOrder(new CreateQueuedOrderRequestDTO
            {
                EnterpriseId = originalEnterprise,
                UserId = originalUser,
                CustomerId = Guid.NewGuid(),
                MessageId = "msg-1",
                Items = [new OrderLineItemRequestDTO { ProductId = Guid.NewGuid(), Quantity = 1, Value = 10m }]
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(originalEnterprise, created!.EnterpriseId);
            Assert.Equal(originalUser, created.UserId);
            Assert.NotEqual(_enterpriseId, created.EnterpriseId);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task InsertQueuedOrder_WithHalfATenantContext_IsRefused(bool hasEnterprise, bool hasUser)
        {
            var result = await Controller().InsertQueuedOrder(new CreateQueuedOrderRequestDTO
            {
                EnterpriseId = hasEnterprise ? Guid.NewGuid() : Guid.Empty,
                UserId = hasUser ? Guid.NewGuid() : Guid.Empty
            });

            Assert.IsType<BadRequestObjectResult>(result);
            _service.Verify(
                s => s.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()),
                Times.Never);
        }

        /// <summary>
        /// A message can outlive the membership that enqueued it, so the pair is re-checked before
        /// the order is written rather than trusted because it once was valid.
        /// </summary>
        [Fact]
        public async Task InsertQueuedOrder_WhenTheUserNoLongerBelongsToThatTenant_IsRefused()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            MembershipIsGood(userId, Guid.NewGuid());

            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().InsertQueuedOrder(new CreateQueuedOrderRequestDTO
                {
                    EnterpriseId = enterpriseId,
                    UserId = userId
                }));

            Assert.Equal("Queued order tenant context is invalid.", result.Value);
        }

        [Fact]
        public async Task InsertQueuedOrder_ForAnEnterpriseThatWasDeactivated_IsRefused()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            MembershipIsGood(userId, enterpriseId);
            EnterpriseIsActive(enterpriseId, isActive: false);

            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().InsertQueuedOrder(new CreateQueuedOrderRequestDTO
                {
                    EnterpriseId = enterpriseId,
                    UserId = userId
                }));

            Assert.Equal("Queued order tenant context is invalid.", result.Value);
        }

        /// <summary>
        /// A redelivered message must not create a second order. The service says so by throwing,
        /// and the worker has to read that as success or it will retry for ever.
        /// </summary>
        [Fact]
        public async Task InsertQueuedOrder_OnARedeliveredMessage_AnswersOkRatherThanFailing()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            MembershipIsGood(userId, enterpriseId);
            EnterpriseIsActive(enterpriseId);

            _service.Setup(s => s.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), "msg-1"))
                .ThrowsAsync(new DuplicateMessageException("msg-1"));

            var result = Assert.IsType<OkObjectResult>(
                await Controller().InsertQueuedOrder(new CreateQueuedOrderRequestDTO
                {
                    EnterpriseId = enterpriseId,
                    UserId = userId,
                    MessageId = "msg-1"
                }));

            Assert.Equal("Order already processed", result.Value);
        }

        [Fact]
        public async Task UpdateOrder_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().UpdateOrder(new UpdateOrderRequestDTO()));
        }

        [Fact]
        public async Task UpdateOrder_ScopesTheWriteToTheCallersTenant()
        {
            var order = NewOrder();
            _service.Setup(s => s.Update(It.IsAny<Order>(), _enterpriseId)).Returns(order);

            Assert.IsType<OkObjectResult>(await Controller().UpdateOrder(
                new UpdateOrderRequestDTO { Id = order.Id, Status = 1 }));

            _service.VerifyAll();
        }

        [Fact]
        public async Task UpdateOrder_PassesTheTrackingTheStoreTyped()
        {
            Order? sent = null;
            _service.Setup(s => s.Update(It.IsAny<Order>(), _enterpriseId))
                .Callback<Order, Guid>((order, _) => sent = order)
                .Returns(NewOrder());

            await Controller().UpdateOrder(new UpdateOrderRequestDTO
            {
                Id = Guid.NewGuid(),
                Status = 3,
                TrackingCode = "BR123456789BR",
                TrackingUrl = "https://example.com/track/BR123456789BR"
            });

            Assert.Equal("BR123456789BR", sent!.TrackingCode);
            Assert.Equal("https://example.com/track/BR123456789BR", sent.TrackingUrl);
        }

        [Fact]
        public async Task UpdateOrder_OnAnOrderThatIsNotThisTenants_AnswersNoContent()
        {
            _service.Setup(s => s.Update(It.IsAny<Order>(), _enterpriseId)).Returns((Order)null!);

            Assert.IsType<NoContentResult>(await Controller().UpdateOrder(
                new UpdateOrderRequestDTO { Id = Guid.NewGuid(), Status = 1 }));
        }

        /// <summary>
        /// Cost and margin are Manager+ information. Every response goes through the same funnel,
        /// so a caller without `products.cost.read` gets nulls rather than a number.
        /// </summary>
        [Fact]
        public async Task UpdateOrder_WithoutTheCostPermission_StripsCostAndMarginFromTheResponse()
        {
            var order = NewOrder();
            _service.Setup(s => s.Update(It.IsAny<Order>(), _enterpriseId)).Returns(order);

            var result = Assert.IsType<OkObjectResult>(await Controller(canReadCost: false).UpdateOrder(
                new UpdateOrderRequestDTO { Id = order.Id, Status = 1 }));
            var dto = Assert.IsType<OrderDTO>(result.Value);

            Assert.Null(dto.TotalCost);
            Assert.Null(dto.MarginAmount);
            Assert.Null(dto.MarginPercent);
        }

        [Fact]
        public async Task UpdateOrder_WithTheCostPermission_KeepsCostAndMargin()
        {
            var order = NewOrder();
            _service.Setup(s => s.Update(It.IsAny<Order>(), _enterpriseId)).Returns(order);

            var result = Assert.IsType<OkObjectResult>(await Controller(canReadCost: true).UpdateOrder(
                new UpdateOrderRequestDTO { Id = order.Id, Status = 1 }));

            Assert.Equal(40m, Assert.IsType<OrderDTO>(result.Value).TotalCost);
        }

        [Fact]
        public async Task RefundOrder_WithAnOversizedReason_IsRefused()
        {
            var result = Assert.IsType<BadRequestObjectResult>(await Controller().RefundOrder(
                Guid.NewGuid(), new RefundRequestDTO { Reason = new string('r', 501) }));

            Assert.Contains("characters or fewer", result.Value?.ToString());
            _service.Verify(s => s.RefundAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RefundOrder_ScopesTheOrderToTheCallersTenant()
        {
            var order = NewOrder();
            _service.Setup(s => s.RefundAsync(order.Id, _enterpriseId, "Devolvido")).ReturnsAsync(order);

            Assert.IsType<OkObjectResult>(await Controller().RefundOrder(
                order.Id, new RefundRequestDTO { Reason = "Devolvido" }));

            _service.VerifyAll();
        }

        [Fact]
        public async Task RefundOrder_ForAnotherTenantsOrder_AnswersNoContent()
        {
            _service.Setup(s => s.RefundAsync(It.IsAny<Guid>(), _enterpriseId, It.IsAny<string>()))
                .ThrowsAsync(new NotFoundDatabaseException("no such order"));

            Assert.IsType<NoContentResult>(await Controller().RefundOrder(
                Guid.NewGuid(), new RefundRequestDTO { Reason = "Devolvido" }));
        }

        /// <summary>A body-less refund is legitimate: the reason is optional, not the request.</summary>
        [Fact]
        public async Task RefundOrder_WithNoBodyAtAll_RefundsWithAnEmptyReason()
        {
            var order = NewOrder();
            _service.Setup(s => s.RefundAsync(order.Id, _enterpriseId, string.Empty)).ReturnsAsync(order);

            Assert.IsType<OkObjectResult>(await Controller().RefundOrder(order.Id, null!));
            _service.VerifyAll();
        }

        [Fact]
        public async Task RefundOrder_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(
                await Anonymous().RefundOrder(Guid.NewGuid(), new RefundRequestDTO()));
        }

        [Fact]
        public async Task DeleteOrder_ScopesTheDeleteToTheCallersTenant()
        {
            var order = NewOrder();
            _service.Setup(s => s.Delete(order.Id, _enterpriseId)).Returns(order);

            Assert.IsType<OkObjectResult>(await Controller().DeleteOrder(order.Id));
            _service.VerifyAll();
        }

        [Fact]
        public async Task DeleteOrder_ForAnotherTenantsOrder_AnswersNoContent()
        {
            _service.Setup(s => s.Delete(It.IsAny<Guid>(), _enterpriseId)).Returns((Order)null!);

            Assert.IsType<NoContentResult>(await Controller().DeleteOrder(Guid.NewGuid()));
        }

        [Fact]
        public async Task DeleteOrder_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().DeleteOrder(Guid.NewGuid()));
        }
    }
}
