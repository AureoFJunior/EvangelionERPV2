using System.Net;
using EvangelionERPV2.BillsModule.Application.Configs;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Options;
using Serilog;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// The régua de cobrança: one reminder per receivable per point on the ladder, and never twice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sweep is deliberately cross-tenant. It runs outside a request, where
    /// <c>AppDbContext.AmbientEnterpriseId</c> is <see cref="Guid.Empty"/> and the tenant query
    /// filter is disabled by design — the same arrangement <c>EmployeeVacationLapseService</c>
    /// relies on. Each tenant is then handled explicitly, and only if it opted in.
    /// </para>
    /// <para>
    /// The log row is written <b>before</b> the email is sent, and that order is load-bearing. A
    /// reminder that was logged but never sent is recoverable — the customer appears on the
    /// inadimplência list and a human chases them. A reminder sent twice is not recoverable: the
    /// customer has already read it.
    /// </para>
    /// <para>
    /// The message states the amount and the due date and <b>no late fee</b>. Juros de mora and
    /// multa are not modelled anywhere in this system (see <c>docs/fiscal/TODO-manual.md</c>,
    /// M-B12), and inventing a rate in an email to a customer is a claim the ERP cannot back.
    /// </para>
    /// </remarks>
    public class DunningService
    {
        private readonly IRepository<Enterprise> _enterpriseRepository;
        private readonly IRepository<Order> _orderRepository;
        private readonly IRepository<Bill> _billRepository;
        private readonly IRepository<Customer> _customerRepository;
        private readonly IRepository<DunningLog> _dunningLogRepository;
        private readonly Func<IEmailService<EmailStructure>> _emailServiceFactory;
        private readonly DunningSettings _settings;

        public DunningService(
            IRepository<Enterprise> enterpriseRepository,
            IRepository<Order> orderRepository,
            IRepository<Bill> billRepository,
            IRepository<Customer> customerRepository,
            IRepository<DunningLog> dunningLogRepository,
            Func<IEmailService<EmailStructure>> emailServiceFactory,
            IOptions<DunningSettings> settings)
        {
            _enterpriseRepository = enterpriseRepository;
            _orderRepository = orderRepository;
            _billRepository = billRepository;
            _customerRepository = customerRepository;
            _dunningLogRepository = dunningLogRepository;
            _emailServiceFactory = emailServiceFactory;
            _settings = settings?.Value ?? new DunningSettings();
        }

        /// <summary>Runs one pass across every tenant that opted in.</summary>
        /// <returns>How many reminders were sent.</returns>
        public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
        {
            if (!_settings.Enabled)
                return 0;

            var offsets = (_settings.OffsetDays ?? []).Distinct().OrderBy(x => x).ToArray();
            if (offsets.Length == 0)
                return 0;

            var tenants = (await _enterpriseRepository.GetAllAsync(
                    x => x.IsActive == true && x.ShouldSendDunning))
                .ToList();

            var sent = 0;
            foreach (var tenant in tenants)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    sent += await SweepTenantAsync(tenant, offsets, cancellationToken);
                }
                catch (Exception ex)
                {
                    // One tenant's bad data must not stop the rest of the installation being chased.
                    Log.Logger.Error(
                        "Dunning sweep failed for one tenant. EnterpriseId={EnterpriseId} ErrorType={ErrorType}",
                        tenant.Id,
                        ex.GetType().Name);
                }
            }

            return sent;
        }

        private async Task<int> SweepTenantAsync(Enterprise tenant, int[] offsets, CancellationToken cancellationToken)
        {
            var today = DateTime.UtcNow.Date;

            // The window the ladder can possibly reach, so the date filter stays in SQL. Offsets are
            // days *past* due, so the earliest due date of interest is today minus the largest one.
            var earliestDueDate = today.AddDays(-offsets.Max());
            var latestDueDate = today.AddDays(-offsets.Min()).AddDays(1);

            var openOrders = (await _orderRepository.GetPagedAsync(
                    x => x.EnterpriseId == tenant.Id
                         && x.IsActive == true
                         && x.TotalValue > 0
                         && x.RefundedAt == null
                         && x.Status != (int)EnumOrderStatus.Refund
                         && x.Status != (int)EnumOrderStatus.Paid
                         && x.Status != (int)EnumOrderStatus.Finished))
                .Items;

            if (openOrders.Count == 0)
                return 0;

            var liveBills = ReceivableResolver.SelectLiveBillsByOrder(
                await _billRepository.GetAllAsync(
                    x => x.Order != null
                         && x.Order.EnterpriseId == tenant.Id
                         && (x.IsActive == null || x.IsActive == true)
                         && (x.Status == BillStatus.Registered || x.Status == BillStatus.Paid)));

            var customersById = (await _customerRepository.GetAllAsync(
                    x => x.EnterpriseId == tenant.Id && x.IsActive == true))
                .ToDictionary(x => x.Id, x => x);

            var alreadySent = (await _dunningLogRepository.GetAllAsync(x => x.EnterpriseId == tenant.Id))
                .Select(x => (x.OrderId, x.OffsetDays))
                .ToHashSet();

            var sent = 0;

            foreach (var order in openOrders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (sent >= _settings.MaxRemindersPerTenantPerPass)
                {
                    Log.Logger.Warning(
                        "Dunning pass hit its per-tenant cap. EnterpriseId={EnterpriseId} Cap={Cap}",
                        tenant.Id,
                        _settings.MaxRemindersPerTenantPerPass);
                    break;
                }

                var bill = liveBills.GetValueOrDefault(order.Id);
                if (bill is { Status: BillStatus.Paid })
                    continue;

                var amount = ReceivableResolver.ResolveReceivableAmount(order, bill);
                if (amount <= 0)
                    continue;

                var dueDate = (bill?.DueDate ?? order.Payday ?? order.PaymentScheduledDate).Date;
                if (dueDate < earliestDueDate || dueDate >= latestDueDate)
                    continue;

                var offset = (today - dueDate).Days;
                if (!offsets.Contains(offset) || alreadySent.Contains((order.Id, offset)))
                    continue;

                var customer = order.CustomerId.HasValue
                    ? customersById.GetValueOrDefault(order.CustomerId.Value)
                    : null;

                var recipient = customer?.Email?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(recipient))
                    continue;

                if (await TrySendAsync(tenant, order, customer!, recipient, amount, dueDate, offset, bill))
                    sent++;
            }

            return sent;
        }

        private async Task<bool> TrySendAsync(
            Enterprise tenant,
            Order order,
            Customer customer,
            string recipient,
            decimal amount,
            DateTime dueDate,
            int offset,
            Bill? bill)
        {
            try
            {
                // Logged first, then sent: a lost reminder is recoverable from the inadimplência
                // list, a duplicated one is already in the customer's inbox.
                await _dunningLogRepository.CreateAsync(new DunningLog
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = tenant.Id,
                    OrderId = order.Id,
                    OffsetDays = offset,
                    SentAt = DateTime.UtcNow,
                    RecipientEmail = recipient,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });

                await _dunningLogRepository.CommitAsync();
            }
            catch (Exception ex)
            {
                // The unique index landing here means a sibling replica already took this one.
                Log.Logger.Information(
                    "Dunning reminder skipped, already logged. OrderId={OrderId} Offset={Offset} ErrorType={ErrorType}",
                    order.Id,
                    offset,
                    ex.GetType().Name);

                return false;
            }

            try
            {
                var email = new EmailStructure(
                    BuildBody(customer, amount, dueDate, offset, bill),
                    BuildSubject(offset),
                    new List<string> { recipient });

                await _emailServiceFactory().SendManualEmail(email, tenant);
                return true;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(
                    "Dunning reminder was logged but could not be enqueued. OrderId={OrderId} ErrorType={ErrorType}",
                    order.Id,
                    ex.GetType().Name);

                return false;
            }
        }

        private static string BuildSubject(int offset) => offset switch
        {
            < 0 => "Lembrete de vencimento",
            0 => "Vence hoje",
            _ => "Pagamento em atraso"
        };

        private static string BuildBody(Customer customer, decimal amount, DateTime dueDate, int offset, Bill? bill)
        {
            var name = WebUtility.HtmlEncode(customer.Name ?? string.Empty);

            var lead = offset switch
            {
                < 0 => $"<p>Olá {name}, este é um lembrete: seu pagamento vence em <strong>{dueDate:dd/MM/yyyy}</strong>.</p>",
                0 => $"<p>Olá {name}, seu pagamento vence <strong>hoje</strong>.</p>",
                _ => $"<p>Olá {name}, consta em aberto um pagamento vencido em <strong>{dueDate:dd/MM/yyyy}</strong> ({offset} dia(s)).</p>"
            };

            // No juros and no multa: neither is modelled anywhere in this system, and a number
            // invented here is a claim nobody can honour.
            var body = $"""
                {lead}
                <p>Valor: <strong>{amount:N2}</strong></p>
                """;

            if (bill != null && !string.IsNullOrWhiteSpace(bill.DigitableLine))
            {
                body += $"""
                    <p>Linha digitável:<br/><strong>{WebUtility.HtmlEncode(bill.DigitableLine)}</strong></p>
                    """;
            }

            body += "<p>Se o pagamento já foi efetuado, por favor desconsidere esta mensagem.</p>";
            return body;
        }
    }
}
