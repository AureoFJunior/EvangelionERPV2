using AutoMapper;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Payroll bills are payable bills that a payables clerk must not see: a folha row carries what
    /// every colleague earns. The controller hides them behind a second policy on top of
    /// <c>payablebills.read</c>, and these assert that it holds on every route that can reach one.
    /// </summary>
    public class PayableBillsControllerTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Mock<IPayableBillService> _service = new();
        private readonly Mock<IRepository<User>> _userRepository = new();

        private PayableBillsController Controller(bool canReadPayroll = false, string? query = null, bool canReceiveProducts = false)
        {
            var authorization = new Mock<IAuthorizationService>();
            authorization
                .Setup(a => a.AuthorizeAsync(
                    It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>()))
                .ReturnsAsync((System.Security.Claims.ClaimsPrincipal _, object? _, string policy) =>
                    policy == RbacPolicies.Payroll.Read && canReadPayroll
                    || policy == RbacPolicies.PayableBills.MarkProductsReceived && canReceiveProducts
                        ? AuthorizationResult.Success()
                        : AuthorizationResult.Failed());

            var controller = new PayableBillsController(
                    _service.Object, Mapper(), authorization.Object)
                .WithTenant(_enterpriseId);

            if (query != null)
                controller.ControllerContext.HttpContext.Request.QueryString = new QueryString(query);

            return controller;
        }

        private PayableBillsController Anonymous()
        {
            var authorization = new Mock<IAuthorizationService>();
            authorization
                .Setup(a => a.AuthorizeAsync(
                    It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>()))
                .ReturnsAsync(AuthorizationResult.Failed());

            return new PayableBillsController(_service.Object, Mapper(), authorization.Object)
                .WithoutTenant();
        }

        private static IMapper Mapper()
        {
            var mapper = new Mock<IMapper>();
            mapper.Setup(m => m.Map<PayableBillDTO>(It.IsAny<PayableBill>()))
                .Returns<PayableBill>(b => new PayableBillDTO { Id = b.Id, Description = b.Description });
            mapper.Setup(m => m.Map<IEnumerable<PayableBillDTO>>(It.IsAny<IEnumerable<PayableBill>>()))
                .Returns<IEnumerable<PayableBill>>(all => all.Select(b => new PayableBillDTO { Id = b.Id }));
            return mapper.Object;
        }

        private static PayableBill Bill(Guid enterpriseId, int billType = (int)EnumPayableBillType.Accounts) => new()
        {
            Id = Guid.NewGuid(),
            Description = "Aluguel",
            BillType = billType,
            DueDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
            EnterpriseId = enterpriseId,
            IsActive = true
        };

        [Fact]
        public async Task GetPayableBills_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetPayableBills());
        }

        /// <summary>
        /// A month without a year would scope nothing, and a year without a month would scope a
        /// whole year by accident. They arrive together or not at all.
        /// </summary>
        [Theory]
        [InlineData(2026, null)]
        [InlineData(null, 9)]
        public async Task GetPayableBills_WithHalfAPeriod_IsRefused(int? year, int? month)
        {
            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().GetPayableBills(year: year, month: month));

            Assert.Equal("year and month must be provided together.", result.Value);
        }

        [Theory]
        [InlineData(2026, 0)]
        [InlineData(2026, 13)]
        public async Task GetPayableBills_WithAMonthOutsideTheYear_IsRefused(int year, int month)
        {
            Assert.IsType<BadRequestObjectResult>(await Controller().GetPayableBills(year: year, month: month));
        }

        [Theory]
        [InlineData(1999, 9)]
        [InlineData(2101, 9)]
        public async Task GetPayableBills_WithAYearOutsideTheSupportedRange_IsRefused(int year, int month)
        {
            Assert.IsType<BadRequestObjectResult>(await Controller().GetPayableBills(year: year, month: month));
        }

        /// <summary>
        /// The three-state filter: absent means "live rows only", "all" means no filter, and
        /// anything unparseable is refused rather than silently read as false.
        /// </summary>
        [Theory]
        [InlineData(null, null, true)]
        [InlineData("?isActive=all", "all", null)]
        [InlineData("?isActive=false", "false", false)]
        [InlineData("?isActive=true", "true", true)]
        [InlineData("?isActive=", "", null)]
        public async Task GetPayableBills_ResolvesTheIsActiveFilter(string? query, string? isActive, bool? expected)
        {
            bool? captured = true;
            _service.Setup(s => s.GetByEnterpriseIdAsync(
                    _enterpriseId, It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<bool?>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<bool>()))
                .Callback((Guid _, int? _, int? _, bool? active, int? _, int? _, int? _, bool _) => captured = active)
                .ReturnsAsync([]);

            Assert.IsType<OkObjectResult>(await Controller(query: query).GetPayableBills(isActive: isActive));
            Assert.Equal(expected, captured);
        }

        [Fact]
        public async Task GetPayableBills_WithAnIsActiveThatIsNotABool_IsRefused()
        {
            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller(query: "?isActive=maybe").GetPayableBills(isActive: "maybe"));

            Assert.Equal("isActive must be true, false or all.", result.Value);
        }

        [Fact]
        public async Task GetPayableBills_AskingForPayrollWithoutThePayrollPermission_IsForbidden()
        {
            var result = await Controller(canReadPayroll: false)
                .GetPayableBills(billType: (int)EnumPayableBillType.Payroll);

            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task GetPayableBills_AskingForPayrollWithThePayrollPermission_IsAllowed()
        {
            _service.Setup(s => s.GetByEnterpriseIdAsync(
                    _enterpriseId, It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<bool?>(),
                    (int)EnumPayableBillType.Payroll, It.IsAny<int?>(), It.IsAny<int?>(), true))
                .ReturnsAsync([]);

            var result = await Controller(canReadPayroll: true)
                .GetPayableBills(billType: (int)EnumPayableBillType.Payroll);

            Assert.IsType<OkObjectResult>(result);
            _service.VerifyAll();
        }

        /// <summary>
        /// The permission is forwarded rather than re-derived: a caller without it gets a list the
        /// service itself filtered, not one the controller trimmed afterwards.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetPayableBills_TellsTheServiceWhetherPayrollMayBeIncluded(bool canReadPayroll)
        {
            bool? forwarded = null;
            _service.Setup(s => s.GetByEnterpriseIdAsync(
                    _enterpriseId, It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<bool?>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<bool>()))
                .Callback((Guid _, int? _, int? _, bool? _, int? _, int? _, int? _, bool includePayroll) =>
                    forwarded = includePayroll)
                .ReturnsAsync([]);

            await Controller(canReadPayroll).GetPayableBills();

            Assert.Equal(canReadPayroll, forwarded);
        }

        [Theory]
        [InlineData(null, null, 1, 50)]
        [InlineData(2, 100000, 2, 200)]
        public async Task GetPayableBills_NormalisesPagination(int? page, int? size, int expectedPage, int expectedSize)
        {
            _service.Setup(s => s.GetByEnterpriseIdAsync(
                    _enterpriseId, expectedPage, expectedSize, It.IsAny<bool?>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<bool>()))
                .ReturnsAsync([]);

            await Controller().GetPayableBills(page, size);
            _service.VerifyAll();
        }

        [Fact]
        public async Task GetPayableBill_ThatDoesNotExist_AnswersNoContent()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.GetByIdAsync(id, _enterpriseId)).ReturnsAsync((PayableBill?)null);

            Assert.IsType<NoContentResult>(await Controller().GetPayableBill(id));
        }

        /// <summary>
        /// A payroll bill read by id answers exactly as a missing one does. Anything else — a 403,
        /// a different message — would confirm the row exists and what kind it is.
        /// </summary>
        [Fact]
        public async Task GetPayableBill_ThatIsPayroll_LooksIdenticalToOneThatDoesNotExist()
        {
            var bill = Bill(_enterpriseId, (int)EnumPayableBillType.Payroll);
            _service.Setup(s => s.GetByIdAsync(bill.Id, _enterpriseId)).ReturnsAsync(bill);

            Assert.IsType<NoContentResult>(await Controller(canReadPayroll: false).GetPayableBill(bill.Id));
        }

        [Fact]
        public async Task GetPayableBill_ThatIsPayroll_IsVisibleWithThePayrollPermission()
        {
            var bill = Bill(_enterpriseId, (int)EnumPayableBillType.Payroll);
            _service.Setup(s => s.GetByIdAsync(bill.Id, _enterpriseId)).ReturnsAsync(bill);

            Assert.IsType<OkObjectResult>(await Controller(canReadPayroll: true).GetPayableBill(bill.Id));
        }

        [Fact]
        public async Task AddPayableBill_StampsTheCallersTenantOnWhatIsSaved()
        {
            PayableBill? created = null;
            _service.Setup(s => s.CreateAsync(It.IsAny<PayableBill>(), false, false))
                .Callback<PayableBill, bool, bool>((bill, _, _) => created = bill)
                .ReturnsAsync((PayableBill bill, bool _, bool _) => bill);

            var result = await Controller().AddPayableBill(new UpsertPayableBillRequestDTO
            {
                Description = "Aluguel",
                BillType = (int)EnumPayableBillType.Accounts,
                DueDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
                Amount = 2500m
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(_enterpriseId, created!.EnterpriseId);
            Assert.Equal(2500m, created.Amount);
        }

        [Fact]
        public async Task AddPayableBill_ReceivingProductsRequiresTheSeparateReceivePermission()
        {
            var result = await Controller().AddPayableBill(new UpsertPayableBillRequestDTO
            {
                Description = "Compra",
                BillType = (int)EnumPayableBillType.Accounts,
                DueDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
                Amount = 250m,
                ReceiveProducts = true
            });

            Assert.IsType<ForbidResult>(result);
            _service.Verify(s => s.CreateWithReceiptAsync(It.IsAny<PayableBill>()), Times.Never);
        }

        [Fact]
        public async Task AddPayableBill_WithReceivePermissionUsesAtomicReceiptPath()
        {
            _service.Setup(s => s.CreateWithReceiptAsync(It.IsAny<PayableBill>()))
                .ReturnsAsync((PayableBill bill) => bill);

            var result = await Controller(canReceiveProducts: true).AddPayableBill(new UpsertPayableBillRequestDTO
            {
                Description = "Compra",
                BillType = (int)EnumPayableBillType.Accounts,
                DueDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
                Amount = 250m,
                ReceiveProducts = true
            });

            Assert.IsType<OkObjectResult>(result);
            _service.Verify(s => s.CreateWithReceiptAsync(It.Is<PayableBill>(bill => bill.EnterpriseId == _enterpriseId)), Times.Once);
        }

        [Fact]
        public async Task AddPayableBill_OfTypePayrollWithoutThePayrollPermission_IsForbidden()
        {
            var result = await Controller(canReadPayroll: false).AddPayableBill(new UpsertPayableBillRequestDTO
            {
                Description = "Folha",
                BillType = (int)EnumPayableBillType.Payroll,
                DueDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
                Amount = 1m
            });

            Assert.IsType<ForbidResult>(result);
            _service.Verify(s => s.CreateAsync(It.IsAny<PayableBill>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        }

        /// <summary>
        /// The inner exception is where a constraint name and a column list live. Anything with one
        /// is answered generically; only the messages this codebase wrote are echoed.
        /// </summary>
        [Fact]
        public async Task AddPayableBill_WhenTheDatabaseRefuses_DoesNotEchoTheInnerDetail()
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<PayableBill>(), false, false))
                .ThrowsAsync(new InsertDatabaseException(
                    "outer", new Exception("Violation of UNIQUE KEY constraint 'IX_PayableBill_Secret'")));

            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().AddPayableBill(new UpsertPayableBillRequestDTO
                {
                    Description = "Aluguel",
                    DueDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
                    Amount = 1m
                }));

            Assert.Equal("An internal error occurred. Please try again later.", result.Value);
        }

        [Fact]
        public async Task AddPayableBill_WhenAnItemsProductIsMissing_SaysSoWithoutTheId()
        {
            var productId = Guid.NewGuid();
            _service.Setup(s => s.CreateAsync(It.IsAny<PayableBill>(), false, false))
                .ThrowsAsync(new InsertDatabaseException($"Product [{productId}] was not found."));

            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().AddPayableBill(new UpsertPayableBillRequestDTO
                {
                    Description = "Compra",
                    DueDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
                    Amount = 1m
                }));

            Assert.Equal("A product for this payable bill was not found.", result.Value);
            Assert.DoesNotContain(productId.ToString(), result.Value?.ToString());
        }

        [Fact]
        public async Task UpdatePayableBill_WithNoId_IsRefused()
        {
            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().UpdatePayableBill(new UpsertPayableBillRequestDTO
                {
                    Description = "Aluguel",
                    DueDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
                    Amount = 1m
                }));

            Assert.Equal("Payable bill id is required.", result.Value);
        }

        /// <summary>
        /// The controller does not pre-check existence — the service is the one that knows, and it
        /// says so by throwing. A missing bill therefore answers 204, not 500.
        /// </summary>
        [Fact]
        public async Task UpdatePayableBill_ThatDoesNotExist_AnswersNoContent()
        {
            var id = Guid.NewGuid();
            _service.Setup(s => s.GetByIdAsync(id, _enterpriseId)).ReturnsAsync((PayableBill?)null);
            _service.Setup(s => s.UpdateAsync(It.IsAny<PayableBill>(), _enterpriseId))
                .ThrowsAsync(new NotFoundDatabaseException("no such bill"));

            var result = await Controller().UpdatePayableBill(new UpsertPayableBillRequestDTO
            {
                Id = id,
                Description = "Aluguel",
                DueDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
                Amount = 1m
            });

            Assert.IsType<NoContentResult>(result);
        }

        /// <summary>
        /// A caller without the payroll permission cannot turn somebody else's bill into a payroll
        /// one, and cannot edit a bill that already is one — the two halves of the same guard.
        /// </summary>
        [Fact]
        public async Task UpdatePayableBill_TurningABillIntoPayroll_IsForbidden()
        {
            var result = await Controller(canReadPayroll: false).UpdatePayableBill(new UpsertPayableBillRequestDTO
            {
                Id = Guid.NewGuid(),
                Description = "Folha",
                BillType = (int)EnumPayableBillType.Payroll,
                DueDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
                Amount = 1m
            });

            Assert.IsType<ForbidResult>(result);
            _service.Verify(s => s.UpdateAsync(It.IsAny<PayableBill>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task UpdatePayableBill_OnABillThatIsAlreadyPayroll_AnswersNoContent()
        {
            var bill = Bill(_enterpriseId, (int)EnumPayableBillType.Payroll);
            _service.Setup(s => s.GetByIdAsync(bill.Id, _enterpriseId)).ReturnsAsync(bill);

            var result = await Controller(canReadPayroll: false).UpdatePayableBill(new UpsertPayableBillRequestDTO
            {
                Id = bill.Id,
                Description = "Aluguel",
                BillType = (int)EnumPayableBillType.Accounts,
                DueDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
                Amount = 1m
            });

            Assert.IsType<NoContentResult>(result);
            _service.Verify(s => s.UpdateAsync(It.IsAny<PayableBill>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task MarkProductsReceived_OnAPayrollBillWithoutThePermission_AnswersNoContent()
        {
            var bill = Bill(_enterpriseId, (int)EnumPayableBillType.Payroll);
            _service.Setup(s => s.GetByIdAsync(bill.Id, _enterpriseId)).ReturnsAsync(bill);

            Assert.IsType<NoContentResult>(await Controller(canReadPayroll: false).MarkProductsReceived(bill.Id));
            _service.Verify(s => s.MarkProductsReceivedAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task MarkProductsReceived_OnAnOrdinaryBill_ReceivesIt()
        {
            var bill = Bill(_enterpriseId);
            _service.Setup(s => s.GetByIdAsync(bill.Id, _enterpriseId)).ReturnsAsync(bill);
            _service.Setup(s => s.MarkProductsReceivedAsync(bill.Id, _enterpriseId)).ReturnsAsync(bill);

            Assert.IsType<OkObjectResult>(await Controller().MarkProductsReceived(bill.Id));
        }

        [Fact]
        public async Task RefundPayableBill_WithAnOversizedReason_IsRefused()
        {
            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().RefundPayableBill(
                    Guid.NewGuid(), new RefundRequestDTO { Reason = new string('r', 501) }));

            Assert.Contains("characters or fewer", result.Value?.ToString());
        }

        [Fact]
        public async Task RefundPayableBill_OnAPayrollBillWithoutThePermission_AnswersNoContent()
        {
            var bill = Bill(_enterpriseId, (int)EnumPayableBillType.Payroll);
            _service.Setup(s => s.GetByIdAsync(bill.Id, _enterpriseId)).ReturnsAsync(bill);

            var result = await Controller(canReadPayroll: false)
                .RefundPayableBill(bill.Id, new RefundRequestDTO { Reason = "Devolvido" });

            Assert.IsType<NoContentResult>(result);
            _service.Verify(s => s.RefundAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RefundPayableBill_OnAnOrdinaryBill_RefundsIt()
        {
            var bill = Bill(_enterpriseId);
            _service.Setup(s => s.GetByIdAsync(bill.Id, _enterpriseId)).ReturnsAsync(bill);
            _service.Setup(s => s.RefundAsync(bill.Id, _enterpriseId, "Devolvido")).ReturnsAsync(bill);

            Assert.IsType<OkObjectResult>(
                await Controller().RefundPayableBill(bill.Id, new RefundRequestDTO { Reason = "Devolvido" }));
            _service.VerifyAll();
        }

        [Fact]
        public async Task DeletePayableBill_OnAPayrollBillWithoutThePermission_AnswersNoContent()
        {
            var bill = Bill(_enterpriseId, (int)EnumPayableBillType.Payroll);
            _service.Setup(s => s.GetByIdAsync(bill.Id, _enterpriseId)).ReturnsAsync(bill);

            Assert.IsType<NoContentResult>(await Controller(canReadPayroll: false).DeletePayableBill(bill.Id));
            _service.Verify(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task DeletePayableBill_OnAnOrdinaryBill_DeletesIt()
        {
            var bill = Bill(_enterpriseId);
            _service.Setup(s => s.GetByIdAsync(bill.Id, _enterpriseId)).ReturnsAsync(bill);
            _service.Setup(s => s.DeleteAsync(bill.Id, _enterpriseId)).ReturnsAsync(bill);

            Assert.IsType<OkObjectResult>(await Controller().DeletePayableBill(bill.Id));
        }

        [Fact]
        public async Task GetReplenishmentSuggestions_WithNoSuggestions_AnswersNoContent()
        {
            _service.Setup(s => s.GetReplenishmentSuggestionsAsync(_enterpriseId, It.IsAny<ReplenishmentSuggestionRequestDTO>()))
                .ReturnsAsync([]);

            Assert.IsType<NoContentResult>(
                await Controller().GetReplenishmentSuggestions(new ReplenishmentSuggestionRequestDTO()));
        }

        [Fact]
        public async Task GetReplenishmentSuggestions_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(
                await Anonymous().GetReplenishmentSuggestions(new ReplenishmentSuggestionRequestDTO()));
        }
    }
}
