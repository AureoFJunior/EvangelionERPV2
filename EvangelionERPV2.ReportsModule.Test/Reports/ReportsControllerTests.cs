using System.Security.Claims;
using EvangelionERPV2.ReportsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EvangelionERPV2.ReportsModule.Test.Reports
{
    public class ReportsControllerTests
    {
        [Fact]
        public async Task GetReports_WhenClaimsMissing_ReturnsUnauthorized()
        {
            var service = new Mock<IReportsService>();
            var controller = BuildController(service, null, null);

            var result = await controller.GetReports();

            Assert.IsType<UnauthorizedResult>(result);
            service.Verify(s => s.GetUserReportsAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task GetReports_WhenClaimsValid_ReturnsOk()
        {
            var service = new Mock<IReportsService>();
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            service.Setup(s => s.GetUserReportsAsync(enterpriseId, userId))
                .ReturnsAsync(new List<ReportListItemDTO>
                {
                    new ReportListItemDTO
                    {
                        Id = Guid.NewGuid(),
                        Date = DateTime.UtcNow,
                        Title = "Stock Report",
                        Description = "Current stock",
                        Type = EnumReportType.Stock,
                        Icon = "package"
                    }
                });

            var controller = BuildController(service, enterpriseId, userId);
            var result = await controller.GetReports();

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(ok.Value);
            service.Verify(s => s.GetUserReportsAsync(enterpriseId, userId), Times.Once);
        }

        [Fact]
        public async Task GenerateReport_WithNullRequest_ReturnsBadRequest()
        {
            var service = new Mock<IReportsService>();
            var controller = BuildController(service, Guid.NewGuid(), Guid.NewGuid());

            var result = await controller.GenerateReport(null!);

            Assert.IsType<BadRequestObjectResult>(result);
            service.Verify(s => s.GenerateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<EnumReportType>()), Times.Never);
        }

        [Fact]
        public async Task GenerateReport_WithValidRequest_ReturnsOk()
        {
            var service = new Mock<IReportsService>();
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var generated = new ReportListItemDTO
            {
                Id = Guid.NewGuid(),
                Date = DateTime.UtcNow,
                Title = "Monthly Billing Report",
                Description = "Current month billing",
                Type = EnumReportType.MonthlyBilling,
                Icon = "bar-chart-2"
            };

            service.Setup(s => s.GenerateAsync(enterpriseId, userId, EnumReportType.MonthlyBilling))
                .ReturnsAsync(generated);

            var controller = BuildController(service, enterpriseId, userId);
            var result = await controller.GenerateReport(new CreateReportRequestDTO { Type = EnumReportType.MonthlyBilling });

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(generated, ok.Value);
            service.Verify(s => s.GenerateAsync(enterpriseId, userId, EnumReportType.MonthlyBilling), Times.Once);
        }

        [Theory]
        [InlineData(EnumReportType.TopProductsRevenue)]
        [InlineData(EnumReportType.SalesByStatus)]
        [InlineData(EnumReportType.PayablesOverview)]
        public async Task GenerateReport_WithNewReportTypes_ReturnsOk(EnumReportType reportType)
        {
            var service = new Mock<IReportsService>();
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var generated = new ReportListItemDTO
            {
                Id = Guid.NewGuid(),
                Date = DateTime.UtcNow,
                Title = "Generated Report",
                Description = "Generated content",
                Type = reportType,
                Icon = "file-text"
            };

            service.Setup(s => s.GenerateAsync(enterpriseId, userId, reportType))
                .ReturnsAsync(generated);

            var controller = BuildController(service, enterpriseId, userId);
            var result = await controller.GenerateReport(new CreateReportRequestDTO { Type = reportType });

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(generated, ok.Value);
            service.Verify(s => s.GenerateAsync(enterpriseId, userId, reportType), Times.Once);
        }

        [Fact]
        public async Task GenerateReport_WhenNoDataAvailable_ReturnsBadRequest()
        {
            var service = new Mock<IReportsService>();
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            service.Setup(s => s.GenerateAsync(enterpriseId, userId, EnumReportType.PayablesOverview))
                .ThrowsAsync(new InsertDatabaseException("No payable bills found in the current month to generate this report."));

            var controller = BuildController(service, enterpriseId, userId);
            var result = await controller.GenerateReport(new CreateReportRequestDTO { Type = EnumReportType.PayablesOverview });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.NotNull(badRequest.Value);
            service.Verify(s => s.GenerateAsync(enterpriseId, userId, EnumReportType.PayablesOverview), Times.Once);
        }

        [Fact]
        public async Task GetReportById_WhenNotFound_ReturnsNoContent()
        {
            var service = new Mock<IReportsService>();
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var reportId = Guid.NewGuid();

            service.Setup(s => s.GetByIdAsync(enterpriseId, userId, reportId))
                .ReturnsAsync((ReportDetailDTO?)null);

            var controller = BuildController(service, enterpriseId, userId);
            var result = await controller.GetReportById(reportId);

            Assert.IsType<NoContentResult>(result);
            service.Verify(s => s.GetByIdAsync(enterpriseId, userId, reportId), Times.Once);
        }

        private static ReportsController BuildController(
            Mock<IReportsService> reportsService,
            Guid? enterpriseId,
            Guid? userId)
        {
            var controller = new ReportsController(reportsService.Object);

            var claims = new List<Claim>();
            if (enterpriseId.HasValue)
                claims.Add(new Claim(ClaimTypes.GroupSid, enterpriseId.Value.ToString()));
            if (userId.HasValue)
                claims.Add(new Claim(ClaimTypes.Sid, userId.Value.ToString()));

            var identity = new ClaimsIdentity(claims, "TestAuth");
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
