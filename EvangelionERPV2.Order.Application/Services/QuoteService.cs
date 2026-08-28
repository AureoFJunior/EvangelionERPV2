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
    /// <remarks>
    /// <para>
    /// Nothing here reserves stock, moves a balance or appears in a financial read. That is the
    /// entire design: a quote is a promise, and the ERP counts money only once the promise becomes
    /// an <see cref="Order"/> through <see cref="ConvertAsync"/>.
    /// </para>
    /// <para>
    /// Conversion deliberately goes through <c>IOrderService.CreateAsync</c> rather than writing an
    /// Order itself, so a converted quote passes every gate a typed order does — stock, the
    /// enterprise balance, the credit limit and the period lock — and fails the same way when one
    /// of them says no.
    /// </para>
    /// </remarks>
    public class QuoteService : IQuoteService
    {
        private const int DefaultValidityInDays = 15;
        private const int DefaultPaymentTermInDays = 30;

        private readonly IQuoteRepository _quoteRepository;
        private readonly IRepository<QuoteItem> _quoteItemRepository;
        private readonly IRepository<Product> _productRepository;
        private readonly IOrderService<Order> _orderService;

        public QuoteService(
            IQuoteRepository quoteRepository,
            IRepository<QuoteItem> quoteItemRepository,
            IRepository<Product> productRepository,
            IOrderService<Order> orderService)
        {
            _quoteRepository = quoteRepository;
            _quoteItemRepository = quoteItemRepository;
            _productRepository = productRepository;
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
        /// <remarks>
        /// A renderer failure is deliberately <em>not</em> swallowed. The bill mailer swallows one
        /// because the digitable line in the body is payable without the attachment; here the PDF
        /// is the entire response, and answering 200 with nothing would be a quote the salesperson
        /// believes was sent.
        /// </remarks>
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
            if (effective is EnumQuoteStatus.Converted or EnumQuoteStatus.Expired or EnumQuoteStatus.Rejected)
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
        /// <remarks>
        /// The order carries the <em>quoted</em> unit values, not today's price list: the customer
        /// is charged what they were promised. <c>ConvertedOrderId</c> is written after the order
        /// exists, and a filtered unique index on it means a double click cannot produce two orders
        /// — the second attempt finds the quote already converted and is refused.
        /// </remarks>
        public async Task<Quote> ConvertAsync(
            Guid id,
            Guid enterpriseId,
            ConvertQuoteRequestDTO request,
            bool allowCreditOverride = false,
            CancellationToken cancellation = default)
        {
            var existing = await _quoteRepository.GetWithItemsAsync(id, enterpriseId, cancellation)
                ?? throw new NotFoundDatabaseException($"{nameof(Quote)} was not found in database.");

            if (existing.ConvertedOrderId.HasValue)
                throw new InsertDatabaseException("This quote has already been converted.");

            var effective = existing.StatusAsOf(DateTime.UtcNow);
            if (effective == EnumQuoteStatus.Expired)
                throw new InsertDatabaseException("This quote has expired and cannot be converted.");

            if (effective is EnumQuoteStatus.Rejected or EnumQuoteStatus.Converted)
                throw new InsertDatabaseException("This quote is closed and cannot be converted.");

            var items = existing.Items?.ToList() ?? [];
            if (items.Count == 0)
                throw new InsertDatabaseException("A quote with no items cannot be converted.");

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

            // Every gate a typed order passes — stock, balance, credit limit, period lock — applies
            // here too, and its refusal propagates rather than being swallowed into a half-converted
            // quote. Nothing is written to the quote until the order exists.
            var createdOrder = await _orderService.CreateAsync(order, allowCreditOverride);

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
        /// <remarks>
        /// The unit of measure comes from the product and never from the request, the same rule
        /// <c>PayableBillService.NormalizeItemsAsync</c> follows: a line that says "12 boxes" when
        /// the catalogue says "12 units" is a delivery argument waiting to happen.
        /// </remarks>
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

            // GetPagedAsync, not GetAllAsync: ProductRepository's override drags the whole product
            // graph across and this needs four columns.
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
    }
}
