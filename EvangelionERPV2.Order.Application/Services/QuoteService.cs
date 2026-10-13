using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.OrderModule.Domain.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Serilog;

namespace EvangelionERPV2.OrderModule.Application.Services
{
    /// <summary>
    /// Orçamentos: what a sale would cost, and the one door from there to a real order.
    /// </summary>
    public class QuoteService : IQuoteService
    {
        private const int DefaultValidityInDays = 15;
        private const int DefaultPaymentTermInDays = 30;

        private readonly IQuoteRepository _quoteRepository;
        private readonly IRepository<QuoteItem> _quoteItemRepository;
        private readonly IRepository<Product> _productRepository;
        private readonly IRepository<Customer> _customerRepository;
        private readonly IOrderService<Order> _orderService;

        public QuoteService(
            IQuoteRepository quoteRepository,
            IRepository<QuoteItem> quoteItemRepository,
            IRepository<Product> productRepository,
            IRepository<Customer> customerRepository,
            IOrderService<Order> orderService)
        {
            _quoteRepository = quoteRepository;
            _quoteItemRepository = quoteItemRepository;
            _productRepository = productRepository;
            _customerRepository = customerRepository;
            _orderService = orderService;
        }

        public async Task<IReadOnlyList<Quote>> GetByEnterpriseAsync(Guid enterpriseId, int? status, CancellationToken cancellation = default)
        {
            if (enterpriseId == Guid.Empty)
                return [];

            return await _quoteRepository.GetByEnterpriseAsync(enterpriseId, status, cancellation);
        }

        public Task<Quote?> GetByIdAsync(Guid id, Guid enterpriseId, CancellationToken cancellation = default)
        {
            return enterpriseId == Guid.Empty || id == Guid.Empty
                ? Task.FromResult<Quote?>(null)
                : _quoteRepository.GetWithItemsAsync(id, enterpriseId, cancellation);
        }

        /// <summary>
        /// The quote as a PDF, or <c>null</c> when there is no such quote for this enterprise.
        /// </summary>
        public async Task<(byte[] Pdf, long Number)?> GetPdfAsync(Guid id, Guid enterpriseId, CancellationToken cancellation = default)
        {
            var quote = await GetByIdAsync(id, enterpriseId, cancellation);
            if (quote == null)
                return null;

            var pdf = await HtmlPdfRenderer.RenderPdfAsync(QuoteDocumentBuilder.BuildHtml(quote, DateTime.UtcNow));
            return (pdf, quote.Number);
        }

        public async Task<Quote> CreateAsync(Guid enterpriseId, UpsertQuoteRequestDTO request, CancellationToken cancellation = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (enterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Enterprise is required.");

            var items = await ResolveItemsAsync(enterpriseId, request.Items);
            await EnsureCustomerBelongsToEnterpriseAsync(request.CustomerId, enterpriseId);

            var quote = new Quote
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                CustomerId = request.CustomerId,
                Number = await _quoteRepository.AllocateNextNumberAsync(enterpriseId, cancellation),
                Status = (int)EnumQuoteStatus.Draft,
                IssuedAt = DateTime.UtcNow,
                ValidUntil = (request.ValidUntil ?? DateTime.UtcNow.AddDays(DefaultValidityInDays)).Date,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                TotalValue = items.Sum(x => x.LineAmount),
                IsActive = true
            };

            foreach (var item in items)
                item.QuoteId = quote.Id;

            await _quoteRepository.CreateAsync(quote);
            await _quoteItemRepository.CreateRangeAsync(items);
            await _quoteRepository.CommitAsync(cancellation);

            Log.Logger.Information("Quote {Number} created. QuoteId={QuoteId}", quote.Number, quote.Id);

            return await _quoteRepository.GetWithItemsAsync(quote.Id, enterpriseId, cancellation) ?? quote;
        }

        /// <summary>
        /// Replaces a draft's items and header. Only a draft: once a quote has been sent, its lines
        /// are what the customer is holding, and editing them under their feet is how a dispute
        /// starts. Withdraw it and issue another instead.
        /// </summary>
        public async Task<Quote> UpdateAsync(Guid id, Guid enterpriseId, UpsertQuoteRequestDTO request, CancellationToken cancellation = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var existing = await _quoteRepository.GetWithItemsAsync(id, enterpriseId, cancellation)
                ?? throw new NotFoundDatabaseException($"{nameof(Quote)} was not found in database.");

            if (existing.Status != (int)EnumQuoteStatus.Draft)
                throw new InsertDatabaseException("Only a draft quote can be edited.");

            var items = await ResolveItemsAsync(enterpriseId, request.Items);
            await EnsureCustomerBelongsToEnterpriseAsync(request.CustomerId, enterpriseId);
            foreach (var item in items)
                item.QuoteId = existing.Id;

            var previousItems = (await _quoteItemRepository.GetAllAsync(x => x.QuoteId == existing.Id)).ToList();
            if (previousItems.Count > 0)
                _quoteItemRepository.DeleteRange(previousItems);

            existing.CustomerId = request.CustomerId;
            existing.ValidUntil = (request.ValidUntil ?? existing.ValidUntil).Date;
            existing.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            existing.TotalValue = items.Sum(x => x.LineAmount);
            existing.UpdatedAt = DateTime.UtcNow;
            existing.Items = null;

            _quoteRepository.Update(existing);
            await _quoteItemRepository.CreateRangeAsync(items);
            await _quoteRepository.CommitAsync(cancellation);

            return await _quoteRepository.GetWithItemsAsync(existing.Id, enterpriseId, cancellation) ?? existing;
        }

        /// <summary>Sent, accepted or rejected. Converted is reached only by converting.</summary>
        public async Task<Quote> SetStatusAsync(Guid id, Guid enterpriseId, EnumQuoteStatus status, CancellationToken cancellation = default)
        {
            if (status is not (EnumQuoteStatus.Sent or EnumQuoteStatus.Accepted or EnumQuoteStatus.Rejected))
                throw new InsertDatabaseException("A quote can only be marked as sent, accepted or rejected.");

            var existing = await _quoteRepository.GetWithItemsAsync(id, enterpriseId, cancellation)
                ?? throw new NotFoundDatabaseException($"{nameof(Quote)} was not found in database.");

            var effective = existing.StatusAsOf(DateTime.UtcNow);
            if (effective is EnumQuoteStatus.Converted or EnumQuoteStatus.Converting or EnumQuoteStatus.Expired or EnumQuoteStatus.Rejected)
                throw new InsertDatabaseException("This quote is closed and cannot change status.");

            existing.Status = (int)status;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.Items = null;

            _quoteRepository.Update(existing);
            await _quoteRepository.CommitAsync(cancellation);

            return existing;
        }

        /// <summary>
        /// Turns the quote into an order, once.
        /// </summary>
        public async Task<Quote> ConvertAsync(
            Guid id,
            Guid enterpriseId,
            ConvertQuoteRequestDTO request,
            bool allowCreditOverride = false,
            CancellationToken cancellation = default)
        {
            var (existing, previousStatus, items) = await ClaimConversionAsync(id, enterpriseId, cancellation);
            var order = new Order
            {
                EnterpriseId = enterpriseId,
                CustomerId = existing.CustomerId,
                PaymentScheduledDate = (request?.PaymentScheduledDate ?? DateTime.UtcNow.AddDays(DefaultPaymentTermInDays)).Date,
                TotalValue = existing.TotalValue,
                Status = (int)EnumOrderStatus.Pending,
                IsActive = true,
                OrderedProduct = items.Select(item => new OrderedProduct
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    Value = item.UnitValue,
                    UnitOfMeasure = item.UnitOfMeasure,
                    IsActive = true
                }).ToList()
            };

            Order createdOrder;
            try
            {
                createdOrder = await _orderService.CreateAsync(order, allowCreditOverride);
            }
            catch
            {
                existing.Status = previousStatus;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.Items = null;
                _quoteRepository.Update(existing);
                await _quoteRepository.CommitAsync(CancellationToken.None);
                throw;
            }

            existing.ConvertedOrderId = createdOrder.Id;
            existing.Status = (int)EnumQuoteStatus.Converted;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.Items = null;

            _quoteRepository.Update(existing);
            await _quoteRepository.CommitAsync(cancellation);

            Log.Logger.Information(
                "Quote {Number} converted. QuoteId={QuoteId} OrderId={OrderId}", existing.Number, existing.Id, createdOrder.Id);

            return existing;
        }

        private async Task<(Quote Quote, int PreviousStatus, List<QuoteItem> Items)> ClaimConversionAsync(
            Guid id, Guid enterpriseId, CancellationToken cancellation)
        {
            Quote? claimed = null;
            var previousStatus = 0;
            List<QuoteItem> items = [];

            await _quoteRepository.ExecuteInTransactionAsync(async () =>
            {
                await _quoteRepository.LockForUpdateAsync(id, cancellation);
                claimed = await _quoteRepository.GetWithItemsAsync(id, enterpriseId, cancellation)
                    ?? throw new NotFoundDatabaseException($"{nameof(Quote)} was not found in database.");

                if (claimed.ConvertedOrderId.HasValue)
                    throw new InsertDatabaseException("This quote has already been converted.");

                var effective = claimed.StatusAsOf(DateTime.UtcNow);
                if (effective == EnumQuoteStatus.Expired)
                    throw new InsertDatabaseException("This quote has expired and cannot be converted.");

                if (effective is EnumQuoteStatus.Rejected or EnumQuoteStatus.Converted or EnumQuoteStatus.Converting)
                    throw new InsertDatabaseException("This quote is closed or already being converted.");

                items = claimed.Items?.ToList() ?? [];
                if (items.Count == 0)
                    throw new InsertDatabaseException("A quote with no items cannot be converted.");

                previousStatus = claimed.Status;
                claimed.Status = (int)EnumQuoteStatus.Converting;
                claimed.UpdatedAt = DateTime.UtcNow;
                claimed.Items = null;
                _quoteRepository.Update(claimed);
                await _quoteRepository.CommitAsync(cancellation);
            }, cancellation);

            return (claimed!, previousStatus, items);
        }

        public async Task<Quote> DeleteAsync(Guid id, Guid enterpriseId, CancellationToken cancellation = default)
        {
            var existing = await _quoteRepository.GetWithItemsAsync(id, enterpriseId, cancellation)
                ?? throw new NotFoundDatabaseException($"{nameof(Quote)} was not found in database.");

            if (existing.ConvertedOrderId.HasValue)
                throw new InsertDatabaseException("A converted quote cannot be deleted; delete the order it became.");

            existing.IsActive = false;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.Items = null;

            _quoteRepository.Update(existing);
            await _quoteRepository.CommitAsync(cancellation);

            return existing;
        }

        /// <summary>
        /// Item lines, priced and unit-locked from the catalogue.
        /// </summary>
        private async Task<List<QuoteItem>> ResolveItemsAsync(Guid enterpriseId, IEnumerable<QuoteItemRequestDTO>? requested)
        {
            var lines = requested?.ToList() ?? [];
            if (lines.Count == 0)
                throw new InsertDatabaseException("A quote needs at least one item.");

            if (lines.Any(x => x.Quantity <= 0))
                throw new InsertDatabaseException("Each quote item must have quantity greater than zero.");

            var productIds = lines.Select(x => x.ProductId).Distinct().ToList();
            if (productIds.Count != lines.Count)
                throw new InsertDatabaseException("The quote has duplicate products.");

            var products = (await _productRepository.GetPagedAsync(
                    x => x.EnterpriseId == enterpriseId && x.IsActive == true && productIds.Contains(x.Id)))
                .Items
                .ToDictionary(x => x.Id, x => x);

            if (products.Count != productIds.Count)
                throw new InsertDatabaseException("Some quote products were not found in enterprise inventory.");

            var now = DateTime.UtcNow;

            return lines.Select(line =>
            {
                var product = products[line.ProductId];
                var unitValue = line.UnitValue > 0 ? line.UnitValue : Math.Max(0, product.DefaultValue);

                return new QuoteItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    Quantity = line.Quantity,
                    UnitValue = unitValue,
                    LineAmount = Math.Round(line.Quantity * unitValue, 2, MidpointRounding.AwayFromZero),
                    UnitOfMeasure = product.UnitOfMeasure,
                    CreatedAt = now,
                    UpdatedAt = now,
                    IsActive = true
                };
            }).ToList();
        }

        private async Task EnsureCustomerBelongsToEnterpriseAsync(Guid? customerId, Guid enterpriseId)
        {
            if (!customerId.HasValue)
                return;

            var customer = (await _customerRepository.GetPagedAsync(
                    item => item.Id == customerId.Value
                        && item.EnterpriseId == enterpriseId
                        && item.IsActive == true,
                    pageNumber: 1,
                    pageSize: 1))
                .Items
                .FirstOrDefault();

            if (customer == null)
                throw new InsertDatabaseException("The quote customer was not found in this enterprise.");
        }
    }
}
