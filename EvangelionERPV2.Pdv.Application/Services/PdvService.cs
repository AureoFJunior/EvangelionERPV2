using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.PdvModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace EvangelionERPV2.PdvModule.Application.Services
{
    public class PdvService : IPdvService
    {
        private readonly AppDbContext _appDbContext;
        private readonly IRepository<CashSession> _sessionRepository;
        private readonly IRepository<CashMovement> _movementRepository;
        private readonly IRepository<PdvSale> _saleRepository;
        private readonly IRepository<PdvSalePayment> _paymentRepository;
        private readonly IOrderService<Order> _orderService;
        private readonly IProductService<Product> _productService;

        public PdvService(
            AppDbContext appDbContext,
            IRepository<CashSession> sessionRepository,
            IRepository<CashMovement> movementRepository,
            IRepository<PdvSale> saleRepository,
            IRepository<PdvSalePayment> paymentRepository,
            IOrderService<Order> orderService,
            IProductService<Product> productService)
        {
            _appDbContext = appDbContext;
            _sessionRepository = sessionRepository;
            _movementRepository = movementRepository;
            _saleRepository = saleRepository;
            _paymentRepository = paymentRepository;
            _orderService = orderService;
            _productService = productService;
        }

        #region Sessions

        public async Task<CashSession> OpenSessionAsync(OpenCashSessionRequestDTO request, Guid enterpriseId, Guid userId)
        {
            ArgumentNullException.ThrowIfNull(request);
            AssertTenant(enterpriseId);

            if (request.ClientSessionId == Guid.Empty)
                throw new ArgumentException("A client session id is required.");

            if (string.IsNullOrWhiteSpace(request.TerminalId))
                throw new ArgumentException("A terminal id is required.");

            if (request.OpeningFloat < 0)
                throw new ArgumentException("An opening float cannot be negative.");

            // Idempotent by the terminal's own id: a re-sent open returns the session it already
            // created rather than opening a second one for the same shift.
            var existing = await _appDbContext.CashSession
                .FirstOrDefaultAsync(session =>
                    session.EnterpriseId == enterpriseId && session.ClientSessionId == request.ClientSessionId);

            if (existing != null)
                return existing;

            var opened = new CashSession
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                ClientSessionId = request.ClientSessionId,
                TerminalId = request.TerminalId.Trim(),
                UserId = userId,
                OpenedAt = request.OpenedAt == default ? DateTime.UtcNow : request.OpenedAt,
                OpeningFloat = request.OpeningFloat,
                Status = (int)EnumCashSessionStatus.Open,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            await _sessionRepository.ExecuteInTransactionAsync(async () =>
            {
                await _sessionRepository.CreateAsync(opened);

                // The float is a movement like any other, so expected cash is the ledger and
                // nothing but the ledger.
                await _movementRepository.CreateAsync(new CashMovement
                {
                    Id = Guid.NewGuid(),
                    ClientMovementId = Guid.NewGuid(),
                    CashSessionId = opened.Id,
                    EnterpriseId = enterpriseId,
                    Type = (int)EnumCashMovementType.Opening,
                    Amount = opened.OpeningFloat,
                    UserId = userId,
                    CreatedAt = opened.OpenedAt,
                    IsActive = true
                });

                await _sessionRepository.CommitAsync();
            });

            Log.Logger.Information("PDV session [{SessionId}] opened on terminal [{TerminalId}].", opened.Id, opened.TerminalId);
            return opened;
        }

        public async Task<CashMovement> RegisterMovementAsync(CashMovementRequestDTO request, Guid enterpriseId, Guid userId)
        {
            ArgumentNullException.ThrowIfNull(request);
            AssertTenant(enterpriseId);

            var type = (EnumCashMovementType)request.Type;

            // Only these two are typed by a human. Letting a client post a Sale movement would let
            // an operator inflate the drawer to cover a shortfall.
            if (type is not (EnumCashMovementType.Sangria or EnumCashMovementType.Suprimento))
                throw new ArgumentException("Only a sangria or a suprimento may be registered by hand.");

            if (request.Amount <= 0)
                throw new ArgumentException("A movement must be positive.");

            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new ArgumentException("A reason is required.");

            var existing = await _appDbContext.CashMovement
                .FirstOrDefaultAsync(movement =>
                    movement.EnterpriseId == enterpriseId && movement.ClientMovementId == request.ClientMovementId);

            if (existing != null)
                return existing;

            var session = await RequireOpenSessionAsync(request.ClientSessionId, enterpriseId);

            var movement = new CashMovement
            {
                Id = Guid.NewGuid(),
                ClientMovementId = request.ClientMovementId == Guid.Empty ? Guid.NewGuid() : request.ClientMovementId,
                CashSessionId = session.Id,
                EnterpriseId = enterpriseId,
                Type = (int)type,
                Amount = type == EnumCashMovementType.Sangria ? -request.Amount : request.Amount,
                Reason = request.Reason.Trim(),
                UserId = userId,
                CreatedAt = request.OccurredAt == default ? DateTime.UtcNow : request.OccurredAt,
                IsActive = true
            };

            await _movementRepository.CreateAsync(movement);
            await _movementRepository.CommitAsync();

            return movement;
        }

        public async Task<CashSessionSummaryDTO> CloseSessionAsync(CloseCashSessionRequestDTO request, Guid enterpriseId, Guid userId)
        {
            ArgumentNullException.ThrowIfNull(request);
            AssertTenant(enterpriseId);

            if (request.CountedAmount < 0)
                throw new ArgumentException("A count cannot be negative.");

            var session = await RequireOpenSessionAsync(request.ClientSessionId, enterpriseId);
            var summary = await BuildSummaryAsync(session, includeExpectedCash: true);

            var closedAt = request.ClosedAt == default ? DateTime.UtcNow : request.ClosedAt;

            // Stored rather than derived. A later correction to a movement must not silently rewrite
            // what the operator was held to on the night.
            session.CountedAmount = request.CountedAmount;
            session.ExpectedAmount = summary.ExpectedCash;
            session.Difference = decimal.Round(
                request.CountedAmount - (summary.ExpectedCash ?? 0m), 2, MidpointRounding.AwayFromZero);
            session.ClosedAt = closedAt;
            session.Status = (int)EnumCashSessionStatus.Closed;
            session.UpdatedAt = DateTime.UtcNow;

            await _sessionRepository.ExecuteInTransactionAsync(async () =>
            {
                _sessionRepository.Update(session);

                await _movementRepository.CreateAsync(new CashMovement
                {
                    Id = Guid.NewGuid(),
                    ClientMovementId = Guid.NewGuid(),
                    CashSessionId = session.Id,
                    EnterpriseId = enterpriseId,
                    Type = (int)EnumCashMovementType.Closing,
                    Amount = -request.CountedAmount,
                    UserId = userId,
                    CreatedAt = closedAt,
                    IsActive = true
                });

                await _sessionRepository.CommitAsync();
            });

            return await BuildSummaryAsync(session, includeExpectedCash: true);
        }

        public async Task<CashSessionSummaryDTO> GetSessionSummaryAsync(Guid clientSessionId, Guid enterpriseId, bool includeExpectedCash)
        {
            AssertTenant(enterpriseId);

            var session = await _appDbContext.CashSession
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.EnterpriseId == enterpriseId && item.ClientSessionId == clientSessionId)
                ?? throw new NotFoundDatabaseException("Cash session was not found.");

            return await BuildSummaryAsync(session, includeExpectedCash);
        }

        public async Task<CashSession?> GetOpenSessionAsync(string terminalId, Guid enterpriseId)
        {
            AssertTenant(enterpriseId);

            if (string.IsNullOrWhiteSpace(terminalId))
                return null;

            return await _appDbContext.CashSession
                .AsNoTracking()
                .Where(session => session.EnterpriseId == enterpriseId
                    && session.TerminalId == terminalId
                    && session.Status == (int)EnumCashSessionStatus.Open)
                .OrderByDescending(session => session.OpenedAt)
                .FirstOrDefaultAsync();
        }

        #endregion

        #region Sales

        /// <summary>
        /// Records a till sale as an <see cref="Order"/>, exactly once.
        /// </summary>
        /// <remarks>
        /// Write-ahead, in three commits, deliberately not one transaction. <c>OrderService.CreateAsync</c>
        /// opens its own transaction and calls <c>ChangeTracker.Clear()</c> on a retry, so wrapping it
        /// in another would either throw on the nested BeginTransaction or detach the rows written
        /// before it. The same shape the NFe module uses for the same reason.
        ///
        /// So the claim is committed first. A crash between the claim and the order leaves a Pending
        /// row, and the retry — which carries the same ClientSaleId — finds that row and finishes
        /// the job rather than selling the basket twice. The unique index is what makes the claim
        /// a claim: two terminals racing the same id, one of them loses at the database.
        /// </remarks>
        public async Task<PdvSaleSyncResultDTO> SyncSaleAsync(PdvSaleSyncRequestDTO request, Guid enterpriseId, Guid userId)
        {
            ArgumentNullException.ThrowIfNull(request);
            AssertTenant(enterpriseId);

            if (request.ClientSaleId == Guid.Empty)
                throw new ArgumentException("A client sale id is required.");

            var items = (request.Items ?? []).ToList();
            if (items.Count == 0)
                throw new ArgumentException("A sale must have at least one item.");

            var payments = (request.Payments ?? []).ToList();
            if (payments.Count == 0)
                throw new ArgumentException("A sale must have at least one payment.");

            var claimed = await ClaimAsync(request, enterpriseId, userId);

            // Already finished, by an earlier call or by another request that raced this one.
            if (claimed.Sale.Status == (int)EnumPdvSaleStatus.Synced || claimed.Sale.Status == (int)EnumPdvSaleStatus.Rejected)
                return Describe(claimed.Sale, wasAlreadyRecorded: true);

            var expectedTotal = items.Sum(item => decimal.Round(item.Value * item.Quantity, 2, MidpointRounding.AwayFromZero));
            var tendered = payments.Sum(payment => payment.Amount);

            // The books have to balance before anything else happens. Change given is not part of
            // it: a R$50 note against a R$43 basket is 43 tendered and 7 back.
            if (decimal.Round(tendered, 2, MidpointRounding.AwayFromZero) != expectedTotal)
            {
                return await RejectAsync(claimed.Sale,
                    $"Payments total {tendered:0.00} but the sale is worth {expectedTotal:0.00}.");
            }

            try
            {
                var order = await _orderService.CreateAsync(
                    BuildOrder(request, items, enterpriseId, userId),
                    allowCreditOverride: false,
                    allowNegativeStock: true);

                await CompleteAsync(claimed.Sale, order, payments, enterpriseId, userId);

                return Describe(claimed.Sale, wasAlreadyRecorded: false);
            }
            catch (PeriodClosedException exception)
            {
                // Final: the period will not reopen because a terminal asked again.
                return await RejectAsync(claimed.Sale, exception.Message);
            }
            catch (CreditLimitExceededException exception)
            {
                return await RejectAsync(claimed.Sale, exception.Message);
            }
            catch (Exception exception)
            {
                // Everything else stays Pending on purpose. The terminal will resend, the claim is
                // still here, and a human can see a sale that never resolved — which is far better
                // than a sale marked failed that actually went through.
                Log.Logger.Error("PDV sale [{ClientSaleId}] failed to sync. ErrorType={ErrorType}",
                    request.ClientSaleId, exception.GetType().Name);
                throw;
            }
        }

        /// <summary>
        /// Takes the idempotency claim, or hands back the one that already exists.
        /// </summary>
        private async Task<(PdvSale Sale, bool WasExisting)> ClaimAsync(
            PdvSaleSyncRequestDTO request, Guid enterpriseId, Guid userId)
        {
            var existing = await _appDbContext.PdvSale
                .FirstOrDefaultAsync(sale => sale.EnterpriseId == enterpriseId && sale.ClientSaleId == request.ClientSaleId);

            if (existing != null)
                return (existing, true);

            var session = await RequireSessionAsync(request.ClientSessionId, enterpriseId);

            var sale = new PdvSale
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                ClientSaleId = request.ClientSaleId,
                CashSessionId = session.Id,
                TerminalId = string.IsNullOrWhiteSpace(request.TerminalId) ? session.TerminalId : request.TerminalId.Trim(),
                UserId = userId,
                DiscountAmount = request.DiscountAmount,
                SurchargeAmount = request.SurchargeAmount,
                SoldAtLocal = request.SoldAtLocal == default ? DateTime.UtcNow : request.SoldAtLocal,
                Status = (int)EnumPdvSaleStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            try
            {
                await _saleRepository.CreateAsync(sale);
                await _saleRepository.CommitAsync();
                return (sale, false);
            }
            catch (DbUpdateException)
            {
                // Lost the race on the unique index. Whoever won is authoritative.
                _saleRepository.DetachEntity(sale);

                var winner = await _appDbContext.PdvSale
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.EnterpriseId == enterpriseId && item.ClientSaleId == request.ClientSaleId);

                // No winner means the write failed for some reason other than the idempotency
                // index, so the original exception is the honest answer rather than a made-up one.
                if (winner == null)
                    throw;

                return (winner, true);
            }
        }

        private Order BuildOrder(PdvSaleSyncRequestDTO request, List<PdvSaleItemRequestDTO> items, Guid enterpriseId, Guid userId)
        {
            var soldAt = request.SoldAtLocal == default ? DateTime.UtcNow : request.SoldAtLocal;

            return new Order
            {
                EnterpriseId = enterpriseId,
                CustomerId = request.CustomerId,
                UserId = userId,

                // A till sale is paid before the customer leaves, so it is Paid on arrival — not
                // Pending waiting on a receivable that does not exist.
                Status = (int)EnumOrderStatus.Paid,
                Payday = soldAt,
                PaymentScheduledDate = soldAt,
                OrderedProduct = items.Select(item => new OrderedProduct
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,

                    // The discount already lives in the unit price, because Order.TotalValue is
                    // Σ(Value × Quantity) and nothing may make that untrue.
                    Value = item.Value,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                }).ToList()
            };
        }

        private async Task CompleteAsync(PdvSale sale, Order order, List<PdvSalePaymentRequestDTO> payments,
            Guid enterpriseId, Guid userId)
        {
            sale.OrderId = order.Id;
            sale.Status = (int)EnumPdvSaleStatus.Synced;
            sale.SyncedAt = DateTime.UtcNow;
            sale.UpdatedAt = DateTime.UtcNow;

            await _saleRepository.ExecuteInTransactionAsync(async () =>
            {
                _saleRepository.Update(sale);

                await _paymentRepository.CreateRangeAsync(payments.Select(payment => new PdvSalePayment
                {
                    Id = Guid.NewGuid(),
                    PdvSaleId = sale.Id,
                    EnterpriseId = enterpriseId,
                    Method = payment.Method,
                    Amount = payment.Amount,
                    ChangeGiven = payment.ChangeGiven,
                    AuthorizationCode = payment.AuthorizationCode,
                    Installments = payment.Installments,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                }));

                // Only the cash leg reaches the drawer. A card tender is money received but not
                // money to count, and recording it would guarantee a difference at every close.
                var cash = payments
                    .Where(payment => payment.Method == (int)EnumPaymentMethod.Cash)
                    .Sum(payment => payment.Amount);

                if (cash > 0)
                {
                    await _movementRepository.CreateAsync(new CashMovement
                    {
                        Id = Guid.NewGuid(),
                        ClientMovementId = Guid.NewGuid(),
                        CashSessionId = sale.CashSessionId,
                        EnterpriseId = enterpriseId,
                        Type = (int)EnumCashMovementType.Sale,
                        Amount = cash,
                        UserId = userId,
                        ReferenceId = sale.Id,
                        CreatedAt = sale.SoldAtLocal,
                        IsActive = true
                    });
                }

                await _saleRepository.CommitAsync();
            });
        }

        private async Task<PdvSaleSyncResultDTO> RejectAsync(PdvSale sale, string reason)
        {
            sale.Status = (int)EnumPdvSaleStatus.Rejected;
            sale.RejectReason = reason.Length > 400 ? reason[..400] : reason;
            sale.UpdatedAt = DateTime.UtcNow;

            _saleRepository.Update(sale);
            await _saleRepository.CommitAsync();

            return Describe(sale, wasAlreadyRecorded: false);
        }

        private static PdvSaleSyncResultDTO Describe(PdvSale sale, bool wasAlreadyRecorded) => new()
        {
            ClientSaleId = sale.ClientSaleId,
            PdvSaleId = sale.Id,
            OrderId = sale.OrderId,
            Status = sale.Status,
            WasAlreadyRecorded = wasAlreadyRecorded,
            RejectReason = sale.RejectReason
        };

        #endregion

        #region Catalog

        public async Task<PdvCatalogPageDTO> GetCatalogAsync(DateTime? since, Guid enterpriseId, int pageSize)
        {
            AssertTenant(enterpriseId);

            var serverTime = DateTime.UtcNow;
            var size = Math.Clamp(pageSize, 1, 1000);

            var query = _appDbContext.Product
                .AsNoTracking()
                .Where(product => product.EnterpriseId == enterpriseId);

            // Changed since the cursor. UpdatedAt is null on a row never edited, so CreatedAt is the
            // fallback — without it a freshly imported catalog looks unchanged and never syncs.
            if (since is { } cursor)
                query = query.Where(product => (product.UpdatedAt ?? product.CreatedAt) > cursor);

            var page = await query
                .OrderBy(product => product.UpdatedAt ?? product.CreatedAt)
                .ThenBy(product => product.Id)
                .Take(size + 1)
                .Select(product => new PdvCatalogItemDTO
                {
                    Id = product.Id,
                    Name = product.Name,
                    Cean = product.Cean,
                    Sku = product.Sku,
                    DefaultValue = product.DefaultValue,
                    StorageQuantity = product.StorageQuantity,
                    UnitOfMeasure = product.UnitOfMeasure,
                    IsService = product.IsService,
                    IsActive = product.IsActive,
                    Ncm = product.Ncm,
                    Cest = product.Cest,
                    CfopIntra = product.CfopIntra,
                    Origem = product.Origem,
                    UnidadeTributavel = product.UnidadeTributavel,
                    ChangedAt = product.UpdatedAt ?? product.CreatedAt
                })
                .ToListAsync();

            // One row over the page size is how "is there more" is answered without a second COUNT
            // over the whole catalog.
            var hasMore = page.Count > size;
            if (hasMore)
                page.RemoveAt(page.Count - 1);

            return new PdvCatalogPageDTO
            {
                Items = page,
                ServerTimeUtc = serverTime,

                // The cursor is the last row's own timestamp, not the server clock: advancing past
                // rows that were written while this page was being read would skip them forever.
                NextCursor = hasMore && page.Count > 0 ? page[^1].ChangedAt : null,
                HasMore = hasMore
            };
        }

        /// <summary>
        /// A page of product thumbnails, oldest change first, for a till filling its image cache.
        /// </summary>
        /// <remarks>
        /// Thumbnails are fetched one product at a time rather than in parallel: this runs in the
        /// background on a till that is selling, and the ERP serving twenty shops has better uses
        /// for its S3 connections than filling one terminal's cache faster.
        /// </remarks>
        public async Task<PdvProductImagePageDTO> GetProductImagesAsync(DateTime? since, Guid enterpriseId, int pageSize)
        {
            AssertTenant(enterpriseId);

            var serverTime = DateTime.UtcNow;
            var size = Math.Clamp(pageSize, 1, 50);

            var query = _appDbContext.Product
                .AsNoTracking()
                .Where(product => product.EnterpriseId == enterpriseId);

            if (since is { } cursor)
                query = query.Where(product => (product.UpdatedAt ?? product.CreatedAt) > cursor);

            var page = await query
                .OrderBy(product => product.UpdatedAt ?? product.CreatedAt)
                .ThenBy(product => product.Id)
                .Take(size + 1)
                .Select(product => new
                {
                    product.Id,
                    product.PictureAdress,
                    ChangedAt = product.UpdatedAt ?? product.CreatedAt
                })
                .ToListAsync();

            var hasMore = page.Count > size;
            if (hasMore)
                page.RemoveAt(page.Count - 1);

            var items = new List<PdvProductImageDTO>(page.Count);
            foreach (var row in page)
            {
                var (base64, hash) = await _productService.GetThumbnailBase64Async(row.PictureAdress);
                items.Add(new PdvProductImageDTO
                {
                    ProductId = row.Id,
                    Hash = hash,
                    ThumbnailBase64 = base64,
                    ChangedAt = row.ChangedAt
                });
            }

            return new PdvProductImagePageDTO
            {
                Items = items,
                ServerTimeUtc = serverTime,
                NextCursor = items.Count > 0 ? items[^1].ChangedAt : since,
                HasMore = hasMore
            };
        }

        #endregion

        #region Helpers

        private async Task<CashSession> RequireSessionAsync(Guid clientSessionId, Guid enterpriseId)
        {
            return await _appDbContext.CashSession
                .FirstOrDefaultAsync(session =>
                    session.EnterpriseId == enterpriseId && session.ClientSessionId == clientSessionId)
                ?? throw new NotFoundDatabaseException("Cash session was not found.");
        }

        private async Task<CashSession> RequireOpenSessionAsync(Guid clientSessionId, Guid enterpriseId)
        {
            var session = await RequireSessionAsync(clientSessionId, enterpriseId);

            if (session.Status != (int)EnumCashSessionStatus.Open)
                throw new ArgumentException("The cash session is already closed.");

            return session;
        }

        private async Task<CashSessionSummaryDTO> BuildSummaryAsync(CashSession session, bool includeExpectedCash)
        {
            var movements = await _appDbContext.CashMovement
                .AsNoTracking()
                .Where(movement => movement.CashSessionId == session.Id && movement.EnterpriseId == session.EnterpriseId)
                .OrderBy(movement => movement.CreatedAt)
                .ToListAsync();

            var sales = await _appDbContext.PdvSale
                .AsNoTracking()
                .Where(sale => sale.CashSessionId == session.Id
                    && sale.EnterpriseId == session.EnterpriseId
                    && sale.Status == (int)EnumPdvSaleStatus.Synced)
                .Select(sale => new { sale.Id, sale.OrderId, sale.DiscountAmount, sale.SurchargeAmount })
                .ToListAsync();

            var saleIds = sales.Select(sale => sale.Id).ToList();

            var payments = await _appDbContext.PdvSalePayment
                .AsNoTracking()
                .Where(payment => saleIds.Contains(payment.PdvSaleId))
                .Select(payment => new { payment.PdvSaleId, payment.Method, payment.Amount })
                .ToListAsync();

            var orderIds = sales.Where(sale => sale.OrderId != null).Select(sale => sale.OrderId!.Value).ToList();

            var gross = await _appDbContext.Order
                .AsNoTracking()
                .Where(order => orderIds.Contains(order.Id))
                .SumAsync(order => (decimal?)order.TotalValue) ?? 0m;

            // Closing is excluded: it is the count being removed, not money the drawer took in.
            var ledger = movements.Where(movement => movement.Type != (int)EnumCashMovementType.Closing).ToList();

            return new CashSessionSummaryDTO
            {
                Session = ToDto(session),
                SaleCount = sales.Count,
                GrossSales = gross,
                TotalDiscount = sales.Sum(sale => sale.DiscountAmount),
                TotalSurcharge = sales.Sum(sale => sale.SurchargeAmount),
                CashIn = ledger.Where(movement => movement.Amount > 0).Sum(movement => movement.Amount),
                CashOut = ledger.Where(movement => movement.Amount < 0).Sum(movement => movement.Amount),

                // Withheld while the session is open and uncounted: an operator who can read the
                // target will hit the target, and a close that always balances tells nobody anything.
                ExpectedCash = includeExpectedCash
                    ? ledger.Sum(movement => movement.Amount)
                    : null,

                Tenders = payments
                    .GroupBy(payment => payment.Method)
                    .Select(group => new PdvTenderTotalDTO
                    {
                        Method = group.Key,
                        Amount = group.Sum(payment => payment.Amount),
                        SaleCount = group.Select(payment => payment.PdvSaleId).Distinct().Count()
                    })
                    .OrderBy(tender => tender.Method)
                    .ToList(),

                Movements = movements.Select(movement => new CashMovementDTO
                {
                    Id = movement.Id,
                    CashSessionId = movement.CashSessionId,
                    Type = movement.Type,
                    Amount = movement.Amount,
                    Reason = movement.Reason,
                    UserId = movement.UserId,
                    ReferenceId = movement.ReferenceId,
                    CreatedAt = movement.CreatedAt
                }).ToList()
            };
        }

        private static CashSessionDTO ToDto(CashSession session) => new()
        {
            Id = session.Id,
            TerminalId = session.TerminalId,
            UserId = session.UserId,
            OpenedAt = session.OpenedAt,
            ClosedAt = session.ClosedAt,
            OpeningFloat = session.OpeningFloat,
            CountedAmount = session.CountedAmount,
            ExpectedAmount = session.ExpectedAmount,
            Difference = session.Difference,
            Status = session.Status
        };

        private static void AssertTenant(Guid enterpriseId)
        {
            if (enterpriseId == Guid.Empty)
                throw new UnauthorizedAccessException("An enterprise is required.");
        }

        #endregion
    }
}
