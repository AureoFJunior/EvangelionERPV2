using System.Text;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.StorefrontModule.Application.DTOs;
using EvangelionERPV2.StorefrontModule.Application.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EvangelionERPV2.StorefrontModule.Test
{
    /// <summary>SPEC-ecommerce-storefront.md §14: the ERP prices, stores and settles Limen checkouts.</summary>
    public class StorefrontCheckoutTests : IDisposable
    {
        private readonly StorefrontTestContext _database = new();
        private readonly TestClock _clock = new(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc));

        public void Dispose() => _database.Dispose();

        private sealed class TestClock(DateTime now) : TimeProvider
        {
            public DateTime Now { get; set; } = now;
            public override DateTimeOffset GetUtcNow() => new(Now);
        }

        private StorefrontCheckoutService Service(bool simulate = true) => _database.CreateCheckoutService(_clock, simulate);

        private static StorefrontCustomerDTO Ana(bool optIn = false) => new("Ana@Example.com", "Ana Souza", "52998224725", "11987654321", optIn);

        private static readonly StorefrontAddressDTO Paulista = new("01310100", "Avenida Paulista", "1000", "Ap 5", "Bela Vista", "São Paulo", "SP");

        private static StorefrontCheckoutRequestDTO Request(decimal expected, StorefrontPaymentMethod method, Guid? shopper, params (Guid Id, int Qty)[] lines) =>
            new(lines.Select(l => new StorefrontCartLineDTO(l.Id, l.Qty)).ToList(), Ana(optIn: true), Paulista, method, expected, shopper);

        /// <summary>Two listed shops: Aurora (shipping 20, delivery 2+3..2+7) with a mug, Traço (shipping 20) with a pen.</summary>
        private async Task<(Enterprise Aurora, Enterprise Traco, Product Mug, Product Pen)> TwoShopsAsync(decimal mugStock = 5, decimal penStock = 5)
        {
            var aurora = Build.ListedShop("aurora", "Cerâmica Aurora");
            var traco = Build.ListedShop("traco", "Papelaria Traço");
            var mug = Build.Item(aurora, "Caneca", 79m, stock: mugStock, label: "Azul", group: Guid.NewGuid());
            var pen = Build.Item(traco, "Lapiseira", 58m, stock: penStock);
            await _database.SeedAsync(aurora, traco, mug, pen);
            return (aurora, traco, mug, pen);
        }

        private async Task<StorefrontCheckoutDTO> PaidAsync(StorefrontCheckoutService service, StorefrontCheckoutRequestDTO request,
            StorefrontSimulatedOutcome outcome = StorefrontSimulatedOutcome.Approve)
        {
            var created = await service.CreateAsync(request);
            return (await service.SimulateAsync(created.Checkout!.Id, outcome))!;
        }

        #region R2 quote

        [Fact]
        public async Task Quote_PricesPerStore_AndFlagsGoneOutOfStockAndReducedLines()
        {
            var (aurora, _, mug, pen) = await TwoShopsAsync(mugStock: 3);
            var empty = Build.Item(aurora, "Prato", 40m, stock: 0);
            var unpublished = Build.Item(aurora, "Tigela", 30m, published: false);
            await _database.SeedAsync(empty, unpublished);

            var quote = await Service().QuoteAsync(
            [
                new(mug.Id, 5), new(pen.Id, 2), new(empty.Id, 1), new(unpublished.Id, 1), new(Guid.NewGuid(), 1)
            ]);

            var auroraStore = quote.Stores.Single(s => s.Slug == "aurora");
            var mugLine = auroraStore.Lines.Single(l => l.ProductId == mug.Id);
            Assert.Equal(("Caneca — Azul", 3, 237m, "quantity-reduced"), (mugLine.Name, mugLine.Quantity, mugLine.LineTotal, mugLine.Issue));
            Assert.Equal("out-of-stock", auroraStore.Lines.Single(l => l.ProductId == empty.Id).Issue);
            Assert.Equal((237m, 20m, 5, 9), (auroraStore.Subtotal, auroraStore.Shipping, auroraStore.DeliveryDaysMin, auroraStore.DeliveryDaysMax));

            var tracoStore = quote.Stores.Single(s => s.Slug == "traco");
            Assert.Equal((116m, 20m), (tracoStore.Subtotal, tracoStore.Shipping));

            var gone = quote.Stores.Single(s => s.Slug == "");
            Assert.Equal(2, gone.Lines.Count(l => l.Issue == "gone"));
            Assert.Equal((353m, 40m, 393m), (quote.Subtotal, quote.Shipping, quote.Total));
        }

        [Fact]
        public async Task Quote_ChargesNoShippingForAStoreWhoseLinesAreAllUnavailable()
        {
            var (_, _, mug, _) = await TwoShopsAsync(mugStock: 0);

            var quote = await Service().QuoteAsync([new(mug.Id, 1)]);

            Assert.Equal(0m, quote.Stores.Single().Shipping);
            Assert.Equal(0m, quote.Total);
        }

        #endregion

        #region R3 R4 create

        [Fact]
        public async Task Create_StoresTheCheckoutWithTheErpQuote_AndAPixCodeValidFor30Minutes()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var shopper = Guid.NewGuid();

            var result = await Service().CreateAsync(Request(99m, StorefrontPaymentMethod.Pix, shopper, (mug.Id, 1)));

            var checkout = result.Checkout!;
            Assert.Null(result.ChangedQuote);
            Assert.Equal((StorefrontCheckoutStatus.AwaitingPayment, 99m, "ana@example.com", true), (checkout.Status, checkout.Total, checkout.Email, checkout.Simulated));
            Assert.Equal(_clock.Now.AddMinutes(30), checkout.Pix!.ExpiresAt);
            Assert.Contains("LIMEN-TESTE", checkout.Pix.CopyPasteCode);

            using var context = _database.CreateContext();
            var row = await context.StorefrontCheckout.SingleAsync();
            Assert.Equal(shopper, row.ShopperId);
            Assert.Contains("\"MarketingOptIn\":true", row.CustomerJson);
        }

        [Fact]
        public async Task Create_RefusesAMovedTotal_AndALineWithAnIssue_WithoutStoringAnything()
        {
            var (_, _, mug, _) = await TwoShopsAsync(mugStock: 1);
            var service = Service();

            var moved = await service.CreateAsync(Request(90m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));
            Assert.Null(moved.Checkout);
            Assert.Equal(99m, moved.ChangedQuote!.Total);

            var reduced = await service.CreateAsync(Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 2)));
            Assert.Null(reduced.Checkout);
            Assert.Equal("quantity-reduced", reduced.ChangedQuote!.Stores.Single().Lines.Single().Issue);

            using var context = _database.CreateContext();
            Assert.Equal(0, await context.StorefrontCheckout.CountAsync());
        }

        [Fact]
        public async Task WithoutPaymentSimulation_NothingCanBeBoughtOrSettled()
        {
            var (_, _, mug, _) = await TwoShopsAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Service(simulate: false).CreateAsync(Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1))));

            var created = await Service().CreateAsync(Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));
            Assert.Null(await Service(simulate: false).SimulateAsync(created.Checkout!.Id, StorefrontSimulatedOutcome.Approve));
            _database.Orders.Verify(o => o.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()), Moq.Times.Never);
        }

        #endregion

        #region R5 R6 settle

        [Fact]
        public async Task Settling_CreatesOnePaidStorefrontOrderPerStore_WithShippingAddressAndCustomer()
        {
            var (aurora, traco, mug, pen) = await TwoShopsAsync();

            var paid = await PaidAsync(Service(), Request(235m, StorefrontPaymentMethod.Pix, null, (mug.Id, 1), (pen.Id, 2)), StorefrontSimulatedOutcome.PixPaid);

            Assert.Equal(StorefrontCheckoutStatus.Paid, paid.Status);
            Assert.All(paid.Outcomes, o => Assert.Equal(StorefrontOutcomeKind.Ordered, o.Outcome));
            Assert.All(paid.Outcomes, o => Assert.Matches("^LM-[0-9A-F]{8}$", o.OrderNumber!));

            using var context = _database.CreateContext();
            var orders = await context.Order.ToListAsync();
            Assert.Equal(2, orders.Count);
            var auroraOrder = orders.Single(o => o.EnterpriseId == aurora.Id);
            Assert.Equal((int)EnumOrderStatus.Paid, auroraOrder.Status);
            Assert.Equal((int)EnumOrderChannel.Storefront, auroraOrder.Channel);
            Assert.Equal(paid.Id.ToString(), auroraOrder.ExternalReference);
            Assert.Equal((20m, 99m), (auroraOrder.ShippingValue, auroraOrder.TotalValue));
            Assert.Equal((int)EnumPaymentMethod.Pix, auroraOrder.PaymentMethod);
            Assert.Contains("Avenida Paulista", auroraOrder.DeliveryAddress);
            Assert.Equal(136m, orders.Single(o => o.EnterpriseId == traco.Id).TotalValue);

            var line = await context.OrderedProduct.SingleAsync(l => l.OrderId == auroraOrder.Id);
            Assert.Equal((mug.Id, 1m, 79m), (line.ProductId, line.Quantity, line.Value));
            Assert.Equal(4m, (await context.Product.SingleAsync(p => p.Id == mug.Id)).StorageQuantity);

            var customer = await context.Customer.SingleAsync(c => c.Id == auroraOrder.CustomerId);
            Assert.Equal((aurora.Id, "Ana Souza", "ana@example.com", "52998224725", "11987654321"),
                (customer.EnterpriseId!.Value, customer.Name, customer.Email, customer.Document, customer.PhoneNumber));
            Assert.Equal(("Avenida Paulista", "1000", "Bela Vista", "São Paulo", "SP", "01310100"),
                (customer.Logradouro, customer.Numero, customer.Bairro, customer.Municipio, customer.Uf, customer.Cep));
            Assert.Equal(2, await context.Customer.CountAsync());
        }

        [Fact]
        public async Task ACardCheckout_BecomesACreditOrder_ForTheShoppersOwnCustomer()
        {
            var (aurora, _, mug, _) = await TwoShopsAsync();
            var shopper = Guid.NewGuid();
            var known = new Customer { Id = Guid.NewGuid(), EnterpriseId = aurora.Id, Name = "Ana", Email = "outro@example.com", ExternalShopperId = shopper.ToString(), IsActive = true };
            await _database.SeedAsync(known);

            await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, shopper, (mug.Id, 1)));

            using var context = _database.CreateContext();
            var order = await context.Order.SingleAsync();
            Assert.Equal((int)EnumPaymentMethod.Credit, order.PaymentMethod);
            Assert.Equal(known.Id, order.CustomerId);
            Assert.Equal(1, await context.Customer.CountAsync());
        }

        [Fact]
        public async Task AGuest_IsMatchedToTheStoresCustomerByEmailAndCpf()
        {
            var (aurora, _, mug, _) = await TwoShopsAsync();
            var known = new Customer { Id = Guid.NewGuid(), EnterpriseId = aurora.Id, Name = "Ana", Email = "ANA@example.com", Document = "52998224725", IsActive = true };
            await _database.SeedAsync(known);

            await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));

            using var context = _database.CreateContext();
            Assert.Equal(known.Id, (await context.Order.SingleAsync()).CustomerId);
        }

        [Fact]
        public async Task AStoreWhoseStockRanOut_GetsNoOrder_AndItsShareIsRefunded()
        {
            var (aurora, traco, mug, pen) = await TwoShopsAsync();
            var created = await Service().CreateAsync(Request(235m, StorefrontPaymentMethod.Card, null, (mug.Id, 1), (pen.Id, 2)));
            using (var context = _database.CreateContext())
            {
                (await context.Product.SingleAsync(p => p.Id == pen.Id)).StorageQuantity = 1;
                await context.SaveChangesAsync();
            }

            var paid = (await Service().SimulateAsync(created.Checkout!.Id, StorefrontSimulatedOutcome.Approve))!;

            var refund = paid.Outcomes.Single(o => o.Slug == "traco");
            Assert.Equal((StorefrontOutcomeKind.RefundedOutOfStock, null, 136m), (refund.Outcome, refund.OrderNumber, refund.Amount));
            using var check = _database.CreateContext();
            Assert.Equal([aurora.Id], await check.Order.Select(o => o.EnterpriseId!.Value).ToListAsync());
            Assert.Equal(1m, (await check.Product.SingleAsync(p => p.Id == pen.Id)).StorageQuantity);
            Assert.DoesNotContain(await check.Order.ToListAsync(), o => o.EnterpriseId == traco.Id);
        }

        [Fact]
        public async Task TheOutOfStockSimulation_RefundsTheLastStore()
        {
            var (_, _, mug, pen) = await TwoShopsAsync();

            var paid = await PaidAsync(Service(), Request(235m, StorefrontPaymentMethod.Card, null, (mug.Id, 1), (pen.Id, 2)), StorefrontSimulatedOutcome.OutOfStock);

            Assert.Equal(StorefrontOutcomeKind.Ordered, paid.Outcomes[0].Outcome);
            Assert.Equal(StorefrontOutcomeKind.RefundedOutOfStock, paid.Outcomes[1].Outcome);
            using var context = _database.CreateContext();
            Assert.Equal(1, await context.Order.CountAsync());
        }

        [Fact]
        public async Task SettlingTwice_CreatesNothingNew()
        {
            var (_, _, mug, pen) = await TwoShopsAsync();
            var paid = await PaidAsync(Service(), Request(235m, StorefrontPaymentMethod.Card, null, (mug.Id, 1), (pen.Id, 2)));

            var again = (await Service().SimulateAsync(paid.Id, StorefrontSimulatedOutcome.Approve))!;

            Assert.Equal(paid.Outcomes.Select(o => o.OrderNumber), again.Outcomes.Select(o => o.OrderNumber));
            using var context = _database.CreateContext();
            Assert.Equal(2, await context.Order.CountAsync());
        }

        [Fact]
        public async Task AnInterruptedSettlement_ResumesWithTheOrderAlreadyCreated()
        {
            var (aurora, _, mug, _) = await TwoShopsAsync();
            var created = await Service().CreateAsync(Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));
            var earlier = new Order { Id = Guid.NewGuid(), EnterpriseId = aurora.Id, ExternalReference = created.Checkout!.Id.ToString(), Channel = 2, Status = 2, IsActive = true };
            await _database.SeedAsync(earlier);

            var paid = (await Service().SimulateAsync(created.Checkout.Id, StorefrontSimulatedOutcome.Approve))!;

            Assert.Equal(StorefrontCheckoutService.OrderNumber(earlier.Id), paid.Outcomes.Single().OrderNumber);
            using var context = _database.CreateContext();
            Assert.Equal(1, await context.Order.CountAsync());
        }

        [Fact]
        public async Task AFailureThatIsNotStock_LeavesTheCheckoutSettling_AndTheNextReadCreatesTheOrder()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            _database.FailCreates = 1;

            var settling = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));

            Assert.Equal(StorefrontCheckoutStatus.Settling, settling.Status);
            Assert.Empty(settling.Outcomes);
            using (var context = _database.CreateContext())
                Assert.Equal(0, await context.Order.CountAsync());

            Assert.Equal(StorefrontCheckoutStatus.Settling, (await Service().GetAsync(settling.Id))!.Status);
            _clock.Now = _clock.Now.AddMinutes(1);
            var retried = (await Service().GetAsync(settling.Id))!;

            Assert.Equal(StorefrontCheckoutStatus.Paid, retried.Status);
            Assert.Equal(StorefrontOutcomeKind.Ordered, retried.Outcomes.Single().Outcome);
            using var check = _database.CreateContext();
            Assert.Equal(1, await check.Order.CountAsync());
        }

        [Fact]
        public async Task ASettlingPurchase_ShowsTheOrdersAlreadyCreated_InMyOrdersAndTheGuestPage()
        {
            var (_, _, mug, pen) = await TwoShopsAsync();
            var shopper = Guid.NewGuid();
            var created = await Service().CreateAsync(Request(235m, StorefrontPaymentMethod.Card, shopper, (mug.Id, 1), (pen.Id, 2)));
            // The second store's order fails once: the first store's order exists, the checkout is settling.
            _database.FailOnCall = 2;

            var settling = (await Service().SimulateAsync(created.Checkout!.Id, StorefrontSimulatedOutcome.Approve))!;

            Assert.Equal(StorefrontCheckoutStatus.Settling, settling.Status);
            var purchase = (await Service().GetPurchaseAsync(settling.Id))!;
            Assert.Single(purchase.Orders);
            Assert.Equal([settling.Id], (await Service().ListPurchasesAsync(shopper)).Select(p => p.CheckoutId));
        }

        [Fact]
        public async Task ThePaidDateTheShopperSees_IsTheDayOfThePayment_EvenAfterTheStoreShips()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var paid = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));
            using (var context = _database.CreateContext())
            {
                var order = await context.Order.SingleAsync();
                order.Payday = _clock.Now.AddDays(3);
                await context.SaveChangesAsync();
            }

            Assert.Equal(_clock.Now, (await Service().GetPurchaseAsync(paid.Id))!.Orders.Single().PaidAt);
        }

        [Fact]
        public async Task AFailureAfterTheStockWasSoldElsewhere_RefundsThatStore()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            _database.FailCreates = 1;
            _database.DuringFailure = context => context.Product.Single(p => p.Id == mug.Id).StorageQuantity = 0;

            var paid = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));

            Assert.Equal(StorefrontCheckoutStatus.Paid, paid.Status);
            Assert.Equal(StorefrontOutcomeKind.RefundedOutOfStock, paid.Outcomes.Single().Outcome);
        }

        [Fact]
        public async Task AStoreStillFailingAfterThreeAttempts_RaisesOneCriticalNotificationForThePlatform_AndStopsRetryingOnRead()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var author = new User
            {
                Id = Guid.NewGuid(), FirstName = "Limen", LastName = "Storefront", UserName = "limen", Password = "x", Email = "s@limen.local",
                ProfilePicture = "", PhoneNumber = "", IsServiceAccount = true, EnterpriseId = StorefrontTestContext.PlatformEnterpriseId, IsActive = true
            };
            await _database.SeedAsync(author);
            _database.FailCreates = 10;

            var checkout = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));
            await Service().GetAsync(checkout.Id);
            _clock.Now = _clock.Now.AddMinutes(1);
            await Service().GetAsync(checkout.Id);
            _clock.Now = _clock.Now.AddMinutes(2);
            await Service().GetAsync(checkout.Id);
            _clock.Now = _clock.Now.AddMinutes(10);
            var afterLimit = (await Service().GetAsync(checkout.Id))!;

            Assert.Equal(StorefrontCheckoutStatus.Settling, afterLimit.Status);
            Assert.Equal(7, _database.FailCreates);
            using var context = _database.CreateContext();
            var notification = await context.Notification.SingleAsync();
            Assert.Equal((StorefrontTestContext.PlatformEnterpriseId, EnumNotificationSeverity.Critical, author.Id),
                (notification.EnterpriseId, notification.Severity, notification.CreatedByUserId));
            Assert.Contains("Cerâmica Aurora", notification.Body);
        }

        [Fact]
        public async Task AStoresOwnEditsToItsCustomer_SurviveTheNextPurchase_AndBlanksAreFilled()
        {
            var (aurora, _, mug, _) = await TwoShopsAsync();
            var shopper = Guid.NewGuid();
            var edited = new Customer
            {
                Id = Guid.NewGuid(), EnterpriseId = aurora.Id, Name = "Ana S. (cliente fiel)", Email = "ana@example.com", PhoneNumber = "",
                ExternalShopperId = shopper.ToString(), Logradouro = "Rua do Ateliê", Numero = "7", Cep = "12260000", Municipio = "Cunha", Uf = "SP",
                Adress = "Rua do Ateliê, 7", IsActive = true
            };
            await _database.SeedAsync(edited);

            await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, shopper, (mug.Id, 1)));

            using var context = _database.CreateContext();
            var customer = await context.Customer.SingleAsync();
            Assert.Equal(("Ana S. (cliente fiel)", "Rua do Ateliê", "Cunha", "Rua do Ateliê, 7"), (customer.Name, customer.Logradouro, customer.Municipio, customer.Adress));
            Assert.Equal(("11987654321", "52998224725"), (customer.PhoneNumber, customer.Document));
            Assert.Contains("Avenida Paulista", (await context.Order.SingleAsync()).DeliveryAddress);
        }

        [Fact]
        public async Task AStorefrontOrder_IsDueWhenItWasPaid()
        {
            var (_, _, mug, _) = await TwoShopsAsync();

            await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));

            using var context = _database.CreateContext();
            var order = await context.Order.SingleAsync();
            Assert.Equal(_clock.Now, order.Payday);
            Assert.Equal(_clock.Now, order.PaymentScheduledDate);
        }

        #endregion

        [Fact]
        public async Task ACheckoutLeftUnpaidForADay_ExpiresWhenTheNextOneIsCreated()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var abandoned = await Service().CreateAsync(Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));
            _clock.Now = _clock.Now.AddHours(23);
            var recent = await Service().CreateAsync(Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));

            _clock.Now = _clock.Now.AddHours(2);
            await Service().CreateAsync(Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));

            Assert.Equal(StorefrontCheckoutStatus.Expired, (await Service().GetAsync(abandoned.Checkout!.Id))!.Status);
            Assert.Equal(StorefrontCheckoutStatus.AwaitingPayment, (await Service().GetAsync(recent.Checkout!.Id))!.Status);
        }

        [Fact]
        public async Task TheCepLookupsIbgeCode_FillsTheNewCustomersCityCode()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var request = Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)) with { Address = Paulista with { IbgeCode = "3550308" } };

            await PaidAsync(Service(), request);

            using var context = _database.CreateContext();
            Assert.Equal("3550308", (await context.Customer.SingleAsync()).CodigoMunicipioIbge);
        }

        #region R7 declines, expiry, renewal

        [Fact]
        public async Task Declines_CarryTheirCode_AndCreateNoOrder()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var service = Service();
            var created = await service.CreateAsync(Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));

            var declined = (await service.SimulateAsync(created.Checkout!.Id, StorefrontSimulatedOutcome.Decline))!;
            Assert.Equal((StorefrontCheckoutStatus.Declined, "card_declined"), (declined.Status, declined.DeclineCode));

            var broke = (await service.SimulateAsync(created.Checkout.Id, StorefrontSimulatedOutcome.InsufficientFunds))!;
            Assert.Equal("insufficient_funds", broke.DeclineCode);

            using var context = _database.CreateContext();
            Assert.Equal(0, await context.Order.CountAsync());
        }

        [Fact]
        public async Task APixPastItsExpiry_ReadsExpired_AndRenewingIssuesAFreshCode()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var created = await Service().CreateAsync(Request(99m, StorefrontPaymentMethod.Pix, null, (mug.Id, 1)));

            _clock.Now = _clock.Now.AddMinutes(31);
            Assert.Equal(StorefrontCheckoutStatus.Expired, (await Service().GetAsync(created.Checkout!.Id))!.Status);

            var renewed = (await Service().RenewPixAsync(created.Checkout.Id))!;
            Assert.Equal(StorefrontCheckoutStatus.AwaitingPayment, renewed.Status);
            Assert.Equal(_clock.Now.AddMinutes(30), renewed.Pix!.ExpiresAt);
        }

        [Fact]
        public async Task ThePixExpireSimulation_Expires_AndACardCheckoutCannotBeRenewed()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var pix = await Service().CreateAsync(Request(99m, StorefrontPaymentMethod.Pix, null, (mug.Id, 1)));
            var card = await Service().CreateAsync(Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));

            Assert.Equal(StorefrontCheckoutStatus.Expired, (await Service().SimulateAsync(pix.Checkout!.Id, StorefrontSimulatedOutcome.PixExpire))!.Status);
            Assert.Null(await Service().RenewPixAsync(card.Checkout!.Id));
        }

        #endregion

        #region R8 purchases

        [Fact]
        public async Task APurchase_ReadsItsOrdersLive_WithTrackingDatesAndShopperStatus()
        {
            var (aurora, _, mug, pen) = await TwoShopsAsync();
            var paid = await PaidAsync(Service(), Request(235m, StorefrontPaymentMethod.Card, null, (mug.Id, 1), (pen.Id, 2)));
            var shippedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            using (var context = _database.CreateContext())
            {
                var order = await context.Order.SingleAsync(o => o.EnterpriseId == aurora.Id);
                order.Status = (int)EnumOrderStatus.Shipped;
                order.ShippedAt = shippedAt;
                order.TrackingCode = "BR1BR";
                order.TrackingUrl = "https://t.example/BR1BR";
                await context.SaveChangesAsync();
            }

            var purchase = (await Service().GetPurchaseAsync(paid.Id))!;

            Assert.Equal((235m, StorefrontPaymentMethod.Card), (purchase.Total, purchase.PaymentMethod));
            var shipped = purchase.Orders.Single(o => o.StoreSlug == "aurora");
            Assert.Equal((StorefrontOrderStatus.Shipped, "BR1BR", "https://t.example/BR1BR", shippedAt),
                (shipped.Status, shipped.TrackingCode, shipped.TrackingUrl, shipped.ShippedAt));
            Assert.Equal(("Cerâmica Aurora", "aurora@example.com", 99m, 5, 9, "São Paulo", "SP"),
                (shipped.StoreName, shipped.ContactEmail, shipped.Amount, shipped.DeliveryDaysMin, shipped.DeliveryDaysMax, shipped.DeliveryCity, shipped.DeliveryUf));
            Assert.Equal("Caneca — Azul", shipped.Lines.Single().Name);
            Assert.Equal(StorefrontOrderStatus.Paid, purchase.Orders.Single(o => o.StoreSlug == "traco").Status);
        }

        [Theory]
        [InlineData(EnumOrderStatus.Pending, StorefrontOrderStatus.Paid)]
        [InlineData(EnumOrderStatus.Paid, StorefrontOrderStatus.Paid)]
        [InlineData(EnumOrderStatus.Processing, StorefrontOrderStatus.Processing)]
        [InlineData(EnumOrderStatus.Shipped, StorefrontOrderStatus.Shipped)]
        [InlineData(EnumOrderStatus.Delivered, StorefrontOrderStatus.Delivered)]
        [InlineData(EnumOrderStatus.Finished, StorefrontOrderStatus.Delivered)]
        [InlineData(EnumOrderStatus.Refund, StorefrontOrderStatus.Refunded)]
        public void ErpStatuses_MapToWhatTheShopperSees(EnumOrderStatus erp, StorefrontOrderStatus shopper) =>
            Assert.Equal(shopper, StorefrontCheckoutService.ShopperStatus((int)erp));

        [Fact]
        public async Task ARefundedOrder_KeepsTheAmountThatWasPaid()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var paid = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));
            using (var context = _database.CreateContext())
            {
                var order = await context.Order.SingleAsync();
                order.Status = (int)EnumOrderStatus.Refund;
                order.TotalValue = 0;
                await context.SaveChangesAsync();
            }

            var refunded = (await Service().GetPurchaseAsync(paid.Id))!.Orders.Single();

            Assert.Equal((StorefrontOrderStatus.Refunded, 99m, false), (refunded.Status, refunded.Amount, refunded.CanWithdraw));
        }

        [Fact]
        public async Task OnlyPaidCheckouts_AreListedAsPurchases_NewestFirst()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var shopper = Guid.NewGuid();
            var first = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, shopper, (mug.Id, 1)));
            _clock.Now = _clock.Now.AddHours(1);
            var second = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, shopper, (mug.Id, 1)));
            var unpaid = await Service().CreateAsync(Request(99m, StorefrontPaymentMethod.Card, shopper, (mug.Id, 1)));

            var purchases = await Service().ListPurchasesAsync(shopper);

            Assert.Equal([second.Id, first.Id], purchases.Select(p => p.CheckoutId));
            Assert.Null(await Service().GetPurchaseAsync(unpaid.Checkout!.Id));
        }

        #endregion

        #region R9 withdrawal

        [Fact]
        public async Task Withdrawal_IsRecordedWithAProtocol_AndTheStoreIsNotified()
        {
            var (aurora, _, mug, _) = await TwoShopsAsync();
            var paid = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));
            var number = paid.Outcomes.Single().OrderNumber!;
            var actor = Guid.NewGuid();

            var order = (await Service().RequestWithdrawalAsync(paid.Id, number, actor))!;

            Assert.Equal(_clock.Now, order.WithdrawalRequestedAt);
            Assert.Equal("DS-" + number[3..], order.WithdrawalProtocol);
            Assert.False(order.CanWithdraw);

            using var context = _database.CreateContext();
            var notification = await context.Notification.SingleAsync();
            Assert.Equal((aurora.Id, EnumNotificationSeverity.Warning, actor), (notification.EnterpriseId, notification.Severity, notification.CreatedByUserId));
            Assert.Contains(number, notification.Title);
            Assert.Contains(order.WithdrawalProtocol!, notification.Body);
        }

        [Fact]
        public async Task Withdrawal_ClosesSevenDaysAfterDelivery()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var paid = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));
            var number = paid.Outcomes.Single().OrderNumber!;
            using (var context = _database.CreateContext())
            {
                var row = await context.Order.SingleAsync();
                row.Status = (int)EnumOrderStatus.Delivered;
                row.DeliveredAt = _clock.Now;
                await context.SaveChangesAsync();
            }

            _clock.Now = _clock.Now.AddDays(7);
            Assert.True((await Service().GetPurchaseAsync(paid.Id))!.Orders.Single().CanWithdraw);

            _clock.Now = _clock.Now.AddMinutes(1);
            var late = (await Service().RequestWithdrawalAsync(paid.Id, number, Guid.NewGuid()))!;

            Assert.Equal((false, null), (late.CanWithdraw, late.WithdrawalRequestedAt));
            using var check = _database.CreateContext();
            Assert.Equal(0, await check.Notification.CountAsync());
        }

        [Fact]
        public async Task Withdrawal_OfAnOrderOutsideTheCheckout_IsNotFound()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var paid = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));

            Assert.Null(await Service().RequestWithdrawalAsync(paid.Id, "LM-00000000", Guid.NewGuid()));
            Assert.Null(await Service().RequestWithdrawalAsync(Guid.NewGuid(), paid.Outcomes.Single().OrderNumber!, Guid.NewGuid()));
        }

        [Fact]
        public async Task TheStepSimulator_ShipsWithTrackingThenDelivers()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var paid = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));
            var number = paid.Outcomes.Single().OrderNumber!;

            var shipped = (await Service().SimulateStepAsync(paid.Id, number, StorefrontSimulatedOrderStep.Shipped))!;
            Assert.Equal(StorefrontOrderStatus.Shipped, shipped.Status);
            Assert.Equal($"LM{number[3..]}BR", shipped.TrackingCode);
            Assert.NotNull(shipped.ShippedAt);

            var delivered = (await Service().SimulateStepAsync(paid.Id, number, StorefrontSimulatedOrderStep.Delivered))!;
            Assert.Equal(StorefrontOrderStatus.Delivered, delivered.Status);
            Assert.NotNull(delivered.DeliveredAt);

            Assert.Null(await Service(simulate: false).SimulateStepAsync(paid.Id, number, StorefrontSimulatedOrderStep.Processing));
        }

        #endregion

        #region R10 ownership

        [Fact]
        public async Task AGuestPurchase_IsAttachedOnce_ByIdOrByVerifiedEmail()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var one = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));
            var two = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));
            var shopper = Guid.NewGuid();

            Assert.True(await Service().AttachShopperAsync(one.Id, shopper));
            Assert.False(await Service().AttachShopperAsync(one.Id, Guid.NewGuid()));
            Assert.Equal(shopper, await Service().OwnerOfAsync(one.Id));

            Assert.Equal(1, await Service().AttachGuestPurchasesAsync(" ANA@example.com ", shopper));
            Assert.Equal(shopper, await Service().OwnerOfAsync(two.Id));
            Assert.Equal(0, await Service().AttachGuestPurchasesAsync("ana@example.com", Guid.NewGuid()));
        }

        [Fact]
        public async Task DeletingTheAccount_UnlinksItsPurchasesAndCustomers_ButKeepsTheOrders()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var shopper = Guid.NewGuid();
            var paid = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, shopper, (mug.Id, 1)));

            Assert.Equal("ana@example.com", (await Service().ListPurchaseContactsAsync(shopper)).Single().Customer.Email.ToLowerInvariant());

            await Service().DetachShopperAsync(shopper);

            Assert.Null(await Service().OwnerOfAsync(paid.Id));
            Assert.Empty(await Service().ListPurchasesAsync(shopper));
            using var context = _database.CreateContext();
            Assert.Null((await context.Customer.SingleAsync()).ExternalShopperId);
            Assert.Equal(1, await context.Order.CountAsync());
        }

        #endregion

        #region R11 invoice

        [Fact]
        public async Task AnAuthorizedNFe_ShowsItsKey_AndItsXmlCanBeDownloaded()
        {
            var (aurora, _, mug, _) = await TwoShopsAsync();
            var paid = await PaidAsync(Service(), Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)));
            var number = paid.Outcomes.Single().OrderNumber!;
            Guid orderId;
            using (var context = _database.CreateContext())
                orderId = (await context.Order.SingleAsync()).Id;

            Assert.Null((await Service().GetPurchaseAsync(paid.Id))!.Orders.Single().InvoiceAccessKey);
            Assert.Null(await Service().GetInvoiceAsync(paid.Id, number));

            var key = new string('3', 44);
            await _database.SeedAsync(
                new NFeDocument { Id = Guid.NewGuid(), OrderId = orderId, EnterpriseId = aurora.Id, Status = NFeStatus.Pending, AccessKey = new string('1', 44), XmlContent = "<pending/>", IsActive = true },
                new NFeDocument { Id = Guid.NewGuid(), OrderId = orderId, EnterpriseId = aurora.Id, Type = NFeDocumentType.NFCe, Status = NFeStatus.Authorized, AccessKey = key, XmlContent = "<nfeProc/>", IsActive = true });

            Assert.Equal(key, (await Service().GetPurchaseAsync(paid.Id))!.Orders.Single().InvoiceAccessKey);
            var invoice = (await Service().GetInvoiceAsync(paid.Id, number))!;
            Assert.Equal((key, "<nfeProc/>"), (invoice.AccessKey, Encoding.UTF8.GetString(invoice.Xml)));
        }

        #endregion

        [Fact]
        public void Installment_plans_follow_the_price_table_and_stop_at_the_minimum_value()
        {
            var plan = StorefrontInstallments.Plan(1000m, 3, 2.69m);
            Assert.Equal((351.43m, 1054.29m), (plan.Value, plan.Total));
            Assert.Equal(37.51m, plan.CetAnnual);
            Assert.Equal(0m, StorefrontInstallments.Plan(30m, 1, 0m).CetAnnual);
        }

        [Fact]
        public async Task The_quote_offers_card_plans_and_checkout_keeps_the_chosen_one_with_cash_orders()
        {
            var (_, _, mug, _) = await TwoShopsAsync();
            var quote = await Service().QuoteAsync([new StorefrontCartLineDTO(mug.Id, 1)]);

            // 79 + 20 shipping = 99: 1× plus the three configured rates (2×–4×), each instalment above R$ 10.
            Assert.Equal([1, 2, 3, 4], quote.Installments!.Select(p => p.Count));
            Assert.Equal(99m, quote.Installments[0].Total);

            var refused = await Service().CreateAsync(Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)) with { Installments = 12 });
            Assert.Null(refused.Checkout);

            var pixWithPlan = await Service().CreateAsync(Request(99m, StorefrontPaymentMethod.Pix, null, (mug.Id, 1)) with { Installments = 3 });
            Assert.Null(pixWithPlan.Checkout);

            var created = await Service().CreateAsync(Request(99m, StorefrontPaymentMethod.Card, null, (mug.Id, 1)) with { Installments = 3 });
            var installment = created.Checkout!.Installment!;
            Assert.Equal(3, installment.Count);
            Assert.True(installment.Total > 99m);
            Assert.Equal(99m, created.Checkout.Total);
        }
    
        #region Line photos

        [Fact]
        public async Task Quote_CarriesEachLinesOwnPhotoAndItsFamilyPhoto_AsCatalogCardsDo()
        {
            var (aurora, _, mug, pen) = await TwoShopsAsync();
            pen.PictureAdress = "products/lapiseira.png";
            await using (var context = _database.CreateContext())
            {
                context.Product.Update(pen);
                context.ProductVariantGroup.Add(new ProductVariantGroup { Id = mug.VariantGroupId!.Value, EnterpriseId = aurora.Id, PictureAdress = "product-families/caneca.png", IsActive = true });
                await context.SaveChangesAsync();
            }

            var quote = await Service().QuoteAsync([new(mug.Id, 1), new(pen.Id, 1)]);

            var lines = quote.Stores.SelectMany(s => s.Lines).ToDictionary(l => l.ProductId);
            Assert.Equal((false, mug.VariantGroupId), (lines[mug.Id].HasPicture, lines[mug.Id].FamilyPictureId));
            Assert.Equal((true, (Guid?)null), (lines[pen.Id].HasPicture, lines[pen.Id].FamilyPictureId));
        }

        [Fact]
        public async Task Quote_NeverBorrowsAnotherShopsFamilyPhotoForTheSameGroupId()
        {
            var (_, traco, mug, _) = await TwoShopsAsync();
            await using (var context = _database.CreateContext())
            {
                context.ProductVariantGroup.Add(new ProductVariantGroup { Id = mug.VariantGroupId!.Value, EnterpriseId = traco.Id, PictureAdress = "product-families/alheia.png", IsActive = true });
                await context.SaveChangesAsync();
            }

            var line = (await Service().QuoteAsync([new(mug.Id, 1)])).Stores.Single().Lines.Single();

            Assert.Null(line.FamilyPictureId);
        }

        [Fact]
        public async Task Purchase_KeepsTheLinePhotosCapturedAtCheckout()
        {
            var (_, _, _, pen) = await TwoShopsAsync();
            pen.PictureAdress = "products/lapiseira.png";
            await using (var context = _database.CreateContext())
            {
                context.Product.Update(pen);
                await context.SaveChangesAsync();
            }

            var paid = await PaidAsync(Service(), Request(78m, StorefrontPaymentMethod.Pix, null, (pen.Id, 1)));
            var purchase = (await Service().GetPurchaseAsync(paid.Id))!;

            var line = purchase.Orders.Single().Lines.Single();
            Assert.Equal((pen.Id, true, (Guid?)null), (line.ProductId, line.HasPicture, line.FamilyPictureId));
        }

        [Fact]
        public void ALineStoredBeforeLinePhotos_StillReadsWithNoPhoto()
        {
            var stored = """{"ProductId":"6f9619ff-8b86-d011-b42d-00c04fc964ff","Name":"Caneca","UnitPrice":79,"Quantity":1,"LineTotal":79,"Issue":null}""";

            var line = System.Text.Json.JsonSerializer.Deserialize<StorefrontQuoteLineDTO>(stored)!;

            Assert.Equal(("Caneca", false, (Guid?)null), (line.Name, line.HasPicture, line.FamilyPictureId));
        }

        #endregion
}
}
