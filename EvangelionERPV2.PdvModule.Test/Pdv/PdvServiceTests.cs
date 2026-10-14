using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.PdvModule.Application.Services;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EvangelionERPV2.PdvModule.Test
{
    /// <summary>
    /// Covers what the module exists to guarantee: a basket rung once is charged once, no matter
    /// how many times a terminal on a bad line resends it.
    /// </summary>
    public class PdvServiceTests : IDisposable
    {
        private readonly PdvTestContext _database = new();
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();

        public void Dispose() => _database.Dispose();

        /// <summary>
        /// Stands in for OrderService, which needs ten collaborators of its own. What matters here
        /// is that a real Order comes back with an id, and how many times it was asked for.
        /// </summary>
        private static Mock<IOrderService<Order>> OrderServiceStub(AppDbContext context)
        {
            var mock = new Mock<IOrderService<Order>>();

            mock.Setup(service => service.GetByExternalReferenceAsync(It.IsAny<Guid>(), It.IsAny<string>()))
                .ReturnsAsync((Guid enterpriseId, string externalReference) => context.Order
                    .AsNoTracking()
                    .FirstOrDefault(order => order.EnterpriseId == enterpriseId
                                             && order.ExternalReference == externalReference));

            mock.Setup(service => service.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .Returns<Order, bool, bool, string?>(async (order, _, _, _) =>
                {
                    order.Id = Guid.NewGuid();
                    order.TotalValue = (order.OrderedProduct ?? [])
                        .Sum(line => decimal.Round(line.Value * line.Quantity, 2, MidpointRounding.AwayFromZero));

                    var lines = (order.OrderedProduct ?? []).ToList();
                    order.OrderedProduct = null;

                    context.Order.Add(order);
                    await context.SaveChangesAsync();

                    order.OrderedProduct = lines;
                    return order;
                });

            return mock;
        }

        /// <summary>No test in this file exercises thumbnails; a stub that never returns one is enough.</summary>
        private static Mock<IProductService<Product>> ProductServiceStub()
        {
            var mock = new Mock<IProductService<Product>>();
            mock.Setup(service => service.GetThumbnailBase64Async(It.IsAny<string?>()))
                .ReturnsAsync((string.Empty, (string?)null));
            return mock;
        }

        /// <summary>
        /// Refuses to write the payments — the crash window between the order commit and the
        /// completion commit, which is the one a retry must not sell twice.
        /// </summary>
        private sealed class FailingPaymentRepository(AppDbContext context) : Repository<PdvSalePayment>(context)
        {
            public override Task<IEnumerable<PdvSalePayment>> CreateRangeAsync(IEnumerable<PdvSalePayment> entitys) =>
                throw new InsertDatabaseException("boom");
        }

        private PdvService CreateService(AppDbContext context, Mock<IOrderService<Order>>? orderService = null,
            IRepository<PdvSalePayment>? paymentRepository = null) =>
            new(context,
                new Repository<CashSession>(context),
                new Repository<CashMovement>(context),
                new Repository<PdvSale>(context),
                paymentRepository ?? new Repository<PdvSalePayment>(context),
                (orderService ?? OrderServiceStub(context)).Object,
                ProductServiceStub().Object,
                new Repository<PdvCharge>(context),
                PixProviderStubs.Object());

        private async Task<CashSession> OpenSessionAsync(PdvService service, Guid clientSessionId, decimal openingFloat = 200m)
        {
            return await service.OpenSessionAsync(new OpenCashSessionRequestDTO
            {
                ClientSessionId = clientSessionId,
                TerminalId = "CAIXA-01",
                OpeningFloat = openingFloat,
                OpenedAt = DateTime.UtcNow
            }, _enterpriseId, _userId);
        }

        private static PdvSaleSyncRequestDTO SaleFor(Guid clientSaleId, Guid clientSessionId,
            decimal unitPrice = 10m, decimal quantity = 1m, int method = (int)EnumPaymentMethod.Cash,
            string? authorizationCode = null)
        {
            var total = decimal.Round(unitPrice * quantity, 2, MidpointRounding.AwayFromZero);

            return new PdvSaleSyncRequestDTO
            {
                ClientSaleId = clientSaleId,
                ClientSessionId = clientSessionId,
                TerminalId = "CAIXA-01",
                SoldAtLocal = DateTime.UtcNow,
                Items = [new PdvSaleItemRequestDTO { ProductId = Guid.NewGuid(), Quantity = quantity, Value = unitPrice }],
                Payments = [new PdvSalePaymentRequestDTO { Method = method, Amount = total, AuthorizationCode = authorizationCode }]
            };
        }

        [Fact]
        public async Task ResendingTheSameSale_CreatesOneOrder()
        {
            var context = _database.CreateContext();
            var orderService = OrderServiceStub(context);
            var service = CreateService(context, orderService);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var request = SaleFor(Guid.NewGuid(), sessionId);

            var first = await service.SyncSaleAsync(request, _enterpriseId, _userId);
            var second = await service.SyncSaleAsync(request, _enterpriseId, _userId);
            var third = await service.SyncSaleAsync(request, _enterpriseId, _userId);

            orderService.Verify(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Once);

            Assert.Equal((int)EnumPdvSaleStatus.Synced, first.Status);
            Assert.False(first.WasAlreadyRecorded);
            Assert.True(second.WasAlreadyRecorded);
            Assert.True(third.WasAlreadyRecorded);
            Assert.Equal(first.OrderId, second.OrderId);
            Assert.Equal(first.OrderId, third.OrderId);
            Assert.Equal(1, await context.PdvSale.CountAsync());
        }

        [Fact]
        public async Task ARequestThatFindsTheSaleProcessing_DoesNotCreateAnotherOrder()
        {
            var context = _database.CreateContext();
            var orderService = OrderServiceStub(context);
            var service = CreateService(context, orderService);
            var sessionId = Guid.NewGuid();
            var session = await OpenSessionAsync(service, sessionId);
            var request = SaleFor(Guid.NewGuid(), sessionId);

            context.PdvSale.Add(new PdvSale
            {
                Id = Guid.NewGuid(),
                EnterpriseId = _enterpriseId,
                ClientSaleId = request.ClientSaleId,
                CashSessionId = session.Id,
                TerminalId = session.TerminalId,
                UserId = _userId,
                Status = (int)EnumPdvSaleStatus.Processing,
                SoldAtLocal = request.SoldAtLocal,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var result = await service.SyncSaleAsync(request, _enterpriseId, _userId);

            Assert.True(result.WasAlreadyRecorded);
            Assert.Equal((int)EnumPdvSaleStatus.Processing, result.Status);
            orderService.Verify(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task ASaleWhosePaymentsDoNotMatchTheTotal_IsRejectedAndNoOrderIsCreated()
        {
            var context = _database.CreateContext();
            var orderService = OrderServiceStub(context);
            var service = CreateService(context, orderService);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var request = SaleFor(Guid.NewGuid(), sessionId, unitPrice: 50m);
            request.Payments = [new PdvSalePaymentRequestDTO { Method = (int)EnumPaymentMethod.Cash, Amount = 40m }];

            var result = await service.SyncSaleAsync(request, _enterpriseId, _userId);

            Assert.Equal((int)EnumPdvSaleStatus.Rejected, result.Status);
            Assert.Null(result.OrderId);
            Assert.Contains("50", result.RejectReason);
            orderService.Verify(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task ARejectedSaleStaysRejectedOnResend()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var request = SaleFor(Guid.NewGuid(), sessionId, unitPrice: 50m);
            request.Payments = [new PdvSalePaymentRequestDTO { Method = (int)EnumPaymentMethod.Cash, Amount = 40m }];

            await service.SyncSaleAsync(request, _enterpriseId, _userId);
            var again = await service.SyncSaleAsync(request, _enterpriseId, _userId);

            Assert.Equal((int)EnumPdvSaleStatus.Rejected, again.Status);
            Assert.True(again.WasAlreadyRecorded);
        }

        [Fact]
        public async Task AClosedPeriodRejectsTheSaleRatherThanLeavingItToRetryForever()
        {
            var context = _database.CreateContext();
            var orderService = OrderServiceStub(context);

            orderService.Setup(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .ThrowsAsync(new PeriodClosedException("O período está fechado."));

            var service = CreateService(context, orderService);
            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var result = await service.SyncSaleAsync(SaleFor(Guid.NewGuid(), sessionId), _enterpriseId, _userId);

            Assert.Equal((int)EnumPdvSaleStatus.Rejected, result.Status);
            Assert.Equal("O período está fechado.", result.RejectReason);
        }

        [Fact]
        public async Task AnUnexpectedFailureLeavesTheClaimPending_SoTheRetryCanFinishIt()
        {
            var context = _database.CreateContext();
            var orderService = OrderServiceStub(context);

            orderService.Setup(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .ThrowsAsync(new InsertDatabaseException("boom"));

            var service = CreateService(context, orderService);
            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var request = SaleFor(Guid.NewGuid(), sessionId);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => service.SyncSaleAsync(request, _enterpriseId, _userId));

            var claim = await context.PdvSale.AsNoTracking()
                .SingleAsync(sale => sale.ClientSaleId == request.ClientSaleId);

            Assert.Equal((int)EnumPdvSaleStatus.Pending, claim.Status);
            Assert.Null(claim.OrderId);
        }

        [Fact]
        public async Task AFailureAfterTheOrderWasCreated_ResumesInsteadOfSellingTheBasketTwice()
        {
            var context = _database.CreateContext();
            var orderService = OrderServiceStub(context);

            var crashing = CreateService(context, orderService, new FailingPaymentRepository(context));

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(crashing, sessionId);

            var request = SaleFor(Guid.NewGuid(), sessionId);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => crashing.SyncSaleAsync(request, _enterpriseId, _userId));

            var interrupted = await context.PdvSale.AsNoTracking()
                .SingleAsync(sale => sale.ClientSaleId == request.ClientSaleId);

            Assert.Equal((int)EnumPdvSaleStatus.Pending, interrupted.Status);
            Assert.NotNull(interrupted.OrderId);

            var retryContext = _database.CreateContext();
            var service = CreateService(retryContext, orderService);

            var resumed = await service.SyncSaleAsync(request, _enterpriseId, _userId);

            Assert.Equal((int)EnumPdvSaleStatus.Synced, resumed.Status);
            Assert.Equal(interrupted.OrderId, resumed.OrderId);

            orderService.Verify(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Once);
            Assert.Equal(1, await retryContext.Order.CountAsync());
            Assert.Equal(1, await retryContext.PdvSalePayment.CountAsync(payment => payment.PdvSaleId == interrupted.Id));

            Assert.Equal(1, await retryContext.CashMovement
                .CountAsync(movement => movement.Type == (int)EnumCashMovementType.Sale));
        }

        [Fact]
        public async Task AnOrderCommitWhoseResponseWasLost_IsRecoveredByItsSaleReference()
        {
            var context = _database.CreateContext();
            var orderService = OrderServiceStub(context);
            var createCalls = 0;

            orderService.Setup(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .Returns<Order, bool, bool, string?>(async (order, _, _, _) =>
                {
                    createCalls++;
                    order.Id = Guid.NewGuid();
                    var lines = (order.OrderedProduct ?? []).ToList();
                    order.OrderedProduct = null;
                    context.Order.Add(order);
                    await context.SaveChangesAsync();
                    order.OrderedProduct = lines;
                    throw new TimeoutException("The committed response was lost.");
                });

            var service = CreateService(context, orderService);
            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);
            var request = SaleFor(Guid.NewGuid(), sessionId);

            var result = await service.SyncSaleAsync(request, _enterpriseId, _userId);

            Assert.Equal((int)EnumPdvSaleStatus.Synced, result.Status);
            Assert.Equal(1, createCalls);
            var order = Assert.Single(await context.Order.AsNoTracking().ToListAsync());
            Assert.Equal($"pdv:{request.ClientSaleId:N}", order.ExternalReference);
            Assert.Equal(order.Id, result.OrderId);
        }

        [Fact]
        public async Task AStaleProcessingClaim_CanBeResumed()
        {
            var context = _database.CreateContext();
            var orderService = OrderServiceStub(context);
            var service = CreateService(context, orderService);
            var sessionId = Guid.NewGuid();
            var session = await OpenSessionAsync(service, sessionId);
            var request = SaleFor(Guid.NewGuid(), sessionId);

            context.PdvSale.Add(new PdvSale
            {
                Id = Guid.NewGuid(),
                EnterpriseId = _enterpriseId,
                ClientSaleId = request.ClientSaleId,
                CashSessionId = session.Id,
                TerminalId = session.TerminalId,
                UserId = _userId,
                Status = (int)EnumPdvSaleStatus.Processing,
                SoldAtLocal = request.SoldAtLocal,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-10),
                IsActive = true
            });
            await context.SaveChangesAsync();

            var result = await service.SyncSaleAsync(request, _enterpriseId, _userId);

            Assert.Equal((int)EnumPdvSaleStatus.Synced, result.Status);
            orderService.Verify(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Once);
        }

        [Fact]
        public async Task OnlyTheCashLegOfASaleReachesTheDrawer()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            var session = await OpenSessionAsync(service, sessionId, openingFloat: 200m);

            var request = SaleFor(Guid.NewGuid(), sessionId, unitPrice: 100m);
            request.Payments =
            [
                new PdvSalePaymentRequestDTO { Method = (int)EnumPaymentMethod.Credit, Amount = 60m, AuthorizationCode = "004417" },
                new PdvSalePaymentRequestDTO { Method = (int)EnumPaymentMethod.Cash, Amount = 40m, ChangeGiven = 10m }
            ];

            await service.SyncSaleAsync(request, _enterpriseId, _userId);

            var summary = await service.GetSessionSummaryAsync(sessionId, _enterpriseId, includeExpectedCash: true);

            Assert.Equal(240m, summary.ExpectedCash);
            Assert.Equal(2, summary.Tenders.Count());
        }

        [Fact]
        public async Task OpeningTheSameSessionTwice_ReturnsTheFirst()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var clientSessionId = Guid.NewGuid();
            var first = await OpenSessionAsync(service, clientSessionId, openingFloat: 200m);
            var second = await OpenSessionAsync(service, clientSessionId, openingFloat: 999m);

            Assert.Equal(first.Id, second.Id);
            Assert.Equal(200m, second.OpeningFloat);
            Assert.Equal(1, await context.CashSession.CountAsync());
        }

        [Fact]
        public async Task ASecondShiftOnTheSameTerminal_IsRefusedInAWayTheTillKeepsRetrying()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            await OpenSessionAsync(service, Guid.NewGuid());

            var refusal = await Assert.ThrowsAsync<InsertDatabaseException>(
                () => service.OpenSessionAsync(new OpenCashSessionRequestDTO
                {
                    ClientSessionId = Guid.NewGuid(),
                    TerminalId = "CAIXA-01",
                    OpeningFloat = 100m,
                    OpenedAt = DateTime.UtcNow
                }, _enterpriseId, _userId));

            Assert.IsNotType<ArgumentException>(refusal);
            Assert.Contains("already has an open cash session", refusal.Message);
            Assert.Equal(1, await context.CashSession.CountAsync());
        }

        [Fact]
        public async Task ClosingTwice_ReturnsTheSameReadingRatherThanRefusingIt()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId, openingFloat: 200m);

            var close = new CloseCashSessionRequestDTO
            {
                ClientSessionId = sessionId,
                CountedAmount = 195m,
                ClosedAt = DateTime.UtcNow
            };

            var first = await service.CloseSessionAsync(close, _enterpriseId, _userId);

            var second = await service.CloseSessionAsync(
                new CloseCashSessionRequestDTO { ClientSessionId = sessionId, CountedAmount = 999m },
                _enterpriseId, _userId);

            Assert.Equal(first.Session.CountedAmount, second.Session.CountedAmount);
            Assert.Equal(first.Session.Difference, second.Session.Difference);
            Assert.Equal(first.Session.ClosedAt, second.Session.ClosedAt);
            Assert.Equal(1, await context.CashMovement
                .CountAsync(movement => movement.Type == (int)EnumCashMovementType.Closing));
        }

        [Fact]
        public async Task ClosingReportsTheDifferenceAndStoresIt()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId, openingFloat: 200m);
            await service.SyncSaleAsync(SaleFor(Guid.NewGuid(), sessionId, unitPrice: 100m), _enterpriseId, _userId);

            var summary = await service.CloseSessionAsync(new CloseCashSessionRequestDTO
            {
                ClientSessionId = sessionId,
                CountedAmount = 295m,
                ClosedAt = DateTime.UtcNow
            }, _enterpriseId, _userId);

            Assert.Equal(300m, summary.Session.ExpectedAmount);
            Assert.Equal(295m, summary.Session.CountedAmount);
            Assert.Equal(-5m, summary.Session.Difference);
            Assert.Equal((int)EnumCashSessionStatus.Closed, summary.Session.Status);
        }

        [Fact]
        public async Task ASangriaNeedsAReason_AndOnlyTheTwoManualTypesAreAccepted()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterMovementAsync(new CashMovementRequestDTO
            {
                ClientMovementId = Guid.NewGuid(),
                ClientSessionId = sessionId,
                Type = (int)EnumCashMovementType.Sangria,
                Amount = 50m,
                Reason = "   "
            }, _enterpriseId, _userId));

            await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterMovementAsync(new CashMovementRequestDTO
            {
                ClientMovementId = Guid.NewGuid(),
                ClientSessionId = sessionId,
                Type = (int)EnumCashMovementType.Sale,
                Amount = 50m,
                Reason = "nope"
            }, _enterpriseId, _userId));
        }

        [Fact]
        public async Task ASangriaResentTwice_OnlyLeavesTheDrawerOnce()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId, openingFloat: 200m);

            var movement = new CashMovementRequestDTO
            {
                ClientMovementId = Guid.NewGuid(),
                ClientSessionId = sessionId,
                Type = (int)EnumCashMovementType.Sangria,
                Amount = 150m,
                Reason = "Cofre",
                OccurredAt = DateTime.UtcNow
            };

            await service.RegisterMovementAsync(movement, _enterpriseId, _userId);
            await service.RegisterMovementAsync(movement, _enterpriseId, _userId);

            var summary = await service.GetSessionSummaryAsync(sessionId, _enterpriseId, includeExpectedCash: true);
            Assert.Equal(50m, summary.ExpectedCash);
        }

        [Fact]
        public async Task AnotherTenantsSessionIsInvisible()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            await Assert.ThrowsAsync<NotFoundDatabaseException>(
                () => service.GetSessionSummaryAsync(sessionId, Guid.NewGuid(), includeExpectedCash: true));
        }

        [Fact]
        public async Task ACardPaymentWithNoAuthorizationCodeIsRejected()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var result = await service.SyncSaleAsync(
                SaleFor(Guid.NewGuid(), sessionId, unitPrice: 60m, method: (int)EnumPaymentMethod.Credit),
                _enterpriseId, _userId);

            Assert.Equal((int)EnumPdvSaleStatus.Rejected, result.Status);
            Assert.Contains("autoriza", result.RejectReason!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task AWhitespaceAuthorizationCodeIsNoCodeAtAll()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var result = await service.SyncSaleAsync(
                SaleFor(Guid.NewGuid(), sessionId, unitPrice: 60m, method: (int)EnumPaymentMethod.Debit,
                    authorizationCode: "   "),
                _enterpriseId, _userId);

            Assert.Equal((int)EnumPdvSaleStatus.Rejected, result.Status);
        }

        [Fact]
        public async Task ACardPaymentWithACodeIsRecorded()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var result = await service.SyncSaleAsync(
                SaleFor(Guid.NewGuid(), sessionId, unitPrice: 60m, method: (int)EnumPaymentMethod.Credit,
                    authorizationCode: "004417"),
                _enterpriseId, _userId);

            Assert.Equal((int)EnumPdvSaleStatus.Synced, result.Status);
        }

        [Fact]
        public async Task ACashSaleStillNeedsNothing()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var result = await service.SyncSaleAsync(
                SaleFor(Guid.NewGuid(), sessionId, unitPrice: 60m, method: (int)EnumPaymentMethod.Cash),
                _enterpriseId, _userId);

            Assert.Equal((int)EnumPdvSaleStatus.Synced, result.Status);
        }

        [Fact]
        public async Task AManualPixTenderIsRecordedAsUnproven()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            await service.SyncSaleAsync(
                SaleFor(Guid.NewGuid(), sessionId, unitPrice: 60m, method: (int)EnumPaymentMethod.Pix),
                _enterpriseId, _userId);

            var payment = await context.PdvSalePayment.SingleAsync();
            Assert.False(payment.IsVerified);
            Assert.Null(payment.PdvChargeId);
        }

        [Fact]
        public async Task AConfirmedPixTenderKeepsItsChargeAndItsProof()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var clientSaleId = Guid.NewGuid();
            var chargeId = await GivePaidChargeAsync(context, clientSaleId: clientSaleId);
            var request = SaleFor(clientSaleId, sessionId, unitPrice: 60m, method: (int)EnumPaymentMethod.Pix);
            var pix = request.Payments!.Single();
            pix.ChargeId = chargeId;
            pix.IsVerified = true;

            await service.SyncSaleAsync(request, _enterpriseId, _userId);

            var payment = await context.PdvSalePayment.SingleAsync();
            Assert.True(payment.IsVerified);
            Assert.Equal(chargeId, payment.PdvChargeId);
        }

        [Fact]
        public async Task ATenderNamingAPaidChargeFromAnotherBasket_IsNotProof()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var otherBasket = await GivePaidChargeAsync(context, clientSaleId: Guid.NewGuid());
            var request = SaleFor(Guid.NewGuid(), sessionId, unitPrice: 60m, method: (int)EnumPaymentMethod.Pix);
            var pix = request.Payments!.Single();
            pix.ChargeId = otherBasket;
            pix.IsVerified = true;

            await service.SyncSaleAsync(request, _enterpriseId, _userId);

            var payment = await context.PdvSalePayment.SingleAsync();
            Assert.False(payment.IsVerified);

            Assert.Equal(otherBasket, payment.PdvChargeId);
            Assert.Equal((int)EnumPdvSaleStatus.Synced, (await context.PdvSale.SingleAsync()).Status);
        }

        [Fact]
        public async Task ATenderClaimingMoreThanTheChargeSettled_IsNotProof()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var clientSaleId = Guid.NewGuid();
            var smallCharge = await GivePaidChargeAsync(context, clientSaleId: clientSaleId, amount: 1m);
            var request = SaleFor(clientSaleId, sessionId, unitPrice: 60m, method: (int)EnumPaymentMethod.Pix);
            var pix = request.Payments!.Single();
            pix.ChargeId = smallCharge;
            pix.IsVerified = true;

            await service.SyncSaleAsync(request, _enterpriseId, _userId);

            Assert.False((await context.PdvSalePayment.SingleAsync()).IsVerified);
        }

        [Fact]
        public async Task ATenderNamingAChargeThatNeverSettled_IsNotProof()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var chargeId = await GivePaidChargeAsync(context, status: EnumPdvChargeStatus.Pending);
            var request = SaleFor(Guid.NewGuid(), sessionId, unitPrice: 60m, method: (int)EnumPaymentMethod.Pix);
            var pix = request.Payments!.Single();
            pix.ChargeId = chargeId;
            pix.IsVerified = true;

            await service.SyncSaleAsync(request, _enterpriseId, _userId);

            Assert.False((await context.PdvSalePayment.SingleAsync()).IsVerified);
        }

        [Fact]
        public async Task ATenderNamingAnotherTenantsPaidCharge_IsNotProof()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var chargeId = await GivePaidChargeAsync(context, enterpriseId: Guid.NewGuid());
            var request = SaleFor(Guid.NewGuid(), sessionId, unitPrice: 60m, method: (int)EnumPaymentMethod.Pix);
            var pix = request.Payments!.Single();
            pix.ChargeId = chargeId;
            pix.IsVerified = true;

            await service.SyncSaleAsync(request, _enterpriseId, _userId);

            Assert.False((await context.PdvSalePayment.SingleAsync()).IsVerified);
        }

        /// <summary>A charge row the proof check can find, in whatever state the test needs.</summary>
        private async Task<Guid> GivePaidChargeAsync(AppDbContext context,
            EnumPdvChargeStatus status = EnumPdvChargeStatus.Paid, Guid? enterpriseId = null,
            Guid? clientSaleId = null, decimal amount = 60m)
        {
            var charge = new PdvCharge
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId ?? _enterpriseId,

                ClientSaleId = clientSaleId ?? Guid.NewGuid(),
                Provider = "stripe",
                ProviderIntentId = $"pi_{Guid.NewGuid():N}",
                Amount = amount,
                Status = (int)status,
                ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            };

            context.PdvCharge.Add(charge);
            await context.SaveChangesAsync();

            return charge.Id;
        }

        [Fact]
        public async Task ATenderThatClaimsProofWithoutNamingACharge_IsStillTheOperatorsWord()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var request = SaleFor(Guid.NewGuid(), sessionId, unitPrice: 60m, method: (int)EnumPaymentMethod.Pix);
            request.Payments!.Single().IsVerified = true;

            await service.SyncSaleAsync(request, _enterpriseId, _userId);

            var payment = await context.PdvSalePayment.SingleAsync();
            Assert.False(payment.IsVerified);
        }

        [Fact]
        public async Task TheCloseCanSeparateProvenPixFromClaimedPix()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            var sessionId = Guid.NewGuid();
            await OpenSessionAsync(service, sessionId);

            var provenSaleId = Guid.NewGuid();
            var proven = SaleFor(provenSaleId, sessionId, unitPrice: 60m, method: (int)EnumPaymentMethod.Pix);
            var provenPix = proven.Payments!.Single();
            provenPix.ChargeId = await GivePaidChargeAsync(context, clientSaleId: provenSaleId);
            provenPix.IsVerified = true;

            await service.SyncSaleAsync(proven, _enterpriseId, _userId);
            await service.SyncSaleAsync(
                SaleFor(Guid.NewGuid(), sessionId, unitPrice: 40m, method: (int)EnumPaymentMethod.Pix),
                _enterpriseId, _userId);

            var byProof = await context.PdvSalePayment.GroupBy(payment => payment.IsVerified)
                .Select(group => new { group.Key, Total = group.Sum(payment => payment.Amount) })
                .ToListAsync();

            Assert.Equal(60m, byProof.Single(x => x.Key).Total);
            Assert.Equal(40m, byProof.Single(x => !x.Key).Total);
        }

        [Fact]
        public async Task ASaleWithNoEnterpriseIsRefused()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => service.SyncSaleAsync(SaleFor(Guid.NewGuid(), Guid.NewGuid()), Guid.Empty, _userId));
        }

        [Fact]
        public async Task ASaleAgainstAnUnknownSessionIsRefused()
        {
            var context = _database.CreateContext();
            var service = CreateService(context);

            await Assert.ThrowsAsync<NotFoundDatabaseException>(
                () => service.SyncSaleAsync(SaleFor(Guid.NewGuid(), Guid.NewGuid()), _enterpriseId, _userId));
        }
    }
}
