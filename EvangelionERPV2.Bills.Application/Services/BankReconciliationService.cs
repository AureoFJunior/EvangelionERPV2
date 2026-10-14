using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// Imports bank statements and reconciles them against what the ERP already believes.
    /// </summary>
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
        private async Task MatchAsync(Guid enterpriseId, List<BankStatementLine> lines)
        {
            var candidateLines = lines.Where(line => line.Amount != 0).ToList();
            if (candidateLines.Count == 0)
                return;

            var from = candidateLines.Min(line => line.PostedAt).AddDays(-MatchWindowInDays - 1);
            var to = candidateLines.Max(line => line.PostedAt).AddDays(MatchWindowInDays + 1);

            var orders = (await _orderRepository.GetPagedAsync(order =>
                    order.EnterpriseId == enterpriseId
                    && order.IsActive == true
                    && order.RefundedAt == null
                    && order.Status != (int)EnumOrderStatus.Refund
                    && (order.Payday.HasValue ? order.Payday.Value : order.PaymentScheduledDate) >= from
                    && (order.Payday.HasValue ? order.Payday.Value : order.PaymentScheduledDate) <= to))
                .Items
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
                    payable.GrossAmount ?? payable.Amount))
                .ToList();

            var candidateIds = orders.Select(x => x.Id).Concat(payables.Select(x => x.Id)).ToList();
            var consumed = (await _lineRepository.GetAllAsync(line =>
                    line.EnterpriseId == enterpriseId
                    && line.MatchedReferenceId != null
                    && candidateIds.Contains(line.MatchedReferenceId.Value)))
                .Select(line => line.MatchedReferenceId!.Value)
                .ToHashSet();

            foreach (var line in candidateLines)
            {
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
                    && (line.Status == (int)BankStatementLineStatus.Unmatched
                        || line.Status == (int)BankStatementLineStatus.Ambiguous));

            return lines
                .OrderBy(line => line.PostedAt)
                .Select(Map)
                .ToList();
        }

        /// <summary>
        /// Clears one line a human has judged: pointed at the document it settles, or marked as
        /// corresponding to nothing in the ERP.
        /// </summary>
        public async Task<BankStatementLineDTO> ResolveAsync(
            Guid enterpriseId,
            Guid lineId,
            Guid? matchedReferenceId,
            CancellationToken cancellationToken = default)
        {
            if (enterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Bank reconciliation enterprise is required.");

            return await _lineRepository.ExecuteInTransactionAsync(async () =>
            {
                var line = await _lineRepository.GetByIdAsync(lineId);

                if (line == null || line.IsActive != true || line.EnterpriseId != enterpriseId)
                    throw new NotFoundDatabaseException($"{nameof(BankStatementLine)} was not found in database.");

                if (matchedReferenceId.HasValue)
                {
                    if (line.Amount > 0)
                        await _orderRepository.LockForUpdateAsync(matchedReferenceId.Value, cancellationToken);
                    else
                        await _payableBillRepository.LockForUpdateAsync(matchedReferenceId.Value, cancellationToken);
                    await EnsureReferenceIsClaimableAsync(enterpriseId, line, matchedReferenceId.Value);

                    line.Status = (int)BankStatementLineStatus.Matched;
                    line.MatchedReferenceId = matchedReferenceId.Value;
                }
                else
                {
                    line.Status = (int)BankStatementLineStatus.Ignored;
                    line.MatchedReferenceId = null;
                }

                line.UpdatedAt = DateTime.UtcNow;

                _lineRepository.Update(line);
                await _lineRepository.CommitAsync(cancellationToken);

                return Map(line);
            }, cancellationToken);
        }

        /// <summary>
        /// The document has to exist, belong to this tenant, sit on the side of the statement the
        /// line's sign implies, and not already be claimed by a different line.
        /// </summary>
        private async Task EnsureReferenceIsClaimableAsync(Guid enterpriseId, BankStatementLine line, Guid referenceId)
        {
            if (referenceId == Guid.Empty)
                throw new InsertDatabaseException("A document is required to match a statement line.");

            if (line.Amount == 0)
                throw new InsertDatabaseException("A zero-amount statement line settles no document.");

            var exists = line.Amount > 0
                ? (await _orderRepository.GetPagedAsync(order =>
                        order.Id == referenceId && order.EnterpriseId == enterpriseId && order.IsActive == true,
                        pageNumber: 1, pageSize: 1)).Items.Count > 0
                : (await _payableBillRepository.GetPagedAsync(payable =>
                        payable.Id == referenceId && payable.EnterpriseId == enterpriseId && payable.IsActive == true,
                        pageNumber: 1, pageSize: 1)).Items.Count > 0;

            if (!exists)
            {
                throw new NotFoundDatabaseException(line.Amount > 0
                    ? "No order of this company matches that reference."
                    : "No payable bill of this company matches that reference.");
            }

            var claimedElsewhere = (await _lineRepository.GetAllAsync(other =>
                    other.EnterpriseId == enterpriseId
                    && other.IsActive == true
                    && other.Id != line.Id
                    && other.MatchedReferenceId == referenceId))
                .Any();

            if (claimedElsewhere)
                throw new InsertDatabaseException("That document is already reconciled against another statement line.");
        }

        private static BankStatementLineDTO Map(BankStatementLine line) => new()
        {
            Id = line.Id,
            FitId = line.FitId,
            PostedAt = line.PostedAt,
            Amount = line.Amount,
            TrnType = line.TrnType,
            Memo = line.Memo,
            Status = ((BankStatementLineStatus)line.Status).ToString(),
            MatchedReferenceId = line.MatchedReferenceId
        };

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
                    && (line.Status == (int)BankStatementLineStatus.Unmatched
                        || line.Status == (int)BankStatementLineStatus.Ambiguous)))
                .Count();

            return new BankReconciliationVarianceDTO
            {
                CurrentBalance = currentBalance,
                StatementBalance = latest?.LedgerBalance,
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
