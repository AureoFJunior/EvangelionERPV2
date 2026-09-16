using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Caching.Distributed;
using Serilog;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// The "painel do dia": what needs a human today, in one call.
    /// </summary>
    /// <remarks>
    /// It lives in the Bills module because six of the seven signals are money — the fiscal and
    /// bank ones are read straight off their own tables and need no service of their own. Nothing
    /// here writes; every signal is a view of a document that already exists, which is also why
    /// there is no notification table behind it: settling the bill is what clears the alert.
    /// </remarks>
    public class DailyPanelService : IDailyPanelService
    {
        private const string StockCacheKeyPrefix = "DailyPanel:Stock:";
        private static readonly TimeSpan StockCacheTtl = TimeSpan.FromMinutes(15);
        private const int StockSampleSize = 5;
        private const int PayablesHorizonInDays = 7;

        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// Read side for the panel's counts and sums. The repositories hand back whole entities,
        /// and every builder below only ever needed a few columns to add them up - the same shape
        /// that made SalesPerformance the most expensive endpoint in the API.
        /// </summary>
        private readonly AppDbContext _context;

        private readonly IRepository<Bill> _billRepository;
        private readonly IRepository<PayableBill> _payableBillRepository;
        private readonly IRepository<NFeDocument> _nfeDocumentRepository;
        private readonly IRepository<EnterpriseFiscalSettings> _fiscalSettingsRepository;
        private readonly IRepository<BankStatementLine> _bankStatementLineRepository;
        private readonly IPayableBillService _payableBillService;
        private readonly IDistributedCache _cache;

        public DailyPanelService(
            AppDbContext context,
            IRepository<Bill> billRepository,
            IRepository<PayableBill> payableBillRepository,
            IRepository<NFeDocument> nfeDocumentRepository,
            IRepository<EnterpriseFiscalSettings> fiscalSettingsRepository,
            IRepository<BankStatementLine> bankStatementLineRepository,
            IPayableBillService payableBillService,
            IDistributedCache cache)
        {
            _context = context;
            _billRepository = billRepository;
            _payableBillRepository = payableBillRepository;
            _nfeDocumentRepository = nfeDocumentRepository;
            _fiscalSettingsRepository = fiscalSettingsRepository;
            _bankStatementLineRepository = bankStatementLineRepository;
            _payableBillService = payableBillService;
            _cache = cache;
        }

        public async Task<DailyPanelDTO> GetTodayAsync(Guid enterpriseId, CancellationToken cancellation = default)
        {
            if (enterpriseId == Guid.Empty)
                throw new ArgumentException("Enterprise is required.", nameof(enterpriseId));

            var today = DateTime.UtcNow.Date;

            return new DailyPanelDTO
            {
                GeneratedAtUtc = DateTime.UtcNow,
                Receivables = await BuildReceivablesAsync(enterpriseId, today),
                Payables = await BuildPayablesAsync(enterpriseId, today),
                Stock = await BuildStockAsync(enterpriseId),
                Fiscal = await BuildFiscalAsync(enterpriseId, today),
                Bank = await BuildBankAsync(enterpriseId)
            };
        }

        private async Task<DailyPanelReceivablesDTO> BuildReceivablesAsync(Guid enterpriseId, DateTime today)
        {
            // Scoped through the Order navigation rather than by collecting order ids first. Two
            // reasons, both load-bearing: a tenant with more than ~2,100 orders would blow SQL
            // Server's parameter limit on a Contains list, and the id list means reading the whole
            // Order table to answer a question about a handful of open titles.
            //
            // Registered is exactly the open set: Paid and Cancelled are excluded here, and Overdue
            // is never persisted — it is derived below from the same clock Bill.StatusAsOf uses.
            // Counted in SQL, not in the process.
            //
            // This read every open title for the tenant and added them up in a foreach. Projecting
            // to four columns made each row cheaper but left the growth: measured 2026-09-16 with
            // 200,005 open bills, one call added ~106 MiB and twenty concurrent callers took the
            // process from 318 MiB to 1.1 GiB - against a 2 GiB container limit. The same shape had
            // already produced an OutOfMemoryException in SalesPerformance at that volume.
            //
            // The database returns six numbers now, whatever the tenant's size.
            var liveBills = _context.Set<Bill>().AsNoTracking()
                .Where(x => x.Order != null
                            && x.Order.EnterpriseId == enterpriseId
                            && x.Order.IsActive == true
                            && x.Order.RefundedAt == null
                            && x.Order.Status != (int)EnumOrderStatus.Refund
                            && (x.IsActive == null || x.IsActive == true)
                            && x.Status == BillStatus.Registered)
                // Newest bill per order, which is what SelectLiveBillsByOrder picked in memory. The
                // status filter above already excludes Cancelled.
                .Where(x => x.CreatedAt == _context.Set<Bill>().AsNoTracking()
                    .Where(other => other.OrderId == x.OrderId
                                    && (other.IsActive == null || other.IsActive == true)
                                    && other.Status == BillStatus.Registered)
                    .Max(other => other.CreatedAt));

            var totals = await liveBills
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    OpenCount = group.Count(),
                    OpenAmount = group.Sum(x => x.Amount),
                    OverdueCount = group.Count(x => x.DueDate < today),
                    OverdueAmount = group.Sum(x => x.DueDate < today ? x.Amount : 0m),
                    DueTodayCount = group.Count(x => x.DueDate == today),
                    DueTodayAmount = group.Sum(x => x.DueDate == today ? x.Amount : 0m)
                })
                .FirstOrDefaultAsync();

            var panel = new DailyPanelReceivablesDTO();

            if (totals != null)
            {
                panel.OpenCount = totals.OpenCount;
                panel.OpenAmount = totals.OpenAmount;
                panel.OverdueCount = totals.OverdueCount;
                panel.OverdueAmount = totals.OverdueAmount;
                panel.DueTodayCount = totals.DueTodayCount;
                panel.DueTodayAmount = totals.DueTodayAmount;
            }

            return panel;
        }

        private async Task<DailyPanelPayablesDTO> BuildPayablesAsync(Guid enterpriseId, DateTime today)
        {
            var horizon = today.AddDays(PayablesHorizonInDays);

            // Counted in SQL for the same reason as the receivables above: the number of unpaid
            // payables inside the horizon is a tenant-sized quantity, and this only ever needed six
            // numbers from it.
            var totals = await _context.Set<PayableBill>().AsNoTracking()
                .Where(x => x.EnterpriseId == enterpriseId
                            && x.IsActive == true
                            && !x.IsPaid
                            && x.RefundedAt == null
                            && x.Amount > 0
                            && x.DueDate < horizon)
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    WeekCount = group.Count(),
                    WeekAmount = group.Sum(x => x.Amount),
                    OverdueCount = group.Count(x => x.DueDate.Date < today),
                    OverdueAmount = group.Sum(x => x.DueDate.Date < today ? x.Amount : 0m),
                    DueTodayCount = group.Count(x => x.DueDate.Date == today),
                    DueTodayAmount = group.Sum(x => x.DueDate.Date == today ? x.Amount : 0m)
                })
                .FirstOrDefaultAsync();

            var panel = new DailyPanelPayablesDTO();

            if (totals != null)
            {
                panel.DueThisWeekCount = totals.WeekCount;
                panel.DueThisWeekAmount = totals.WeekAmount;
                panel.OverdueCount = totals.OverdueCount;
                panel.OverdueAmount = totals.OverdueAmount;
                panel.DueTodayCount = totals.DueTodayCount;
                panel.DueTodayAmount = totals.DueTodayAmount;
            }

            return panel;
        }

        private async Task<DailyPanelStockDTO> BuildStockAsync(Guid enterpriseId)
        {
            // The replenishment engine reads a 180-day movement window per product, which is far
            // too expensive to run on every panel load. Criticality does not change minute to
            // minute, so it is cached.
            // ponytail: 15-minute cache. Drop the TTL if a tenant complains the panel lags a receipt.
            var cacheKey = StockCacheKeyPrefix + enterpriseId;

            var cached = await GetCachedStockAsync(cacheKey);
            if (cached != null)
            {
                cached.FromCache = true;
                return cached;
            }

            var suggestions = await _payableBillService.GetReplenishmentSuggestionsAsync(
                enterpriseId,
                new ReplenishmentSuggestionRequestDTO
                {
                    PageNumber = 1,
                    PageSize = StockSampleSize,
                    SortByCriticality = true
                });

            var critical = suggestions
                .Where(x => string.Equals(x.Criticality, "high", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var panel = new DailyPanelStockDTO
            {
                CriticalCount = critical.Count,
                CriticalProducts = critical.Select(x => x.ProductName).ToList()
            };

            await SetCachedStockAsync(cacheKey, panel);
            return panel;
        }

        private async Task<DailyPanelFiscalDTO> BuildFiscalAsync(Guid enterpriseId, DateTime today)
        {
            var documents = await _nfeDocumentRepository.GetAllAsync(
                x => x.EnterpriseId == enterpriseId
                     && x.IsActive == true
                     && (x.Status == NFeStatus.Pending || x.Status == NFeStatus.Rejected));

            var panel = new DailyPanelFiscalDTO();

            foreach (var document in documents)
            {
                if (document.Status == NFeStatus.Rejected)
                    panel.RejectedCount++;
                else
                    panel.PendingCount++;

                if (document.IsOutcomeUnknown)
                    panel.OutcomeUnknownCount++;
            }

            var settings = (await _fiscalSettingsRepository.GetAllAsync(
                    x => x.EnterpriseId == enterpriseId && x.IsActive == true))
                .FirstOrDefault();

            // A missing certificate reports "not configured" rather than "expired": the two mean
            // different things to whoever reads the panel, and only one of them is an emergency.
            if (settings?.CertNotAfterUtc != null)
            {
                panel.CertificateConfigured = true;
                panel.CertificateDaysRemaining = (int)(settings.CertNotAfterUtc.Value.Date - today).TotalDays;
            }

            return panel;
        }

        private async Task<DailyPanelBankDTO> BuildBankAsync(Guid enterpriseId)
        {
            var lines = await _bankStatementLineRepository.GetAllAsync(
                x => x.EnterpriseId == enterpriseId
                     && x.IsActive == true
                     && (x.Status == (int)BankStatementLineStatus.Unmatched
                         || x.Status == (int)BankStatementLineStatus.Ambiguous));

            return new DailyPanelBankDTO { UnclearedCount = lines.Count() };
        }

        private async Task<DailyPanelStockDTO?> GetCachedStockAsync(string key)
        {
            try
            {
                var cachedValue = await _cache.GetStringAsync(key);
                if (string.IsNullOrWhiteSpace(cachedValue))
                    return null;

                return JsonSerializer.Deserialize<DailyPanelStockDTO>(cachedValue, SerializerOptions);
            }
            catch (Exception ex)
            {
                // A cache that is down must not take the panel with it.
                Log.Logger.Warning("Daily panel stock cache read failed. ErrorType={ErrorType}", ex.GetType().Name);
                return null;
            }
        }

        private async Task SetCachedStockAsync(string key, DailyPanelStockDTO panel)
        {
            try
            {
                await _cache.SetStringAsync(
                    key,
                    JsonSerializer.Serialize(panel, SerializerOptions),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = StockCacheTtl });
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("Daily panel stock cache write failed. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }

    }
}
