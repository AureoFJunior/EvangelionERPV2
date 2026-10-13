using System.Text;
using System.Text.Json;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.StorefrontModule.Application.DTOs;
using EvangelionERPV2.StorefrontModule.Application.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace EvangelionERPV2.StorefrontModule.Application.Services
{
    public sealed class StorefrontCheckoutService : IStorefrontCheckoutService
    {
        public static readonly TimeSpan PixLifetime = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan WithdrawalWindow = TimeSpan.FromDays(7);

        /// <summary>After this many tries a store whose order still fails is handed to a person.</summary>
        public const int MaxSettleAttempts = 3;

        /// <summary>
        /// Reads poll every few seconds; a store that failed is retried 1, then 2 minutes later, so a
        /// passing database hiccup has time to clear before the attempts run out.
        /// </summary>
        public static TimeSpan SettleRetryDelay(int attempts) => TimeSpan.FromMinutes(attempts);

        private readonly AppDbContext _context;
        private readonly IOrderService<Order> _orderService;
        private readonly TimeProvider _clock;
        private readonly Guid? _platformEnterpriseId;
        private readonly StorefrontInstallments _installments;

        public StorefrontCheckoutService(AppDbContext context, IOrderService<Order> orderService, IConfiguration configuration, TimeProvider? clock = null)
        {
            _context = context;
            _orderService = orderService;
            _clock = clock ?? TimeProvider.System;
            _platformEnterpriseId = Guid.TryParse(configuration["Storefront:PlatformEnterpriseId"], out var platform) ? platform : null;
            SimulationEnabled = bool.TryParse(configuration["Storefront:SimulatePayments"], out var simulate) && simulate;
            _installments = new StorefrontInstallments(configuration);
        }

        public bool SimulationEnabled { get; }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        #region Quote and checkout

        public async Task<StorefrontQuoteDTO> QuoteAsync(IReadOnlyList<StorefrontCartLineDTO> lines)
        {
            var ids = lines.Select(line => line.ProductId).Distinct().ToList();
            var products = await StorefrontService.Published(_context)
                .Include(p => p.Enterprise)
                .Where(p => ids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);

            var groupIds = products.Values.Where(p => p.VariantGroupId != null).Select(p => p.VariantGroupId!.Value).Distinct().ToList();
            var familiesWithPicture = groupIds.Count == 0
                ? []
                : (await _context.ProductVariantGroup.AsNoTracking()
                    .Where(g => groupIds.Contains(g.Id) && g.PictureAdress != null && g.PictureAdress != "")
                    .Select(g => new { g.Id, g.EnterpriseId })
                    .ToListAsync())
                .Select(g => (g.Id, g.EnterpriseId))
                .ToHashSet();

            var priced = new List<(StorefrontQuoteLineDTO Line, Enterprise? Store)>();
            foreach (var input in lines)
            {
                if (!products.TryGetValue(input.ProductId, out var product))
                {
                    priced.Add((new StorefrontQuoteLineDTO(input.ProductId, "", 0, 0, 0, "gone"), null));
                    continue;
                }

                var maxPerOrder = StorefrontService.IsAvailable(product.IsService, product.StorageQuantity)
                    ? StorefrontService.MaxPerOrder(product.IsService, product.StorageQuantity)
                    : 0;
                var (quantity, issue) = maxPerOrder < 1
                    ? (0, "out-of-stock")
                    : input.Quantity > maxPerOrder
                        ? (maxPerOrder, "quantity-reduced")
                        : (input.Quantity, (string?)null);

                var name = string.IsNullOrWhiteSpace(product.VariantLabel) ? product.Name : $"{product.Name} — {product.VariantLabel}";
                var familyPictureId = product.VariantGroupId is { } groupId && familiesWithPicture.Contains((groupId, product.EnterpriseId!.Value))
                    ? groupId
                    : (Guid?)null;
                priced.Add((new StorefrontQuoteLineDTO(product.Id, name, product.DefaultValue, quantity,
                    Math.Round(product.DefaultValue * quantity, 2), issue,
                    !string.IsNullOrEmpty(product.PictureAdress), familyPictureId), product.Enterprise));
            }

            var stores = priced
                .Where(p => p.Store != null)
                .GroupBy(p => p.Store!.Id)
                .Select(group =>
                {
                    var store = group.First().Store!;
                    var subtotal = group.Sum(p => p.Line.LineTotal);
                    return new StorefrontQuoteStoreDTO(
                        store.StorefrontSlug!,
                        store.Name,
                        group.Select(p => p.Line).ToList(),
                        subtotal,
                        subtotal > 0 ? store.StorefrontShippingFee : 0,
                        store.StorefrontHandlingDays + store.StorefrontDeliveryDaysMin,
                        store.StorefrontHandlingDays + store.StorefrontDeliveryDaysMax);
                })
                .ToList();

            var gone = priced.Where(p => p.Store == null).Select(p => p.Line).ToList();
            if (gone.Count > 0)
                stores.Add(new StorefrontQuoteStoreDTO("", "", gone, 0, 0, 0, 0));

            var total = stores.Sum(s => s.Subtotal + s.Shipping);
            return new StorefrontQuoteDTO(stores, stores.Sum(s => s.Subtotal), stores.Sum(s => s.Shipping), total, _installments.Plans(total));
        }

        /// <summary>
        /// Re-prices in the ERP; a quote with an issue, or one that moved from what the shopper saw, is
        /// handed back instead of charged (spec D7, §11.13).
        /// </summary>
        public async Task<StorefrontCheckoutResultDTO> CreateAsync(StorefrontCheckoutRequestDTO request)
        {
            if (!SimulationEnabled)
                throw new InvalidOperationException("No payment provider is connected yet.");

            await ExpireAbandonedAsync();

            var quote = await QuoteAsync(request.Lines);
            var hasIssues = quote.Stores.SelectMany(s => s.Lines).Any(l => l.Issue != null);
            if (hasIssues || quote.Total <= 0 || Math.Round(quote.Total, 2) != Math.Round(request.ExpectedTotal, 2))
                return new StorefrontCheckoutResultDTO(null, quote);

            // Pix is always à vista (I1); a card plan the quote no longer offers is a changed offer, not an error.
            var plan = request.PaymentMethod == StorefrontPaymentMethod.Card
                ? quote.Installments?.FirstOrDefault(p => p.Count == request.Installments)
                : null;
            if (request.PaymentMethod == StorefrontPaymentMethod.Card ? plan == null : request.Installments != 1)
                return new StorefrontCheckoutResultDTO(null, quote);

            var now = Now;
            var checkout = new StorefrontCheckout
            {
                Id = Guid.NewGuid(),
                ShopperId = request.ShopperId,
                Email = request.Customer.Email.Trim().ToLowerInvariant(),
                Status = (int)StorefrontCheckoutStatus.AwaitingPayment,
                PaymentMethod = (int)request.PaymentMethod,
                TotalValue = quote.Total,
                Installments = plan?.Count ?? 1,
                InstallmentValue = plan?.Value,
                InterestMonthly = plan?.MonthlyRate,
                CetAnnual = plan?.CetAnnual,
                TotalWithInterest = plan?.Total,
                QuoteJson = JsonSerializer.Serialize(quote),
                CustomerJson = JsonSerializer.Serialize(request.Customer),
                AddressJson = JsonSerializer.Serialize(request.Address),
                Simulated = true,
                CreatedAt = now,
                IsActive = true
            };

            if (request.PaymentMethod == StorefrontPaymentMethod.Pix)
                (checkout.PixCode, checkout.PixExpiresAt) = NewPix(checkout.Id, quote.Total, now);

            _context.StorefrontCheckout.Add(checkout);
            await _context.SaveChangesAsync();

            return new StorefrontCheckoutResultDTO(ToDto(checkout), null);
        }

        public async Task<StorefrontCheckoutDTO?> GetAsync(Guid id)
        {
            var checkout = await _context.StorefrontCheckout.FirstOrDefaultAsync(c => c.Id == id);
            if (checkout == null)
                return null;

            if (checkout.Status == (int)StorefrontCheckoutStatus.Settling && checkout.SettleAttempts < MaxSettleAttempts &&
                Now >= (checkout.UpdatedAt ?? checkout.CreatedAt) + SettleRetryDelay(checkout.SettleAttempts))
                return ToDto(await SettleAsync(id, refundLastStore: false));

            return ToDto(await ExpireAsync(checkout));
        }

        public async Task<StorefrontCheckoutDTO?> RenewPixAsync(Guid id)
        {
            var checkout = await _context.StorefrontCheckout.FirstOrDefaultAsync(c => c.Id == id);
            if (checkout == null || checkout.PaymentMethod != (int)StorefrontPaymentMethod.Pix ||
                checkout.Status is (int)StorefrontCheckoutStatus.Paid or (int)StorefrontCheckoutStatus.Settling)
                return null;

            (checkout.PixCode, checkout.PixExpiresAt) = NewPix(checkout.Id, checkout.TotalValue, Now);
            checkout.Status = (int)StorefrontCheckoutStatus.AwaitingPayment;
            checkout.UpdatedAt = Now;
            await _context.SaveChangesAsync();
            return ToDto(checkout);
        }

        public async Task<StorefrontCheckoutDTO?> SimulateAsync(Guid id, StorefrontSimulatedOutcome outcome)
        {
            if (!SimulationEnabled)
                return null;

            var checkout = await _context.StorefrontCheckout.FirstOrDefaultAsync(c => c.Id == id);
            if (checkout == null)
                return null;

            checkout = await ExpireAsync(checkout);
            if (checkout.Status == (int)StorefrontCheckoutStatus.Paid)
                return ToDto(checkout);

            // The money is taken: whatever the simulator is asked, only the orders are left to create.
            if (checkout.Status == (int)StorefrontCheckoutStatus.Settling)
                return ToDto(await SettleAsync(id, refundLastStore: false));

            switch (outcome)
            {
                case StorefrontSimulatedOutcome.Approve:
                case StorefrontSimulatedOutcome.PixPaid:
                    return ToDto(await SettleAsync(id, refundLastStore: false));
                case StorefrontSimulatedOutcome.OutOfStock:
                    return ToDto(await SettleAsync(id, refundLastStore: true));
                case StorefrontSimulatedOutcome.Decline:
                    checkout.Status = (int)StorefrontCheckoutStatus.Declined;
                    checkout.DeclineCode = "card_declined";
                    break;
                case StorefrontSimulatedOutcome.InsufficientFunds:
                    checkout.Status = (int)StorefrontCheckoutStatus.Declined;
                    checkout.DeclineCode = "insufficient_funds";
                    break;
                case StorefrontSimulatedOutcome.PixExpire:
                    checkout.Status = (int)StorefrontCheckoutStatus.Expired;
                    if (checkout.PixCode != null)
                        checkout.PixExpiresAt = Now;
                    break;
            }

            checkout.UpdatedAt = Now;
            await _context.SaveChangesAsync();
            return ToDto(checkout);
        }

        /// <summary>
        /// One order per store, each through <c>OrderService.CreateAsync</c> (its own transaction, stock
        /// movements and balance), and the checkout's outcomes written after each — the PDV's
        /// write-ahead shape, so a retry resumes rather than sells twice. A store whose stock no longer
        /// covers its lines gets its share refunded instead (spec §7.2). A store whose order fails for any
        /// other reason gets no outcome: the checkout stays Settling and the next read or settle retries it
        /// (review spec L1).
        /// </summary>
        private async Task<StorefrontCheckout> SettleAsync(Guid id, bool refundLastStore)
        {
            var checkout = await _context.StorefrontCheckout.AsNoTracking().FirstAsync(c => c.Id == id);
            var quote = Read<StorefrontQuoteDTO>(checkout.QuoteJson);
            var outcomes = Read<List<StorefrontOutcomeDTO>>(checkout.OutcomesJson);
            var stores = quote.Stores.Where(s => s.Slug != "" && s.Subtotal > 0).ToList();

            for (var index = 0; index < stores.Count; index++)
            {
                if (outcomes.Any(o => o.Slug == stores[index].Slug))
                    continue;

                if (await SettleStoreAsync(checkout, stores[index], forceRefund: refundLastStore && index == stores.Count - 1) is not { } outcome)
                    continue;

                outcomes.Add(outcome);
                await SaveCheckoutAsync(id, row => row.OutcomesJson = JsonSerializer.Serialize(outcomes));
            }

            var pending = stores.Where(store => outcomes.All(o => o.Slug != store.Slug)).Select(store => store.Name).ToList();
            var settled = await SaveCheckoutAsync(id, row =>
            {
                row.Status = pending.Count == 0 ? (int)StorefrontCheckoutStatus.Paid : (int)StorefrontCheckoutStatus.Settling;
                row.DeclineCode = null;
                row.PaidAt ??= Now;
                row.SettleAttempts++;
            });

            if (pending.Count > 0 && settled.SettleAttempts == MaxSettleAttempts)
                await NotifyPlatformAsync(settled, pending);

            return settled;
        }

        /// <summary>The platform's bell: a paid checkout whose orders still could not be created.</summary>
        private async Task NotifyPlatformAsync(StorefrontCheckout checkout, IReadOnlyList<string> stores)
        {
            Log.Logger.Error("Limen checkout [{CheckoutId}] is paid but the orders of {Stores} failed {Attempts} times",
                checkout.Id, string.Join(", ", stores), checkout.SettleAttempts);

            if (_platformEnterpriseId is not { } platform)
                return;

            var author = await _context.User.AsNoTracking()
                .Where(u => u.EnterpriseId == platform && u.IsServiceAccount)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync();
            if (author == null)
                return;

            _context.Notification.Add(new Notification
            {
                Id = Guid.NewGuid(),
                EnterpriseId = platform,
                Title = "Compra paga sem pedido na Limen",
                Body = $"A compra {checkout.Id} foi paga, mas o pedido de {string.Join(", ", stores)} falhou {checkout.SettleAttempts} vezes. " +
                       "Verifique o registro de erros e crie o pedido ou estorne o valor da loja.",
                Severity = EnumNotificationSeverity.Critical,
                CreatedByUserId = author.Value,
                CreatedAt = Now,
                IsActive = true
            });
            await _context.SaveChangesAsync();
        }

        /// <summary>Null when the order could not be created for a reason other than stock — try again later.</summary>
        private async Task<StorefrontOutcomeDTO?> SettleStoreAsync(StorefrontCheckout checkout, StorefrontQuoteStoreDTO store, bool forceRefund)
        {
            var reference = checkout.Id.ToString();
            var enterprise = await _context.Enterprise.AsNoTracking().FirstOrDefaultAsync(e => e.StorefrontSlug == store.Slug);

            StorefrontOutcomeDTO Refunded() => Outcome(store, StorefrontOutcomeKind.RefundedOutOfStock, null, enterprise?.Email ?? "");
            StorefrontOutcomeDTO Ordered(Guid orderId) => Outcome(store, StorefrontOutcomeKind.Ordered, OrderNumber(orderId), enterprise!.Email);

            if (enterprise == null)
                return Refunded();

            var existing = await ExistingOrderIdAsync(enterprise.Id, reference);
            if (existing != null)
                return Ordered(existing.Value);

            if (forceRefund)
                return Refunded();

            var wanted = store.Lines
                .Where(line => line.Quantity > 0)
                .GroupBy(line => line.ProductId)
                .Select(group => (ProductId: group.Key, Quantity: group.Sum(line => line.Quantity), UnitPrice: group.First().UnitPrice))
                .ToList();
            var ids = wanted.Select(line => line.ProductId).ToList();
            var products = await _context.Product.AsNoTracking()
                .Where(p => ids.Contains(p.Id) && p.EnterpriseId == enterprise.Id && p.IsActive == true)
                .ToDictionaryAsync(p => p.Id);

            if (wanted.Count == 0 || !StockCovers(wanted, products))
                return Refunded();

            var customer = Read<StorefrontCustomerDTO>(checkout.CustomerJson);
            var address = Read<StorefrontAddressDTO>(checkout.AddressJson);
            var customerId = await UpsertCustomerAsync(enterprise.Id, checkout.ShopperId, customer, address);
            var paidAt = checkout.PaidAt ?? Now;

            var order = new Order
            {
                EnterpriseId = enterprise.Id,
                CustomerId = customerId,
                Status = (int)EnumOrderStatus.Paid,
                Channel = (int)EnumOrderChannel.Storefront,
                ExternalReference = reference,
                ShippingValue = store.Shipping,
                DeliveryAddress = checkout.AddressJson,
                PaymentMethod = checkout.PaymentMethod == (int)StorefrontPaymentMethod.Pix ? (int)EnumPaymentMethod.Pix : (int)EnumPaymentMethod.Credit,
                Payday = paidAt,
                PaymentScheduledDate = paidAt,
                OrderedProduct = wanted.Select(line => new OrderedProduct
                {
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    Value = line.UnitPrice,
                    UnitOfMeasure = products[line.ProductId].UnitOfMeasure
                }).ToList()
            };

            try
            {
                // ponytail: services carry no stock, so an all-service order may go negative; a mixed
                // order enforces stock for every line, and a service line at zero refunds that store.
                var created = await _orderService.CreateAsync(order, allowNegativeStock: products.Values.All(p => p.IsService));
                return Ordered(created.Id);
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "Limen checkout [{CheckoutId}] could not create the order of enterprise [{EnterpriseId}]",
                    checkout.Id, enterprise.Id);
                _context.ChangeTracker.Clear();
                if (await ExistingOrderIdAsync(enterprise.Id, reference) is { } raced)
                    return Ordered(raced);

                var now = await _context.Product.AsNoTracking()
                    .Where(p => ids.Contains(p.Id) && p.EnterpriseId == enterprise.Id && p.IsActive == true)
                    .ToDictionaryAsync(p => p.Id);
                return StockCovers(wanted, now) ? null : Refunded();
            }
        }

        private static bool StockCovers(IReadOnlyCollection<(Guid ProductId, int Quantity, decimal UnitPrice)> wanted, IReadOnlyDictionary<Guid, Product> products) =>
            products.Count == wanted.Count &&
            wanted.All(line => products[line.ProductId].IsService || products[line.ProductId].StorageQuantity >= line.Quantity);

        private Task<Guid?> ExistingOrderIdAsync(Guid enterpriseId, string reference) =>
            _context.Order.AsNoTracking()
                .Where(o => o.EnterpriseId == enterpriseId && o.ExternalReference == reference)
                .Select(o => (Guid?)o.Id)
                .FirstOrDefaultAsync();

        /// <summary>
        /// The store's customer for this shopper — by shopper id, else by e-mail and CPF. A new one takes
        /// everything typed at checkout; an existing one only has its blanks filled, so corrections the
        /// store made by hand survive (review spec L2). The order carries its own delivery address.
        /// </summary>
        private async Task<Guid> UpsertCustomerAsync(Guid enterpriseId, Guid? shopperId, StorefrontCustomerDTO input, StorefrontAddressDTO address)
        {
            var shopperKey = shopperId?.ToString();
            var email = input.Email.Trim().ToLowerInvariant();

            var customer = shopperKey == null
                ? null
                : await _context.Customer.FirstOrDefaultAsync(c => c.EnterpriseId == enterpriseId && c.IsActive == true && c.ExternalShopperId == shopperKey);
            customer ??= await _context.Customer.FirstOrDefaultAsync(c =>
                c.EnterpriseId == enterpriseId && c.IsActive == true && c.Email.ToLower() == email && c.Document == input.Cpf);

            if (customer == null)
            {
                customer = new Customer { Id = Guid.NewGuid(), EnterpriseId = enterpriseId, CreatedAt = Now, IsActive = true };
                _context.Customer.Add(customer);
            }

            static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

            if (Blank(customer.Name)) customer.Name = input.FullName;
            if (Blank(customer.Email)) customer.Email = email;
            if (Blank(customer.PhoneNumber)) customer.PhoneNumber = input.Phone;
            if (Blank(customer.Document)) customer.Document = input.Cpf;
            customer.ExternalShopperId ??= shopperKey;

            if (Blank(customer.Logradouro) && Blank(customer.Cep))
            {
                customer.Logradouro = Truncate(address.Street, 60);
                customer.Numero = address.Number ?? "S/N";
                customer.Complemento = Blank(address.Complement) ? null : Truncate(address.Complement!, 60);
                customer.Bairro = Truncate(address.District, 60);
                customer.Municipio = Truncate(address.City, 60);
                customer.Uf = address.Uf;
                customer.Cep = address.Cep;
            }

            if (Blank(customer.CodigoMunicipioIbge) && address.IbgeCode is { Length: 7 } ibge && ibge.All(char.IsAsciiDigit) && customer.Cep == address.Cep)
                customer.CodigoMunicipioIbge = ibge;

            if (Blank(customer.Adress))
                customer.Adress = $"{address.Street}, {address.Number ?? "S/N"} - {address.District}, {address.City}/{address.Uf}, CEP {address.Cep}";

            customer.UpdatedAt = Now;

            await _context.SaveChangesAsync();
            return customer.Id;
        }

        private async Task<StorefrontCheckout> SaveCheckoutAsync(Guid id, Action<StorefrontCheckout> change)
        {
            var row = await _context.StorefrontCheckout.FirstAsync(c => c.Id == id);
            change(row);
            row.UpdatedAt = Now;
            await _context.SaveChangesAsync();
            return row;
        }

        /// <summary>A checkout nobody paid within a day expires; the row stays for the audit (review spec L10).</summary>
        public static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(24);

        // ponytail: swept on each new checkout instead of by a worker; one indexed-enough UPDATE on a small
        // table. Move it into a scheduled job if checkouts reach thousands a day.
        private Task<int> ExpireAbandonedAsync()
        {
            var cutoff = Now - AbandonedAfter;
            var now = Now;
            return _context.StorefrontCheckout
                .Where(c => c.Status == (int)StorefrontCheckoutStatus.AwaitingPayment && c.CreatedAt < cutoff)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(c => c.Status, (int)StorefrontCheckoutStatus.Expired)
                    .SetProperty(c => c.UpdatedAt, now));
        }

        private async Task<StorefrontCheckout> ExpireAsync(StorefrontCheckout checkout)
        {
            if (checkout.Status == (int)StorefrontCheckoutStatus.AwaitingPayment && checkout.PixExpiresAt is { } expiry && expiry <= Now)
            {
                checkout.Status = (int)StorefrontCheckoutStatus.Expired;
                checkout.UpdatedAt = Now;
                await _context.SaveChangesAsync();
            }

            return checkout;
        }

        #endregion

        #region Purchases

        public async Task<IReadOnlyList<StorefrontPurchaseDTO>> ListPurchasesAsync(Guid shopperId)
        {
            var ids = await _context.StorefrontCheckout.AsNoTracking()
                .Where(c => c.ShopperId == shopperId && (c.Status == (int)StorefrontCheckoutStatus.Paid || c.Status == (int)StorefrontCheckoutStatus.Settling))
                .OrderByDescending(c => c.PaidAt)
                .Select(c => c.Id)
                .ToListAsync();

            // ponytail: one read per purchase; batch them if an account ever holds hundreds.
            var purchases = new List<StorefrontPurchaseDTO>();
            foreach (var id in ids)
            {
                if (await GetPurchaseAsync(id) is { } purchase)
                    purchases.Add(purchase);
            }

            return purchases;
        }

        public async Task<IReadOnlyList<StorefrontPurchaseContactDTO>> ListPurchaseContactsAsync(Guid shopperId) =>
            (await _context.StorefrontCheckout.AsNoTracking()
                .Where(c => c.ShopperId == shopperId)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync())
            .Select(c => new StorefrontPurchaseContactDTO(c.Id, Read<StorefrontCustomerDTO>(c.CustomerJson), Read<StorefrontAddressDTO>(c.AddressJson)))
            .ToList();

        public async Task<StorefrontPurchaseDTO?> GetPurchaseAsync(Guid checkoutId)
        {
            var checkout = await _context.StorefrontCheckout.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == checkoutId && (c.Status == (int)StorefrontCheckoutStatus.Paid || c.Status == (int)StorefrontCheckoutStatus.Settling));
            if (checkout == null)
                return null;

            var orders = await StorefrontOrders(checkout.Id).ToListAsync();
            var invoices = await InvoiceKeysAsync(orders.Select(o => o.Id).ToList());
            var views = orders
                .Select(order => ToOrderDto(checkout, order, invoices.GetValueOrDefault(order.Id)))
                .OrderBy(view => view.OrderNumber)
                .ToList();

            var refunds = Read<List<StorefrontOutcomeDTO>>(checkout.OutcomesJson)
                .Where(o => o.Outcome == StorefrontOutcomeKind.RefundedOutOfStock)
                .ToList();

            return new StorefrontPurchaseDTO(checkout.Id, checkout.PaidAt ?? checkout.CreatedAt, checkout.TotalValue,
                (StorefrontPaymentMethod)checkout.PaymentMethod, views, refunds, InstallmentOf(checkout));
        }

        public async Task<bool> AttachShopperAsync(Guid checkoutId, Guid shopperId)
        {
            var checkout = await _context.StorefrontCheckout.FirstOrDefaultAsync(c => c.Id == checkoutId && c.ShopperId == null);
            if (checkout == null)
                return false;

            checkout.ShopperId = shopperId;
            checkout.UpdatedAt = Now;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> AttachGuestPurchasesAsync(string email, Guid shopperId)
        {
            var normalised = email.Trim().ToLowerInvariant();
            var checkouts = await _context.StorefrontCheckout
                .Where(c => c.ShopperId == null && (c.Status == (int)StorefrontCheckoutStatus.Paid || c.Status == (int)StorefrontCheckoutStatus.Settling) && c.Email == normalised)
                .ToListAsync();

            foreach (var checkout in checkouts)
            {
                checkout.ShopperId = shopperId;
                checkout.UpdatedAt = Now;
            }

            await _context.SaveChangesAsync();
            return checkouts.Count;
        }

        /// <summary>The account is gone; its purchases stay with the stores (fiscal retention), linked to no one (spec §5).</summary>
        public async Task DetachShopperAsync(Guid shopperId)
        {
            var shopperKey = shopperId.ToString();
            foreach (var checkout in await _context.StorefrontCheckout.Where(c => c.ShopperId == shopperId).ToListAsync())
            {
                checkout.ShopperId = null;
                checkout.UpdatedAt = Now;
            }

            foreach (var customer in await _context.Customer.Where(c => c.ExternalShopperId == shopperKey).ToListAsync())
            {
                customer.ExternalShopperId = null;
                customer.UpdatedAt = Now;
            }

            await _context.SaveChangesAsync();
        }

        public Task<Guid?> OwnerOfAsync(Guid checkoutId) =>
            _context.StorefrontCheckout.AsNoTracking()
                .Where(c => c.Id == checkoutId)
                .Select(c => c.ShopperId)
                .FirstOrDefaultAsync();

        /// <summary>
        /// The 7-day withdrawal (CDC art. 49, Decreto 7.962 art. 5): recorded at once, with a protocol,
        /// and the store told through its notification bell.
        /// </summary>
        public async Task<StorefrontOrderDTO?> RequestWithdrawalAsync(Guid checkoutId, string orderNumber, Guid actorUserId)
        {
            var (checkout, order) = await FindOrderAsync(checkoutId, orderNumber);
            if (checkout == null || order == null)
                return null;

            if (!CanWithdraw(order))
                return await OrderViewAsync(checkout, order);

            // The guard is in the UPDATE itself: two clicks at once record one withdrawal and one notification.
            var now = Now;
            var claimed = await _context.Order
                .Where(o => o.Id == order.Id && o.WithdrawalRequestedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(o => o.WithdrawalRequestedAt, now).SetProperty(o => o.UpdatedAt, now));
            order.WithdrawalRequestedAt ??= now;
            if (claimed == 0)
                return await OrderViewAsync(checkout, await _context.Order.AsNoTracking().Include(o => o.Enterprise).FirstAsync(o => o.Id == order.Id));

            _context.Notification.Add(new Notification
            {
                Id = Guid.NewGuid(),
                EnterpriseId = order.EnterpriseId!.Value,
                Title = $"Desistência do pedido {OrderNumber(order.Id)} (Limen)",
                Body = $"O cliente pediu a desistência da compra (CDC art. 49), protocolo {WithdrawalProtocol(order.Id)}. " +
                       "Envie as instruções de devolução e estorne o pedido quando o produto voltar.",
                Severity = EnumNotificationSeverity.Warning,
                CreatedByUserId = actorUserId,
                CreatedAt = Now,
                IsActive = true
            });
            await _context.SaveChangesAsync();

            return await OrderViewAsync(checkout, order);
        }

        /// <summary>Test mode only: moves an order along as its store would, through <c>OrderService.Update</c>.</summary>
        public async Task<StorefrontOrderDTO?> SimulateStepAsync(Guid checkoutId, string orderNumber, StorefrontSimulatedOrderStep step)
        {
            if (!SimulationEnabled)
                return null;

            var (checkout, order) = await FindOrderAsync(checkoutId, orderNumber);
            if (checkout == null || order == null)
                return null;

            if (order.Status is (int)EnumOrderStatus.Refund or (int)EnumOrderStatus.Finished)
                return await OrderViewAsync(checkout, order);

            var number = OrderNumber(order.Id);
            var tracking = $"LM{number[3..]}BR";
            var payload = new Order
            {
                Id = order.Id,
                EnterpriseId = order.EnterpriseId,
                Status = step switch
                {
                    StorefrontSimulatedOrderStep.Processing => (int)EnumOrderStatus.Processing,
                    StorefrontSimulatedOrderStep.Shipped => (int)EnumOrderStatus.Shipped,
                    _ => (int)EnumOrderStatus.Delivered
                },
                TrackingCode = step == StorefrontSimulatedOrderStep.Shipped ? tracking : null,
                TrackingUrl = step == StorefrontSimulatedOrderStep.Shipped
                    ? $"https://rastreamento.correios.com.br/app/index.php?objeto={tracking}"
                    : null,
                PaymentScheduledDate = default
            };

            _context.ChangeTracker.Clear();
            _orderService.Update(payload, order.EnterpriseId!.Value);
            _context.ChangeTracker.Clear();

            var updated = await _context.Order.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            return await OrderViewAsync(checkout, updated);
        }

        public async Task<StorefrontInvoiceDTO?> GetInvoiceAsync(Guid checkoutId, string orderNumber)
        {
            var (checkout, order) = await FindOrderAsync(checkoutId, orderNumber);
            if (checkout == null || order == null ||
                checkout.Status is not ((int)StorefrontCheckoutStatus.Paid or (int)StorefrontCheckoutStatus.Settling))
                return null;

            var document = await AuthorizedInvoices(order.Id).FirstOrDefaultAsync();
            return document == null || string.IsNullOrWhiteSpace(document.XmlContent)
                ? null
                : new StorefrontInvoiceDTO(document.AccessKey, Encoding.UTF8.GetBytes(document.XmlContent));
        }

        private IQueryable<Order> StorefrontOrders(Guid checkoutId)
        {
            var reference = checkoutId.ToString();
            return _context.Order
                .Include(o => o.Enterprise)
                .Where(o => o.ExternalReference == reference && o.Channel == (int)EnumOrderChannel.Storefront && o.IsActive == true);
        }

        private async Task<(StorefrontCheckout? Checkout, Order? Order)> FindOrderAsync(Guid checkoutId, string orderNumber)
        {
            var checkout = await _context.StorefrontCheckout.AsNoTracking().FirstOrDefaultAsync(c => c.Id == checkoutId);
            if (checkout == null)
                return (null, null);

            var orders = await StorefrontOrders(checkoutId).ToListAsync();
            return (checkout, orders.FirstOrDefault(o => OrderNumber(o.Id) == orderNumber));
        }

        private IQueryable<NFeDocument> AuthorizedInvoices(Guid orderId) =>
            _context.NFeDocument.AsNoTracking()
                .Where(d => d.OrderId == orderId && d.Status == NFeStatus.Authorized && d.IsActive == true)
                .OrderByDescending(d => d.AuthorizedAtUtc);

        private async Task<Dictionary<Guid, string>> InvoiceKeysAsync(IReadOnlyCollection<Guid> orderIds) =>
            (await _context.NFeDocument.AsNoTracking()
                .Where(d => orderIds.Contains(d.OrderId) && d.Status == NFeStatus.Authorized && d.IsActive == true)
                .Select(d => new { d.OrderId, d.AccessKey, d.AuthorizedAtUtc })
                .ToListAsync())
            .GroupBy(d => d.OrderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.AuthorizedAtUtc).First().AccessKey);

        private async Task<StorefrontOrderDTO> OrderViewAsync(StorefrontCheckout checkout, Order order)
        {
            order.Enterprise ??= await _context.Enterprise.AsNoTracking().FirstAsync(e => e.Id == order.EnterpriseId);
            var invoice = await AuthorizedInvoices(order.Id).Select(d => d.AccessKey).FirstOrDefaultAsync();
            return ToOrderDto(checkout, order, invoice);
        }

        private StorefrontOrderDTO ToOrderDto(StorefrontCheckout checkout, Order order, string? invoiceKey)
        {
            var number = OrderNumber(order.Id);
            var outcome = Read<List<StorefrontOutcomeDTO>>(checkout.OutcomesJson).FirstOrDefault(o => o.OrderNumber == number);
            var store = Read<StorefrontQuoteDTO>(checkout.QuoteJson).Stores.FirstOrDefault(s => s.Slug == order.Enterprise?.StorefrontSlug);
            var address = Read<StorefrontAddressDTO>(checkout.AddressJson);

            return new StorefrontOrderDTO(
                number,
                checkout.Id,
                order.Enterprise?.StorefrontSlug ?? outcome?.Slug ?? "",
                order.Enterprise?.Name ?? outcome?.Name ?? "",
                order.Enterprise?.Email ?? "",
                ShopperStatus(order.Status),
                outcome?.Lines ?? store?.Lines ?? [],
                outcome?.Amount ?? order.TotalValue,
                outcome?.DeliveryDaysMin ?? store?.DeliveryDaysMin ?? 0,
                outcome?.DeliveryDaysMax ?? store?.DeliveryDaysMax ?? 0,
                (StorefrontPaymentMethod)checkout.PaymentMethod,
                address.City,
                address.Uf,
                checkout.PaidAt ?? order.CreatedAt,
                order.ShippedAt,
                order.DeliveredAt,
                order.TrackingCode,
                order.TrackingUrl,
                order.WithdrawalRequestedAt,
                order.WithdrawalRequestedAt == null ? null : WithdrawalProtocol(order.Id),
                CanWithdraw(order),
                invoiceKey);
        }

        /// <summary>Seven days from delivery; before delivery the shopper may always cancel (CDC art. 49).</summary>
        private bool CanWithdraw(Order order) =>
            order.Status != (int)EnumOrderStatus.Refund &&
            order.WithdrawalRequestedAt == null &&
            (order.DeliveredAt == null || Now <= order.DeliveredAt.Value + WithdrawalWindow);

        public static StorefrontOrderStatus ShopperStatus(int status) => (EnumOrderStatus)status switch
        {
            EnumOrderStatus.Processing => StorefrontOrderStatus.Processing,
            EnumOrderStatus.Shipped => StorefrontOrderStatus.Shipped,
            EnumOrderStatus.Delivered or EnumOrderStatus.Finished => StorefrontOrderStatus.Delivered,
            EnumOrderStatus.Refund => StorefrontOrderStatus.Refunded,
            _ => StorefrontOrderStatus.Paid
        };

        #endregion

        public static string OrderNumber(Guid orderId) => StorefrontOrderNumber.For(orderId);

        public static string WithdrawalProtocol(Guid orderId) => "DS-" + orderId.ToString("N")[..8].ToUpperInvariant();

        private static StorefrontOutcomeDTO Outcome(StorefrontQuoteStoreDTO store, StorefrontOutcomeKind kind, string? orderNumber, string contactEmail) =>
            new(store.Slug, store.Name, kind, orderNumber, store.Subtotal + store.Shipping, store.DeliveryDaysMin, store.DeliveryDaysMax, contactEmail, store.Lines);

        private StorefrontCheckoutDTO ToDto(StorefrontCheckout checkout) => new(
            checkout.Id,
            (StorefrontCheckoutStatus)checkout.Status,
            (StorefrontPaymentMethod)checkout.PaymentMethod,
            checkout.TotalValue,
            checkout.Email,
            checkout.CreatedAt,
            checkout.PixCode == null ? null : new StorefrontPixDTO(checkout.PixCode, checkout.PixExpiresAt ?? checkout.CreatedAt),
            checkout.DeclineCode,
            Read<StorefrontQuoteDTO>(checkout.QuoteJson),
            Read<List<StorefrontOutcomeDTO>>(checkout.OutcomesJson),
            checkout.Simulated,
            Read<StorefrontCustomerDTO>(checkout.CustomerJson).FullName,
            InstallmentOf(checkout));

        /// <summary>The card plan the shopper chose; null for Pix.</summary>
        private static StorefrontInstallmentDTO? InstallmentOf(StorefrontCheckout checkout) =>
            checkout.PaymentMethod != (int)StorefrontPaymentMethod.Card
                ? null
                : new StorefrontInstallmentDTO(checkout.Installments, checkout.InstallmentValue ?? checkout.TotalValue,
                    checkout.TotalWithInterest ?? checkout.TotalValue, checkout.InterestMonthly ?? 0, checkout.CetAnnual ?? 0);

        private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json)!;

        private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

        /// <summary>A code shaped like a Pix copy-and-paste string and plainly marked as a test; no bank will accept it.</summary>
        private static (string Code, DateTime ExpiresAt) NewPix(Guid id, decimal total, DateTime now) =>
            ($"00020126580014BR.GOV.BCB.PIX0136LIMEN-TESTE-{id.ToString("N")[..12].ToUpperInvariant()}5204000053039865406{total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}5802BR5905LIMEN6009SAO PAULO6304TEST",
                now + PixLifetime);
    }
}
