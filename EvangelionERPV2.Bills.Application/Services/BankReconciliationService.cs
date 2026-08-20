using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// Imports bank statements and reconciles them against what the ERP already believes.
    /// </summary>
    /// <remarks>
    /// The bank is a witness, not the authority. Nothing in here writes
    /// <see cref="Enterprise.CurrentBalance"/>: that number stays app-computed, and the deliverable
    /// is the *variance* between it and the statement. Making the imported ledger authoritative
    /// would mean rewriting every balance write path in the system for a figure a human reads once
    /// a day, which is why it is not done here.
    /// </remarks>
    public class BankReconciliationService : IBankReconciliationService
    {
        /// <summary>
        /// Settlement drifts from posting by a weekend at most, so ±3 days covers a Friday payment
        /// landing on Monday. Wider windows do not find more true matches, they only manufacture
        /// ambiguity — every extra day pulls in more same-amount documents that then have to be
        /// handed to a human anyway.
        /// </summary>
        public const int MatchWindowInDays = 3;

        private const int MaxPageSize = 200;

        private readonly IRepository<BankStatementImport> _importRepository;
        private readonly IRepository<BankStatementLine> _lineRepository;
        private readonly IRepository<Order> _orderRepository;
        private readonly IRepository<PayableBill> _payableBillRepository;
        private readonly IRepository<Enterprise> _enterpriseRepository;

        public BankReconciliationService(
            IRepository<BankStatementImport> importRepository,
            IRepository<BankStatementLine> lineRepository,
            IRepository<Order> orderRepository,
            IRepository<PayableBill> payableBillRepository,
            IRepository<Enterprise> enterpriseRepository)
        {
            _importRepository = importRepository;
            _lineRepository = lineRepository;
            _orderRepository = orderRepository;
            _payableBillRepository = payableBillRepository;
            _enterpriseRepository = enterpriseRepository;
        }

        public async Task<BankStatementImportResultDTO> ImportAsync(
            Guid enterpriseId, byte[] content, CancellationToken cancellationToken = default)
        {
            var statement = OfxStatementParser.Parse(content);

            // A file may repeat a FITID; the first occurrence wins, so the in-file duplicate is
            // dropped here rather than blowing up on the unique index halfway through the insert.
            var transactions = statement.Transactions
                .GroupBy(transaction => transaction.FitId, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(transaction => transaction.PostedAt)
                .ThenBy(transaction => transaction.FitId, StringComparer.Ordinal)
                .ToList();

            var import = new BankStatementImport
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                BankId = statement.BankId,
                AccountId = statement.AccountId,
                LedgerBalance = statement.LedgerBalance,
                BalanceAsOf = statement.BalanceAsOf,
                LineCount = transactions.Count,
                IsActive = true
            };

            var fitIds = transactions.Select(transaction => transaction.FitId).ToList();

            // The idempotency read. Bounded by the file, not by history, and deliberately without an
            // IsActive filter: a soft-deleted line still occupies its FITID, and reinserting behind
            // it would double-count the transaction.
            var alreadyImported = (await _lineRepository.GetAllAsync(line =>
                    line.EnterpriseId == enterpriseId
                    && line.BankId == statement.BankId
                    && line.AccountId == statement.AccountId
                    && fitIds.Contains(line.FitId)))
                .Select(line => line.FitId)
                .ToHashSet(StringComparer.Ordinal);

            var newLines = transactions
                .Where(transaction => !alreadyImported.Contains(transaction.FitId))
                .Select(transaction => new BankStatementLine
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId,
                    ImportId = import.Id,
                    BankId = statement.BankId,
                    AccountId = statement.AccountId,
                    FitId = transaction.FitId,
                    PostedAt = transaction.PostedAt,
                    Amount = transaction.Amount,
                    TrnType = Truncate(transaction.TrnType, 32),
                    Memo = Truncate(transaction.Memo, 512),
                    Status = (int)BankStatementLineStatus.Unmatched,
                    IsActive = true
                })
                .ToList();

            import.NewLineCount = newLines.Count;

            await MatchAsync(enterpriseId, newLines);

            await _importRepository.CreateAsync(import);

            if (newLines.Count > 0)
                await _lineRepository.CreateRangeAsync(newLines);

            // One commit for both: the repositories share the request's AppDbContext, so the import
            // row and its lines land in the same transaction. A half-written import would leave a
            // balance with no movement behind it.
            await _lineRepository.CommitAsync(cancellationToken);

            return new BankStatementImportResultDTO
            {
                ImportId = import.Id,
                BankId = import.BankId,
                AccountId = import.AccountId,
                LedgerBalance = import.LedgerBalance,
                BalanceAsOf = import.BalanceAsOf,
                TotalLines = import.LineCount,
                NewLines = import.NewLineCount,
                MatchedLines = newLines.Count(line => line.Status == (int)BankStatementLineStatus.Matched),
                UnmatchedLines = newLines.Count(line => line.Status == (int)BankStatementLineStatus.Unmatched),
                AmbiguousLines = newLines.Count(line => line.Status == (int)BankStatementLineStatus.Ambiguous)
            };
        }

        /// <summary>
        /// Exact amount, ±<see cref="MatchWindowInDays"/> days, and exactly one candidate. Anything
        /// else is left for a human.
        /// </summary>
        /// <remarks>
        /// Amount equality is not a heuristic that can be loosened later without thought: it is what
        /// makes a single candidate mean something. Fuzzy memo matching was considered and cut —
        /// bank memo text is bank-specific free-form, so a similarity score would be tuned against
        /// one bank's format and quietly mis-reconcile every other one.
        /// </remarks>
        private async Task MatchAsync(Guid enterpriseId, List<BankStatementLine> lines)
        {
            var candidateLines = lines.Where(line => line.Amount != 0).ToList();
            if (candidateLines.Count == 0)
                return;

            // Widened by a day on each side of the window: the stored order and payable dates carry
            // a time component, and the exact ±3 comparison below is done on whole dates.
            var from = candidateLines.Min(line => line.PostedAt).AddDays(-MatchWindowInDays - 1);
            var to = candidateLines.Max(line => line.PostedAt).AddDays(MatchWindowInDays + 1);

            var orders = (await _orderRepository.GetAllAsync(order =>
                    order.EnterpriseId == enterpriseId
                    && order.IsActive == true
                    && order.RefundedAt == null
                    && order.Status != (int)EnumOrderStatus.Refund
                    && (order.Payday.HasValue ? order.Payday.Value : order.PaymentScheduledDate) >= from
                    && (order.Payday.HasValue ? order.Payday.Value : order.PaymentScheduledDate) <= to))
                .Select(order => new Candidate(
                    order.Id,
                    (order.Payday ?? order.PaymentScheduledDate).Date,
                    order.TotalValue))
                .ToList();

            var payables = (await _payableBillRepository.GetAllAsync(payable =>
                    payable.EnterpriseId == enterpriseId
                    && payable.IsActive == true
                    && payable.RefundedAt == null
                    && (payable.PaidAt.HasValue ? payable.PaidAt.Value : payable.DueDate) >= from
                    && (payable.PaidAt.HasValue ? payable.PaidAt.Value : payable.DueDate) <= to))
                .Select(payable => new Candidate(
                    payable.Id,
                    (payable.PaidAt ?? payable.DueDate).Date,
                    // The gross, where one is recorded: a férias bill's Amount is netted by the
                    // provision payroll already accrued (SPEC-hr-module §4 P5a), but the bank sees
                    // the whole payment leave. Null on every other kind of payable, so this is a
                    // no-op for them.
                    payable.GrossAmount ?? payable.Amount))
                .ToList();

            // A document already reconciled against an earlier statement line must not be offered
            // again, or two bank transactions of the same amount would both claim the one invoice.
            var candidateIds = orders.Select(x => x.Id).Concat(payables.Select(x => x.Id)).ToList();
            var consumed = (await _lineRepository.GetAllAsync(line =>
                    line.EnterpriseId == enterpriseId
                    && line.MatchedReferenceId != null
                    && candidateIds.Contains(line.MatchedReferenceId.Value)))
                .Select(line => line.MatchedReferenceId!.Value)
                .ToHashSet();

            foreach (var line in candidateLines)
            {
                // Sign decides the side: money in can only settle a receivable, money out a payable.
                var pool = line.Amount > 0 ? orders : payables;
                var target = Math.Abs(line.Amount);

                var matches = pool
                    .Where(candidate => !consumed.Contains(candidate.Id))
                    .Where(candidate => candidate.Amount == target)
                    .Where(candidate => Math.Abs((candidate.Date - line.PostedAt.Date).Days) <= MatchWindowInDays)
                    .Take(2)
                    .ToList();

                if (matches.Count == 1)
                {
                    line.Status = (int)BankStatementLineStatus.Matched;
                    line.MatchedReferenceId = matches[0].Id;
                    consumed.Add(matches[0].Id);
                }
                else if (matches.Count > 1)
                {
                    line.Status = (int)BankStatementLineStatus.Ambiguous;
                }
            }
        }

        public async Task<IEnumerable<BankStatementLineDTO>> GetUnclearedAsync(
            Guid enterpriseId, int? pageNumber = null, int? pageSize = null)
        {
            var lines = await _lineRepository.GetAllAsync(
                pageNumber,
                NormalizePageSize(pageSize),
                line => line.EnterpriseId == enterpriseId
                    && line.IsActive == true
                    && line.Status != (int)BankStatementLineStatus.Matched);

            return lines
                .OrderBy(line => line.PostedAt)
                .Select(line => new BankStatementLineDTO
                {
                    Id = line.Id,
                    FitId = line.FitId,
                    PostedAt = line.PostedAt,
                    Amount = line.Amount,
                    TrnType = line.TrnType,
                    Memo = line.Memo,
                    Status = ((BankStatementLineStatus)line.Status).ToString(),
                    MatchedReferenceId = line.MatchedReferenceId
                })
                .ToList();
        }

        public async Task<BankReconciliationVarianceDTO> GetVarianceAsync(Guid enterpriseId)
        {
            var enterprise = await _enterpriseRepository.GetByIdAsync(enterpriseId);
            var currentBalance = enterprise?.CurrentBalance ?? 0m;

            var latest = (await _importRepository.GetAllAsync(import =>
                    import.EnterpriseId == enterpriseId && import.IsActive == true))
                .OrderByDescending(import => import.BalanceAsOf)
                .ThenByDescending(import => import.CreatedAt)
                .FirstOrDefault();

            var uncleared = (await _lineRepository.GetAllAsync(line =>
                    line.EnterpriseId == enterpriseId
                    && line.IsActive == true
                    && line.Status != (int)BankStatementLineStatus.Matched))
                .Count();

            return new BankReconciliationVarianceDTO
            {
                CurrentBalance = currentBalance,
                StatementBalance = latest?.LedgerBalance,
                // Null rather than the raw balance when nothing has been imported: a "variance" of
                // the whole balance would read as a catastrophic break rather than as no data.
                Variance = latest == null ? null : currentBalance - latest.LedgerBalance,
                StatementAsOf = latest?.BalanceAsOf,
                BankId = latest?.BankId,
                AccountId = latest?.AccountId,
                UnclearedLines = uncleared
            };
        }

        private static int? NormalizePageSize(int? pageSize) =>
            pageSize.HasValue ? Math.Clamp(pageSize.Value, 1, MaxPageSize) : null;

        private static string Truncate(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength];

        /// <summary>A document the statement could be pointing at, flattened to the three fields matching uses.</summary>
        private sealed record Candidate(Guid Id, DateTime Date, decimal Amount);
    }
}
