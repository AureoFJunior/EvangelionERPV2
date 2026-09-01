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
        private readonly IRepository<PdvCharge> _chargeRepository;
        private readonly StripePixProvider _pixProvider;

        /// <summary>A backoffice list, not an export. Enough to see a run of refusals, not the year.</summary>
        private const int MaxOverrideAuditPageSize = 200;

        public PdvService(
            AppDbContext appDbContext,
            IRepository<CashSession> sessionRepository,
            IRepository<CashMovement> movementRepository,
            IRepository<PdvSale> saleRepository,
            IRepository<PdvSalePayment> paymentRepository,
            IOrderService<Order> orderService,
            IProductService<Product> productService,
            IRepository<PdvCharge> chargeRepository,
            StripePixProvider pixProvider)
        {
            _appDbContext = appDbContext;
            _sessionRepository = sessionRepository;
            _movementRepository = movementRepository;
            _saleRepository = saleRepository;
            _paymentRepository = paymentRepository;
            _orderService = orderService;
            _productService = productService;
            _chargeRepository = chargeRepository;
            _pixProvider = pixProvider;
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

        /// <inheritdoc />
        public async Task<IReadOnlyList<PdvTerminalDTO>> GetTerminalsAsync(Guid enterpriseId)
        {
            AssertTenant(enterpriseId);

            // Projected, never the entity: PdvTerminal carries the PBKDF2 verifier, and a backoffice
            // screen that renders the whole row would put it in a browser and in every log between.
            return await _appDbContext.PdvTerminal
                .AsNoTracking()
                .Where(terminal => terminal.EnterpriseId == enterpriseId)
                .OrderBy(terminal => terminal.TerminalId)
                .Select(terminal => new PdvTerminalDTO
                {
                    Id = terminal.Id,
                    TerminalId = terminal.TerminalId,
                    IsRevoked = terminal.IsRevoked,
                    LastSeenAt = terminal.LastSeenAt,
                    EnrolledAt = terminal.CreatedAt
                })
                .ToListAsync();
        }

        /// <inheritdoc />
        public async Task<bool> RevokeTerminalAsync(Guid terminalId, Guid enterpriseId)
        {
            AssertTenant(enterpriseId);

            var terminal = await _appDbContext.PdvTerminal
                .FirstOrDefaultAsync(row => row.Id == terminalId && row.EnterpriseId == enterpriseId);

            // Idempotent, and a terminal belonging to another tenant is indistinguishable from one
            // that never existed.
            if (terminal == null || terminal.IsRevoked)
                return false;

            terminal.IsRevoked = true;
            terminal.UpdatedAt = DateTime.UtcNow;

            // The entity is tracked by this context, which every repository here wraps; saving it
            // directly is the same write without a ninth constructor argument.
            await _appDbContext.SaveChangesAsync();

            return true;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<CashSessionDTO>> GetOpenSessionsAsync(Guid enterpriseId)
        {
            AssertTenant(enterpriseId);

            return await _appDbContext.CashSession
                .AsNoTracking()
                .Where(session => session.EnterpriseId == enterpriseId
                    && session.Status == (int)EnumCashSessionStatus.Open)
                .OrderBy(session => session.TerminalId)
                .Select(session => new CashSessionDTO
                {
                    Id = session.ClientSessionId,
                    TerminalId = session.TerminalId,
                    UserId = session.UserId,
                    OpenedAt = session.OpenedAt,
                    ClosedAt = session.ClosedAt,
                    OpeningFloat = session.OpeningFloat,
                    CountedAmount = session.CountedAmount,
                    ExpectedAmount = session.ExpectedAmount,
                    Difference = session.Difference,
                    Status = session.Status
                })
                .ToListAsync();
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<PdvOverrideAuditDTO>> GetOverrideAuditAsync(
            Guid enterpriseId, DateTime? since, bool? onlyRefused, int take)
        {
            AssertTenant(enterpriseId);

            var query = _appDbContext.PdvOverrideAudit
                .AsNoTracking()
                .Where(audit => audit.EnterpriseId == enterpriseId);

            if (since != null)
                query = query.Where(audit => audit.OccurredAt >= since);

            // The refused ones are the reason this table exists: a run of them against one
            // supervisor is somebody guessing a PIN.
            if (onlyRefused == true)
                query = query.Where(audit => !audit.Succeeded);

            return await query
                .OrderByDescending(audit => audit.OccurredAt)
                .Take(Math.Clamp(take, 1, MaxOverrideAuditPageSize))
                .Select(audit => new PdvOverrideAuditDTO
                {
                    Id = audit.Id,
                    TerminalId = audit.TerminalId,
                    SupervisorUserId = audit.SupervisorUserId,
                    OperatorUserId = audit.OperatorUserId,
                    Permission = audit.Permission,
                    Succeeded = audit.Succeeded,
                    OccurredAt = audit.OccurredAt
                })
                .ToListAsync();
        }

        /// <inheritdoc />
        public async Task<PdvSale?> GetSaleAsync(Guid clientSaleId, Guid enterpriseId)
        {
            AssertTenant(enterpriseId);

            return await _appDbContext.PdvSale
                .AsNoTracking()
                .FirstOrDefaultAsync(sale => sale.EnterpriseId == enterpriseId && sale.ClientSaleId == clientSaleId);
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

            // Card only, and rejected rather than thrown: this is a payload the server will never
            // accept however many times the terminal resends it, which is exactly what Rejected
            // means. The till refuses it first (TenderSheet.Take); this is the assertion that makes
            // the rule true of anything that reaches the database, including a hand-built request.
            var uncodedCard = payments.FirstOrDefault(payment =>
                (payment.Method == (int)EnumPaymentMethod.Debit || payment.Method == (int)EnumPaymentMethod.Credit)
                && string.IsNullOrWhiteSpace(payment.AuthorizationCode));

            if (uncodedCard != null)
            {
                return await RejectAsync(claimed.Sale,
                    "Um pagamento em cartão precisa do código de autorização.");
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

            // Which of the named charges we can actually prove were paid. Asked once, here, rather
            // than per tender: a sale has a handful of payments and this is one round trip.
            var namedCharges = payments
                .Where(payment => payment.ChargeId != null)
                .Select(payment => payment.ChargeId!.Value)
                .Distinct()
                .ToList();

            // Scoped to this basket and matched on the amount, not merely "a paid charge of ours".
            // A terminal names the charge id, so anything the id alone can reach is something the
            // terminal can choose: without the ClientSaleId a R$ 500 tender could point at a paid
            // R$ 1 charge from yesterday's sale and be recorded as proven.
            var paidCharges = namedCharges.Count == 0
                ? []
                : await _appDbContext.PdvCharge
                    .Where(charge => charge.EnterpriseId == enterpriseId
                                     && namedCharges.Contains(charge.Id)
                                     && charge.ClientSaleId == sale.ClientSaleId
                                     && charge.Status == (int)EnumPdvChargeStatus.Paid)
                    .Select(charge => new { charge.Id, charge.Amount })
                    .ToListAsync();

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
                    PdvChargeId = payment.ChargeId,

                    // Never the raw flag. Proof is a charge that exists, belongs to this shop,
                    // was raised for this basket, reached Paid and settled this much — checked
                    // here against our own rows, because a terminal that could mint proof by
                    // setting a bool would make the column worthless. A tender that fails any of
                    // those is recorded unverified, never refused: the sale itself is fine and
                    // RejectAsync is terminal.
                    IsVerified = payment.ChargeId != null
                        && paidCharges.Any(charge => charge.Id == payment.ChargeId.Value
                                                     && charge.Amount == payment.Amount),
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
                NextCursor = hasMore && items.Count > 0 ? items[^1].ChangedAt : null,
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

        #region Charges

        /// <summary>Stripe's PIX ceiling. Splitting a bigger basket across charges is not automated.</summary>
        private const decimal MaxPixChargeAmount = 3000m;

        /// <summary>Below this a PIX charge costs more in fees than it collects.</summary>
        private const decimal MinPixChargeAmount = 0.50m;

        /// <summary>
        /// Raises a PIX charge for a basket, or hands back the one already raised for it.
        /// </summary>
        /// <remarks>
        /// Every gate runs before the provider is touched. A shop that cannot take PIX must find
        /// that out from a greyed-out button, not from a customer standing at the counter with a
        /// bank app open — and the amount bounds are Stripe's, so sending a request we know it will
        /// refuse only costs the customer time.
        ///
        /// The lookup on <c>ClientSaleId</c> is what makes a resend safe: a terminal that lost the
        /// response to its first call must find that intent rather than open a second one against
        /// the same basket. A customer who pays twice is the failure this method exists to prevent,
        /// and the unique index on <c>(EnterpriseId, ProviderIntentId)</c> is the backstop.
        /// </remarks>
        public async Task<PdvChargeDTO> StartPixChargeAsync(StartPixChargeRequestDTO request, Guid enterpriseId)
        {
            ArgumentNullException.ThrowIfNull(request);
            AssertTenant(enterpriseId);

            if (request.ClientSaleId == Guid.Empty)
                throw new ArgumentException("A client sale id is required.");

            if (request.Amount < MinPixChargeAmount || request.Amount > MaxPixChargeAmount)
            {
                throw new ArgumentException(
                    $"A PIX charge must be between {MinPixChargeAmount:0.00} and {MaxPixChargeAmount:0.00}.");
            }

            var existing = await _appDbContext.PdvCharge
                .Where(charge => charge.EnterpriseId == enterpriseId
                                 && charge.ClientSaleId == request.ClientSaleId)
                .OrderByDescending(charge => charge.CreatedAt)
                .FirstOrDefaultAsync();

            // A window that has closed is the same dead QR as an abandoned one, so it is written
            // down here rather than left Pending forever — nothing else in the system does it.
            await ExpireIfElapsedAsync(existing);

            // A charge that was abandoned, refused or timed out is not an answer to "take PIX for
            // this basket". Handing it back left the operator with a dead QR and a poll that reports
            // "cancelado" forever — PIX permanently unavailable for a basket the customer is still
            // standing at. Those get a fresh attempt; Pending and Paid do not.
            var settledOrAbandoned = existing != null
                && existing.Status is (int)EnumPdvChargeStatus.Cancelled
                    or (int)EnumPdvChargeStatus.Failed
                    or (int)EnumPdvChargeStatus.Expired;

            if (existing != null && !settledOrAbandoned)
            {
                // Paid: never a second charge for a basket the customer already paid for.
                // Pending: the live intent, and only if it is for the amount being asked. Returning
                // a QR for a different figure is how a customer pays the wrong price.
                if (existing.Status == (int)EnumPdvChargeStatus.Pending && existing.Amount != request.Amount)
                {
                    throw new ArgumentException(
                        "A PIX charge for a different amount is already open on this sale. Cancel it first.");
                }

                return ToDto(existing);
            }

            var enterprise = await _appDbContext.Enterprise
                .FirstOrDefaultAsync(shop => shop.Id == enterpriseId);

            // Both halves. A connected account is not the same as Stripe having let this shop
            // through the invite-only PIX programme, and neither is worth a failed payment.
            if (enterprise?.PixPaymentsEnabled != true
                || string.IsNullOrWhiteSpace(enterprise.StripeConnectedAccountId))
            {
                throw new ArgumentException("This shop is not set up to take PIX.");
            }

            var intent = await _pixProvider.CreateAsync(
                request.Amount, request.ClientSaleId, enterprise.StripeConnectedAccountId);

            var created = new PdvCharge
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                ClientSaleId = request.ClientSaleId,
                Provider = "stripe",
                ProviderIntentId = intent.IntentId,
                Amount = request.Amount,
                Status = (int)EnumPdvChargeStatus.Pending,
                QrPayload = intent.QrPayload,
                ExpiresAt = intent.ExpiresAt,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            await _chargeRepository.CreateAsync(created);
            await _chargeRepository.CommitAsync();

            return ToDto(created);
        }

        /// <summary>
        /// The charge as we last recorded it.
        /// </summary>
        /// <remarks>
        /// Reads our own row and never the provider. The till polls this every second or so while a
        /// customer pays, and a poll that reached Stripe would put its rate limit on the path of
        /// every sale — the webhook is what moves the row, and this only reports it.
        /// </remarks>
        public async Task<PdvChargeDTO?> GetChargeAsync(Guid chargeId, Guid enterpriseId)
        {
            AssertTenant(enterpriseId);

            var charge = await _appDbContext.PdvCharge
                .FirstOrDefaultAsync(row => row.Id == chargeId && row.EnterpriseId == enterpriseId);

            // The till polls this while the customer pays. Without it a closed window answers
            // Pending forever and the operator waits on a QR nobody can still scan.
            await ExpireIfElapsedAsync(charge);

            return charge == null ? null : ToDto(charge);
        }

        /// <summary>
        /// Writes down that a charge's window closed. One row, at most once.
        /// </summary>
        /// <remarks>
        /// Lazily, on the two paths that care — the till's poll and the next attempt on the same
        /// basket — rather than in a sweeper. A charge nobody is looking at costs nothing by staying
        /// Pending, and a background pass over every open charge in every tenant would exist to
        /// change rows no one is waiting on.
        ///
        /// **Expired is our inference, not the provider's verdict**, which is why
        /// <see cref="ApplyChargeEventAsync"/> still lets a Paid event settle a row this marked. The
        /// clocks are not the same and the money is what matters: a customer whose PIX landed in the
        /// last second of the window is paid, whatever we had written down by then.
        /// </remarks>
        private async Task ExpireIfElapsedAsync(PdvCharge? charge)
        {
            if (charge == null
                || charge.Status != (int)EnumPdvChargeStatus.Pending
                || charge.ExpiresAt > DateTime.UtcNow)
            {
                return;
            }

            charge.Status = (int)EnumPdvChargeStatus.Expired;
            charge.UpdatedAt = DateTime.UtcNow;

            _chargeRepository.Update(charge);
            await _chargeRepository.CommitAsync();
        }

        /// <summary>
        /// Abandons a charge nobody paid.
        /// </summary>
        /// <remarks>
        /// A charge that already reached a terminal status is returned untouched. The race is real
        /// and it is one-sided: a customer whose payment landed a second before the operator pressed
        /// Esc must not have it marked cancelled, so anything already settled wins over the cancel.
        /// </remarks>
        public async Task<PdvChargeDTO> CancelPixChargeAsync(Guid chargeId, Guid enterpriseId)
        {
            AssertTenant(enterpriseId);

            var charge = await _appDbContext.PdvCharge
                .FirstOrDefaultAsync(row => row.Id == chargeId && row.EnterpriseId == enterpriseId)
                ?? throw new ArgumentException("No such charge.");

            if (charge.Status != (int)EnumPdvChargeStatus.Pending)
                return ToDto(charge);

            // Stripe first: if it refuses because the intent already succeeded, our row must not
            // claim otherwise, and the exception carries that to the operator.
            await _pixProvider.CancelAsync(charge.ProviderIntentId);

            charge.Status = (int)EnumPdvChargeStatus.Cancelled;
            charge.UpdatedAt = DateTime.UtcNow;
            _chargeRepository.Update(charge);
            await _chargeRepository.CommitAsync();

            return ToDto(charge);
        }

        /// <summary>
        /// Moves a charge to what the provider says it now is. Returns false when nothing changed.
        /// </summary>
        /// <remarks>
        /// Idempotent twice over: an event id already recorded on the row is a no-op, and a charge
        /// already in a terminal status is never moved again. Between them, a replay and a late
        /// delivery both cost one indexed read — and expiry racing settlement cannot un-pay a
        /// customer, because the terminal status that got there first wins.
        ///
        /// A charge we have no row for returns false and is not an error. Our endpoint may be
        /// receiving events for another integration on the same account, and answering 500 to those
        /// buys an endlessly retried queue.
        ///
        /// No tenant argument by design: the row carries the EnterpriseId, and nothing the caller
        /// says can change which shop this concerns.
        /// </remarks>
        public async Task<bool> ApplyChargeEventAsync(string eventId, string providerIntentId, int status,
            string? endToEndId, decimal? feeAmount, DateTime? paidAt)
        {
            if (string.IsNullOrWhiteSpace(providerIntentId))
                return false;

            var charge = await _appDbContext.PdvCharge
                .FirstOrDefaultAsync(row => row.ProviderIntentId == providerIntentId);

            // Pending is the ordinary case. Expired is the one exception, because unlike Cancelled
            // (we called the provider and it agreed) and Failed (the provider's own verdict), it is
            // a conclusion this system drew from a clock. A settlement event outranks it: the money
            // arrived, and a paid customer must never be filed as timed out.
            var settlesAnExpiredCharge = charge != null
                && charge.Status == (int)EnumPdvChargeStatus.Expired
                && status == (int)EnumPdvChargeStatus.Paid;

            if (charge == null
                || charge.ProviderEventId == eventId
                || (charge.Status != (int)EnumPdvChargeStatus.Pending && !settlesAnExpiredCharge))
            {
                return false;
            }

            charge.Status = status;
            charge.EndToEndId = endToEndId ?? charge.EndToEndId;
            charge.FeeAmount = feeAmount ?? charge.FeeAmount;
            charge.PaidAt = paidAt ?? charge.PaidAt;
            charge.ProviderEventId = eventId;
            charge.UpdatedAt = DateTime.UtcNow;

            _chargeRepository.Update(charge);
            await _chargeRepository.CommitAsync();

            return true;
        }

        private static PdvChargeDTO ToDto(PdvCharge charge) => new()
        {
            ChargeId = charge.Id,
            Status = charge.Status,
            QrPayload = charge.QrPayload ?? string.Empty,
            Amount = charge.Amount,
            ExpiresAt = charge.ExpiresAt,
            EndToEndId = charge.EndToEndId
        };

        #endregion

        private static void AssertTenant(Guid enterpriseId)
        {
            if (enterpriseId == Guid.Empty)
                throw new UnauthorizedAccessException("An enterprise is required.");
        }

        #endregion
    }
}
