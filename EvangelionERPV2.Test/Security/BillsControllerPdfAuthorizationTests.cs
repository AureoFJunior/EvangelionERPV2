using AutoMapper;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    public class BillsControllerPdfAuthorizationTests
    {
        private readonly Mock<IBillsService<Bill>> _billsServiceMock = new();
        private readonly Mock<IOrderService<Order>> _orderServiceMock = new();
        private readonly Mock<IRepository<User>> _userRepositoryMock = new();
        private readonly Mock<IMapper> _mapperMock = new();

        [Fact]
        public async Task Pdf_ReturnsUnauthorized_WhenEnterpriseClaimIsMissing()
        {
            var controller = CreateController(Array.Empty<Claim>());

            var response = await controller.Pdf(Guid.NewGuid());

            Assert.IsType<UnauthorizedResult>(response);
            _orderServiceMock.Verify(
                x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>()),
                Times.Never);
            _billsServiceMock.Verify(
                x => x.GetPdfAsync(It.IsAny<Guid>()),
                Times.Never);
        }

        [Fact]
        public async Task Pdf_ReturnsNoContent_WhenOrderIsNotFoundForEnterprise()
        {
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();

            _orderServiceMock
                .Setup(x => x.GetByIdAsync(orderId, enterpriseId))
                .ReturnsAsync((Order?)null);

            var controller = CreateController(new[]
            {
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())
            });

            var response = await controller.Pdf(orderId);

            Assert.IsType<NoContentResult>(response);
            _billsServiceMock.Verify(
                x => x.GetPdfAsync(It.IsAny<Guid>()),
                Times.Never);
        }

        [Fact]
        public async Task Pdf_ReturnsFile_WhenOrderBelongsToEnterprise()
        {
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var expectedContent = new byte[] { 1, 2, 3 };

            _orderServiceMock
                .Setup(x => x.GetByIdAsync(orderId, enterpriseId))
                .ReturnsAsync(new Order { Id = orderId, EnterpriseId = enterpriseId });

            _billsServiceMock
                .Setup(x => x.GetPdfAsync(orderId))
                .ReturnsAsync(expectedContent);

            var controller = CreateController(new[]
            {
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())
            });

            var response = await controller.Pdf(orderId);

            var fileResult = Assert.IsType<FileContentResult>(response);
            Assert.Equal("application/pdf", fileResult.ContentType);
            Assert.Equal(expectedContent, fileResult.FileContents);
        }

        private BillsController CreateController(IEnumerable<Claim> claims)
        {
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
    }
}
