using AutoMapper;
using EvangelionERPV2.NFeModule.Application.Interface;
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
    public class NFeControllerConsultAuthorizationTests
    {
        private const string ValidAccessKey = "12345678901234567890123456789012345678901234";
        private readonly Mock<INFeService<NFeDocument>> _nfeServiceMock = new();
        private readonly Mock<IOrderService<Order>> _orderServiceMock = new();
        private readonly Mock<IRepository<User>> _userRepositoryMock = new();
        private readonly Mock<IMapper> _mapperMock = new();

        [Fact]
        public async Task Consult_ReturnsUnauthorized_WhenEnterpriseClaimIsMissing()
        {
            var controller = CreateController(Array.Empty<Claim>());

            var response = await controller.Consult(ValidAccessKey);

            Assert.IsType<UnauthorizedResult>(response);
            _nfeServiceMock.Verify(x => x.ConsultAsync(It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
            _orderServiceMock.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task Consult_ReturnsNoContent_WhenOrderIsNotFoundForEnterprise()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            var accessKey = ValidAccessKey;
            var document = new NFeDocument
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Type = NFeDocumentType.NFe,
                Status = NFeStatus.Authorized,
                AccessKey = accessKey
            };

            _nfeServiceMock
                .Setup(x => x.ConsultAsync(accessKey, enterpriseId))
                .ReturnsAsync(document);

            _orderServiceMock
                .Setup(x => x.GetByIdAsync(orderId, enterpriseId))
                .ReturnsAsync((Order?)null);

            var controller = CreateController(new[]
            {
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                new Claim(ClaimTypes.Sid, userId.ToString())
            });

            var response = await controller.Consult(accessKey);

            Assert.IsType<NoContentResult>(response);
            _mapperMock.Verify(x => x.Map<NFeDocumentDTO>(It.IsAny<NFeDocument>()), Times.Never);
        }

        [Fact]
        public async Task Consult_ReturnsOk_WhenDocumentBelongsToEnterprise()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            var accessKey = ValidAccessKey;
            var document = new NFeDocument
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Type = NFeDocumentType.NFe,
                Status = NFeStatus.Authorized,
                AccessKey = accessKey
            };
            var dto = new NFeDocumentDTO
            {
                Id = document.Id,
                OrderId = orderId,
                Type = NFeDocumentType.NFe,
                Status = NFeStatus.Authorized,
                AccessKey = accessKey
            };

            _nfeServiceMock
                .Setup(x => x.ConsultAsync(accessKey, enterpriseId))
                .ReturnsAsync(document);

            _orderServiceMock
                .Setup(x => x.GetByIdAsync(orderId, enterpriseId))
                .ReturnsAsync(new Order { Id = orderId, EnterpriseId = enterpriseId });

            _mapperMock
                .Setup(x => x.Map<NFeDocumentDTO>(document))
                .Returns(dto);

            var controller = CreateController(new[]
            {
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                new Claim(ClaimTypes.Sid, userId.ToString())
            });

            var response = await controller.Consult(accessKey);

            var ok = Assert.IsType<OkObjectResult>(response);
            var payload = Assert.IsType<NFeDocumentDTO>(ok.Value);
            Assert.Equal(orderId, payload.OrderId);
            Assert.Equal(accessKey, payload.AccessKey);
        }

        private NFeController CreateController(IEnumerable<Claim> claims)
        {
            SetupManagementUserIfPossible(claims);

            var controller = new NFeController(
                _nfeServiceMock.Object,
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
