using System.Text;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Every action here reads the tenant from the token and passes it to the service — which is
    /// exactly why the "no tenant claim" case matters: without the guard, `Guid.Empty` would reach
    /// a query that scopes on it.
    /// </summary>
    public class SalesPerformanceControllerTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Mock<ISalesPerformanceService> _service = new();

        private SalesPerformanceController Controller() =>
            new SalesPerformanceController(_service.Object).WithTenant(_enterpriseId);

        private SalesPerformanceController Anonymous() =>
            new SalesPerformanceController(_service.Object).WithoutTenant();

        [Fact]
        public async Task GetPerformance_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetPerformance(2026, 9));
        }

        [Fact]
        public async Task GetPerformance_PassesTheCallersTenantToTheService()
        {
            _service.Setup(s => s.GetPerformanceAsync(_enterpriseId, 2026, 9, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SalesPerformanceDTO { Year = 2026, Month = 9 });

            var result = Assert.IsType<OkObjectResult>(await Controller().GetPerformance(2026, 9));

            Assert.Equal(2026, Assert.IsType<SalesPerformanceDTO>(result.Value).Year);
            _service.VerifyAll();
        }

        /// <summary>
        /// The message that reaches the caller is one this code wrote, never the database's own —
        /// the allow-list shape `QuoteController` uses.
        /// </summary>
        [Fact]
        public async Task GetPerformance_WhenTheServiceRefuses_AnswersBadRequestWithoutTheInnerDetail()
        {
            _service.Setup(s => s.GetPerformanceAsync(_enterpriseId, 2026, 9, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InsertDatabaseException(
                    "outer", new Exception("Violation of UNIQUE KEY constraint 'IX_SalesGoal_Secret'")));

            var result = Assert.IsType<BadRequestObjectResult>(await Controller().GetPerformance(2026, 9));

            Assert.DoesNotContain("IX_SalesGoal_Secret", result.Value?.ToString());
        }

        [Fact]
        public async Task GetSellerPerformance_WithNoRowForThatSeller_AnswersNoContent()
        {
            var sellerId = Guid.NewGuid();
            _service.Setup(s => s.GetSellerPerformanceAsync(_enterpriseId, sellerId, 2026, 9, It.IsAny<CancellationToken>()))
                .ReturnsAsync((SalesPerformanceRowDTO?)null);

            Assert.IsType<NoContentResult>(await Controller().GetSellerPerformance(sellerId, 2026, 9));
        }

        [Fact]
        public async Task GetSellerPerformance_WithARow_AnswersIt()
        {
            var sellerId = Guid.NewGuid();
            _service.Setup(s => s.GetSellerPerformanceAsync(_enterpriseId, sellerId, 2026, 9, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SalesPerformanceRowDTO { UserId = sellerId, SellerName = "Maria", SoldAmount = 10m });

            var result = Assert.IsType<OkObjectResult>(await Controller().GetSellerPerformance(sellerId, 2026, 9));

            Assert.Equal("Maria", Assert.IsType<SalesPerformanceRowDTO>(result.Value).SellerName);
        }

        [Fact]
        public async Task GetSellerPerformance_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetSellerPerformance(Guid.NewGuid(), 2026, 9));
        }

        [Fact]
        public async Task GetGoals_AnswersTheTenantsGoals()
        {
            _service.Setup(s => s.GetGoalsAsync(_enterpriseId, 2026, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new SalesGoalDTO { Year = 2026, Month = 9, TargetAmount = 1000m }]);

            var result = Assert.IsType<OkObjectResult>(await Controller().GetGoals(2026));

            Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<SalesGoalDTO>>(result.Value));
        }

        [Fact]
        public async Task GetGoals_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetGoals(2026));
        }

        [Fact]
        public async Task UpsertGoal_ProjectsTheSavedGoalRatherThanEchoingTheRequest()
        {
            var goalId = Guid.NewGuid();
            var sellerId = Guid.NewGuid();
            var request = new UpsertSalesGoalRequestDTO { UserId = sellerId, Year = 2026, Month = 9, TargetAmount = 5000m };

            _service.Setup(s => s.UpsertGoalAsync(_enterpriseId, request, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SalesGoal
                {
                    Id = goalId,
                    UserId = sellerId,
                    Year = 2026,
                    Month = 9,
                    TargetAmount = 4999m
                });

            var result = Assert.IsType<OkObjectResult>(await Controller().UpsertGoal(request));
            var dto = Assert.IsType<SalesGoalDTO>(result.Value);

            Assert.Equal(goalId, dto.Id);
            Assert.Equal(4999m, dto.TargetAmount);
        }

        /// <summary>A target for somebody who is not a seller here is not found, not an error.</summary>
        [Fact]
        public async Task UpsertGoal_ForAUserTheTenantDoesNotHave_AnswersNoContent()
        {
            var request = new UpsertSalesGoalRequestDTO { UserId = Guid.NewGuid(), Year = 2026, Month = 9 };

            _service.Setup(s => s.UpsertGoalAsync(_enterpriseId, request, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NotFoundDatabaseException("no such seller"));

            Assert.IsType<NoContentResult>(await Controller().UpsertGoal(request));
        }

        [Fact]
        public async Task UpsertGoal_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(
                await Anonymous().UpsertGoal(new UpsertSalesGoalRequestDTO { Year = 2026, Month = 9 }));
        }
    }

    public class BankReconciliationControllerTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Mock<IBankReconciliationService> _service = new();

        private BankReconciliationController Controller() =>
            new BankReconciliationController(_service.Object).WithTenant(_enterpriseId);

        private BankReconciliationController Anonymous() =>
            new BankReconciliationController(_service.Object).WithoutTenant();

        private static ImportBankStatementRequestDTO Upload(long length, string content = "OFXHEADER:100")
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var file = new Mock<IFormFile>();
            file.SetupGet(f => f.Length).Returns(length);
            file.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns<Stream, CancellationToken>((target, token) => target.WriteAsync(bytes, token).AsTask());

            return new ImportBankStatementRequestDTO { File = file.Object };
        }

        [Fact]
        public async Task ImportStatement_WithNoFile_IsRefusedBeforeTheTenantIsEvenRead()
        {
            var result = Assert.IsType<BadRequestObjectResult>(
                await Anonymous().ImportStatement(new ImportBankStatementRequestDTO()));

            Assert.Equal("Statement file is required.", result.Value);
        }

        [Fact]
        public async Task ImportStatement_WithAnEmptyFile_IsRefused()
        {
            Assert.IsType<BadRequestObjectResult>(await Controller().ImportStatement(Upload(0)));
        }

        /// <summary>
        /// The 8 MB ceiling is checked before the file is copied into memory, so an oversized
        /// upload never becomes an 8 MB allocation.
        /// </summary>
        [Fact]
        public async Task ImportStatement_OverEightMegabytes_IsRefusedWithoutBuffering()
        {
            var request = Upload((8 * 1024 * 1024) + 1);

            var result = Assert.IsType<BadRequestObjectResult>(await Controller().ImportStatement(request));

            Assert.Equal("Statement file is too large.", result.Value);
            Mock.Get(request.File!).Verify(
                f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ImportStatement_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().ImportStatement(Upload(10)));
        }

        [Fact]
        public async Task ImportStatement_HandsTheServiceTheFilesBytes()
        {
            byte[]? captured = null;
            _service.Setup(s => s.ImportAsync(_enterpriseId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .Callback<Guid, byte[], CancellationToken>((_, content, _) => captured = content)
                .ReturnsAsync(new BankStatementImportResultDTO { TotalLines = 3 });

            var result = Assert.IsType<OkObjectResult>(await Controller().ImportStatement(Upload(13)));

            Assert.Equal(3, Assert.IsType<BankStatementImportResultDTO>(result.Value).TotalLines);
            Assert.Equal("OFXHEADER:100", Encoding.UTF8.GetString(captured!));
        }

        /// <summary>
        /// A file that is not OFX is the user's mistake, not a server fault — and the parser's own
        /// message never reaches them.
        /// </summary>
        [Fact]
        public async Task ImportStatement_WithSomethingThatIsNotOfx_AnswersBadRequest()
        {
            _service.Setup(s => s.ImportAsync(_enterpriseId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FormatException("offset 0x41 in /tmp/upload"));

            var result = Assert.IsType<BadRequestObjectResult>(await Controller().ImportStatement(Upload(13)));

            Assert.Equal("The file could not be read as an OFX statement.", result.Value);
        }

        [Theory]
        [InlineData(null, null, 1, 50)]
        [InlineData(2, 500, 2, 200)]
        public async Task GetUnclearedLines_NormalisesPaginationBeforeTheService(
            int? pageNumber, int? pageSize, int expectedPage, int expectedSize)
        {
            _service.Setup(s => s.GetUnclearedAsync(_enterpriseId, expectedPage, expectedSize))
                .ReturnsAsync([]);

            Assert.IsType<OkObjectResult>(await Controller().GetUnclearedLines(pageNumber, pageSize));
            _service.VerifyAll();
        }

        [Fact]
        public async Task GetUnclearedLines_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetUnclearedLines());
        }

        [Fact]
        public async Task MatchLine_PointsTheLineAtTheDocument()
        {
            var lineId = Guid.NewGuid();
            var referenceId = Guid.NewGuid();

            _service.Setup(s => s.ResolveAsync(_enterpriseId, lineId, referenceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BankStatementLineDTO { Id = lineId, Status = "Matched" });

            var result = Assert.IsType<OkObjectResult>(await Controller().MatchLine(
                new ResolveBankStatementLineRequestDTO { LineId = lineId, MatchedReferenceId = referenceId }));

            Assert.Equal("Matched", Assert.IsType<BankStatementLineDTO>(result.Value).Status);
        }

        /// <summary>
        /// Ignoring is the same call with no reference — a bank fee or an own-account transfer
        /// corresponds to no ERP document, and inventing one would be worse than clearing it.
        /// </summary>
        [Fact]
        public async Task IgnoreLine_ResolvesTheLineAgainstNoDocument()
        {
            var lineId = Guid.NewGuid();

            _service.Setup(s => s.ResolveAsync(_enterpriseId, lineId, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BankStatementLineDTO { Id = lineId, Status = "Ignored" });

            var result = Assert.IsType<OkObjectResult>(await Controller().IgnoreLine(
                new IgnoreBankStatementLineRequestDTO { LineId = lineId }));

            Assert.Equal("Ignored", Assert.IsType<BankStatementLineDTO>(result.Value).Status);
            _service.VerifyAll();
        }

        [Fact]
        public async Task MatchLine_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(
                await Anonymous().MatchLine(new ResolveBankStatementLineRequestDTO()));
        }

        [Fact]
        public async Task IgnoreLine_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(
                await Anonymous().IgnoreLine(new IgnoreBankStatementLineRequestDTO()));
        }

        [Fact]
        public async Task GetVariance_AnswersTheTenantsVariance()
        {
            _service.Setup(s => s.GetVarianceAsync(_enterpriseId))
                .ReturnsAsync(new BankReconciliationVarianceDTO());

            Assert.IsType<OkObjectResult>(await Controller().GetVariance());
            _service.VerifyAll();
        }

        [Fact]
        public async Task GetVariance_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetVariance());
        }
    }
}
