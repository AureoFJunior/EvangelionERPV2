using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    public class CashFlowForecastControllerErrorSanitizationTests
    {
        [Fact]
        public async Task RunSimulation_WhenArgumentExceptionHasInnerException_ReturnsGenericBadRequestMessage()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var service = new Mock<ICashFlowForecastService>(MockBehavior.Strict);

            service
                .Setup(x => x.RunSimulationAsync(enterpriseId, userId, It.IsAny<RunSimulationRequestDTO>()))
                .ThrowsAsync(new ArgumentException(
                    "Database timeout. Connection string=Server=prod;User Id=admin;",
                    new InvalidOperationException("Sensitive internal error")));

            var controller = CreateController(service.Object, enterpriseId, userId);

            var result = await controller.RunSimulation(new RunSimulationRequestDTO());

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid simulation request.", badRequest.Value);
        }

        [Fact]
        public async Task RunSimulation_WhenArgumentExceptionHasNoInnerException_ReturnsOriginalBadRequestMessage()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var service = new Mock<ICashFlowForecastService>(MockBehavior.Strict);

            service
                .Setup(x => x.RunSimulationAsync(enterpriseId, userId, It.IsAny<RunSimulationRequestDTO>()))
                .ThrowsAsync(new ArgumentException("Horizon must be 30, 60 or 90 days."));

            var controller = CreateController(service.Object, enterpriseId, userId);

            var result = await controller.RunSimulation(new RunSimulationRequestDTO());

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid simulation request.", badRequest.Value);
        }

        private static CashFlowForecastController CreateController(
            ICashFlowForecastService service,
            Guid enterpriseId,
            Guid userId)
        {
            var controller = new CashFlowForecastController(service);
            var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                new Claim(ClaimTypes.Sid, userId.ToString())
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
    }
}
