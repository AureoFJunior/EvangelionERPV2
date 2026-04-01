using AutoMapper;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    public class BillsControllerGetByOrderAuthorizationTests
    {
        private readonly Mock<IBillsService<Bill>> _billsServiceMock = new();
        private readonly Mock<IOrderService<Order>> _orderServiceMock = new();
        private readonly Mock<IRepository<User>> _userRepositoryMock = new();
        private readonly Mock<IMapper> _mapperMock = new();

        [Fact]
        public async Task GetByOrder_ReturnsUnauthorized_WhenEnterpriseClaimIsMissing()
        {
            var controller = CreateController(Array.Empty<Claim>());

            var response = await controller.GetByOrder(Guid.NewGuid());

            Assert.IsType<UnauthorizedResult>(response);
            _orderServiceMock.Verify(
                x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>()),
                Times.Never);
            _billsServiceMock.Verify(
                x => x.GetByOrderIdAsync(It.IsAny<Guid>()),
                Times.Never);
        }

        [Fact]
        public async Task GetByOrder_ReturnsNoContent_WhenOrderIsNotFoundForEnterprise()
        {
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            _orderServiceMock
                .Setup(x => x.GetByIdAsync(orderId, enterpriseId))
                .ReturnsAsync((Order?)null);

            var controller = CreateController(new[]
            {
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                new Claim(ClaimTypes.Sid, userId.ToString())
            });

            var response = await controller.GetByOrder(orderId);

            Assert.IsType<NoContentResult>(response);
            _billsServiceMock.Verify(
                x => x.GetByOrderIdAsync(It.IsAny<Guid>()),
                Times.Never);
        }

        [Fact]
        public async Task GetByOrder_ReturnsOk_WhenOrderBelongsToEnterprise()
        {
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var bill = new Bill
            {
                Id = Guid.NewGuid(),
                OrderId = orderId
            };
            var dto = new BillDTO
            {
                Id = bill.Id,
                OrderId = orderId
            };

            _orderServiceMock
                .Setup(x => x.GetByIdAsync(orderId, enterpriseId))
                .ReturnsAsync(new Order { Id = orderId, EnterpriseId = enterpriseId });

            _billsServiceMock
                .Setup(x => x.GetByOrderIdAsync(orderId))
                .ReturnsAsync(bill);

            _mapperMock
                .Setup(x => x.Map<BillDTO>(bill))
                .Returns(dto);

            var controller = CreateController(new[]
            {
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                new Claim(ClaimTypes.Sid, userId.ToString())
            });

            var response = await controller.GetByOrder(orderId);

            var ok = Assert.IsType<OkObjectResult>(response);
            var payload = Assert.IsType<BillDTO>(ok.Value);
            Assert.Equal(orderId, payload.OrderId);
            Assert.Equal(dto.Id, payload.Id);
        }

        private BillsController CreateController(IEnumerable<Claim> claims)
        {
            SetupManagementUserIfPossible(claims);

            var controller = new BillsController(
                _billsServiceMock.Object,
                _orderServiceMock.Object,
                _userRepositoryMock.Object,
                _mapperMock.Object);

            var identity = new ClaimsIdentity(claims, "TestAuthType");
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            };

            return controller;
        }

        private void SetupManagementUserIfPossible(IEnumerable<Claim> claims)
        {
            var userClaim = claims.FirstOrDefault(x => x.Type == ClaimTypes.Sid)?.Value;
            var enterpriseClaim = claims.FirstOrDefault(x => x.Type == ClaimTypes.GroupSid)?.Value;

            if (!Guid.TryParse(userClaim, out var userId) || userId == Guid.Empty)
                return;
            if (!Guid.TryParse(enterpriseClaim, out var enterpriseId) || enterpriseId == Guid.Empty)
                return;

            _userRepositoryMock
                .Setup(x => x.GetByIdAsync(userId))
                .ReturnsAsync(new User
                {
                    Id = userId,
                    EnterpriseId = enterpriseId,
                    AccessLevel = (short)EnumAccessLevel.Supervisor,
                    IsActive = true
                });
        }
    }
}
