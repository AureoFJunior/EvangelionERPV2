using System.IO.Compression;
using System.Text;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// One month of movement as a zip of four CSVs — the "pacote do contador".
    /// </summary>
    /// <remarks>
    /// The roadmap's settled position is to <em>export to</em> the accountant's system rather than
    /// build accounting here, and this is that promise kept: no chart of accounts, no double entry,
    /// no journal. Four flat tables of what already happened.
    ///
    /// Each table is filtered by the date that table is actually booked on, not by one shared rule:
    /// a sale is booked when it was recorded, a payable when it falls due, a nota when it was
    /// issued, a stock movement when it moved. The period is half-open [first of month, first of
    /// next month) so a document on the last day of the month cannot fall into both packages.
    ///
    /// Payroll and férias payables are collapsed to one row per due date and kind. There is one
    /// payable per employee behind them, and the accountant books the folha as a single lançamento
    /// anyway — the aggregate keeps individual names and salaries out of a file that gets emailed.
    /// </remarks>
    public class AccountingExportService : IAccountingExportService
    {
        private readonly IRepository<Order> _orderRepository;
        private readonly IRepository<Bill> _billRepository;
        private readonly IRepository<Customer> _customerRepository;
        private readonly IRepository<PayableBill> _payableBillRepository;
        private readonly IRepository<Supplier> _supplierRepository;
        private readonly IRepository<NFeDocument> _nfeDocumentRepository;
        private readonly IRepository<StockMovement> _stockMovementRepository;
        private readonly IRepository<Product> _productRepository;

        public AccountingExportService(
            IRepository<Order> orderRepository,
            IRepository<Bill> billRepository,
            IRepository<Customer> customerRepository,
            IRepository<PayableBill> payableBillRepository,
            IRepository<Supplier> supplierRepository,
            IRepository<NFeDocument> nfeDocumentRepository,
            IRepository<StockMovement> stockMovementRepository,
            IRepository<Product> productRepository)
        {
            _orderRepository = orderRepository;
            _billRepository = billRepository;
            _customerRepository = customerRepository;
            _payableBillRepository = payableBillRepository;
            _supplierRepository = supplierRepository;
            _nfeDocumentRepository = nfeDocumentRepository;
            _stockMovementRepository = stockMovementRepository;
            _productRepository = productRepository;
        }

        public async Task<byte[]> BuildMonthlyAsync(
            Guid enterpriseId,
            int year,
            int month,
            CancellationToken cancellation = default)
        {
            if (enterpriseId == Guid.Empty)
                throw new ArgumentException("Enterprise is required.", nameof(enterpriseId));
            if (year < 2000 || year > 2100)
                throw new ArgumentOutOfRangeException(nameof(year), "Year is outside the supported range.");
            if (month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month), "Month must be between 1 and 12.");

            var from = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = from.AddMonths(1);

            var receivables = await BuildReceivablesAsync(enterpriseId, from, to);
            var payables = await BuildPayablesAsync(enterpriseId, from, to);
            var nfe = await BuildNFeAsync(enterpriseId, from, to);
            var stock = await BuildStockMovementsAsync(enterpriseId, from, to);

            // ponytail: the whole package is built in memory. A month is four flat tables of a
            // small business's movement; stream it to the response if a tenant ever makes that hurt.
            using var buffer = new MemoryStream();

            // leaveOpen: the archive must be disposed — that is what writes the central directory —
            // before ToArray() sees a complete zip, and disposing it must not take the stream with it.
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                await WriteEntryAsync(zip, "receivables.csv", receivables, cancellation);
                await WriteEntryAsync(zip, "payables.csv", payables, cancellation);
                await WriteEntryAsync(zip, "nfe.csv", nfe, cancellation);
                await WriteEntryAsync(zip, "stock-movements.csv", stock, cancellation);
            }

            return buffer.ToArray();
        }

        private async Task<string> BuildReceivablesAsync(Guid enterpriseId, DateTime from, DateTime to)
        {
            // GetPagedAsync, not GetAllAsync: OrderRepository's override Includes Customer,
            // OrderedProduct and Product, which for a month of sales means dragging every line item
            // across the wire to read four columns.
            var orders = (await _orderRepository.GetPagedAsync(
                    x => x.EnterpriseId == enterpriseId
                         && x.IsActive == true
                         && x.CreatedAt >= from
                         && x.CreatedAt < to))
                .Items;

            var liveBills = ReceivableResolver.SelectLiveBillsByOrder(
                await _billRepository.GetAllAsync(
                    x => x.Order != null
                         && x.Order.EnterpriseId == enterpriseId
                         && x.Order.CreatedAt >= from
                         && x.Order.CreatedAt < to
                         && (x.IsActive == null || x.IsActive == true)));

            // One read of the tenant's customer master, and only when there is something to name.
            // GetPagedAsync, not GetAllAsync: CustomerRepository's override throws
            // NotFoundDatabaseException on an empty result, which would turn "this tenant has no
            // customers yet" into a 204 with no package.
            var customerNames = new Dictionary<Guid, string>();
            if (orders.Count > 0)
            {
                customerNames = (await _customerRepository.GetPagedAsync(x => x.EnterpriseId == enterpriseId))
                    .Items
                    .GroupBy(x => x.Id)
                    .ToDictionary(x => x.Key, x => x.First().Name);
            }

            var csv = new StringBuilder(Csv.Line("Data", "Pedido", "Cliente", "Documento", "Vencimento", "Valor", "Status", "PagoEm", "ValorPago"));

            foreach (var order in orders.OrderBy(x => x.CreatedAt))
            {
                var bill = liveBills.GetValueOrDefault(order.Id);
                var dueDate = bill?.DueDate ?? order.PaymentScheduledDate;

                csv.Append(Csv.Line(
                    Csv.Date(order.CreatedAt),
                    order.Id.ToString(),
                    order.CustomerId.HasValue ? customerNames.GetValueOrDefault(order.CustomerId.Value, string.Empty) : string.Empty,
                    bill?.DocumentNumber ?? string.Empty,
                    Csv.Date(dueDate),
                    Csv.Money(ReceivableResolver.ResolveReceivableAmount(order, bill)),
                    ResolveReceivableStatus(order, bill),
                    Csv.Date(bill?.PaidAt),
                    bill?.PaidAmount is > 0 ? Csv.Money(bill.PaidAmount!.Value) : string.Empty));
            }

            return csv.ToString();
        }

        private async Task<string> BuildPayablesAsync(Guid enterpriseId, DateTime from, DateTime to)
        {
            var payables = (await _payableBillRepository.GetAllAsync(
                    x => x.EnterpriseId == enterpriseId
                         && (x.IsActive == null || x.IsActive == true)
                         && x.DueDate >= from
                         && x.DueDate < to))
                .ToList();

            // Same reason as the customers above, and it bites harder here: most tenants have no
            // suppliers at all, so the throwing override would 204 the whole month's package.
            var supplierNames = new Dictionary<Guid, string>();
            if (payables.Count > 0)
            {
                supplierNames = (await _supplierRepository.GetPagedAsync(x => x.EnterpriseId == enterpriseId))
                    .Items
                    .GroupBy(x => x.Id)
                    .ToDictionary(x => x.Key, x => x.First().Name);
            }

            var csv = new StringBuilder(Csv.Line("Vencimento", "Descricao", "Fornecedor", "Tipo", "Valor", "Pago", "PagoEm"));

            foreach (var bill in payables.Where(x => !IsPersonnel(x)).OrderBy(x => x.DueDate))
            {
                csv.Append(Csv.Line(
                    Csv.Date(bill.DueDate),
                    bill.Description,
                    bill.SupplierId.HasValue ? supplierNames.GetValueOrDefault(bill.SupplierId.Value, string.Empty) : string.Empty,
                    ((EnumPayableBillType)bill.BillType).ToString(),
                    Csv.Money(PayableAmount(bill)),
                    bill.IsPaid ? "sim" : "nao",
                    Csv.Date(bill.PaidAt)));
            }

            var personnel = payables
                .Where(IsPersonnel)
                .GroupBy(x => (Date: x.DueDate.Date, Type: (EnumPayableBillType)x.BillType))
                .OrderBy(x => x.Key.Date)
                .ThenBy(x => x.Key.Type);

            foreach (var group in personnel)
            {
                var label = group.Key.Type == EnumPayableBillType.Vacation ? "Férias" : "Folha de pagamento";

                csv.Append(Csv.Line(
                    Csv.Date(group.Key.Date),
                    $"{label} ({group.Count()} funcionários)",
                    string.Empty,
                    group.Key.Type.ToString(),
                    Csv.Money(group.Sum(PayableAmount)),
                    group.All(x => x.IsPaid) ? "sim" : "nao",
                    Csv.Date(group.Max(x => x.PaidAt))));
            }

            return csv.ToString();
        }

        private async Task<string> BuildNFeAsync(Guid enterpriseId, DateTime from, DateTime to)
        {
            // No XML blobs: the accountant's system wants the fiscal identity of the note, and one
            // month of XmlContent is megabytes of payload nothing in the package reads.
            var documents = await _nfeDocumentRepository.GetAllAsync(
                x => x.EnterpriseId == enterpriseId
                     && (x.IsActive == null || x.IsActive == true)
                     && (x.IssuedAt ?? x.CreatedAt) >= from
                     && (x.IssuedAt ?? x.CreatedAt) < to);

            var csv = new StringBuilder(Csv.Line("Chave", "Numero", "Serie", "Emissao", "Valor", "Status", "cStat"));

            foreach (var document in documents.OrderBy(x => x.IssuedAt ?? x.CreatedAt))
            {
                csv.Append(Csv.Line(
                    document.AccessKey,
                    document.Number,
                    document.Series,
                    Csv.Date(document.IssuedAt ?? document.CreatedAt),
                    Csv.Money(document.TotalValue),
                    document.Status.ToString(),
                    document.Cstat));
            }

            return csv.ToString();
        }

        private async Task<string> BuildStockMovementsAsync(Guid enterpriseId, DateTime from, DateTime to)
        {
            var movements = (await _stockMovementRepository.GetAllAsync(
                    x => x.EnterpriseId == enterpriseId
                         && x.CreatedAt >= from
                         && x.CreatedAt < to))
                .ToList();

            var productNames = new Dictionary<Guid, string>();
            if (movements.Count > 0)
            {
                productNames = (await _productRepository.GetPagedAsync(x => x.EnterpriseId == enterpriseId))
                    .Items
                    .GroupBy(x => x.Id)
                    .ToDictionary(x => x.Key, x => x.First().Name);
            }

            var csv = new StringBuilder(Csv.Line("Data", "Produto", "Tipo", "Quantidade", "SaldoApos", "Referencia"));

            foreach (var movement in movements.OrderBy(x => x.CreatedAt))
            {
                csv.Append(Csv.Line(
                    Csv.Date(movement.CreatedAt),
                    productNames.GetValueOrDefault(movement.ProductId, string.Empty),
                    ((EnumStockMovementType)movement.Type).ToString(),
                    Csv.Money(movement.Quantity),
                    Csv.Money(movement.BalanceAfter),
                    movement.ReferenceId?.ToString() ?? string.Empty));
            }

            return csv.ToString();
        }

        /// <summary>
        /// What actually leaves the bank. On a férias bill <c>Amount</c> is the netted forecast and
        /// <c>GrossAmount</c> is the cheque — the same rule M12's matcher uses.
        /// </summary>
        private static decimal PayableAmount(PayableBill bill) => bill.GrossAmount ?? bill.Amount;

        private static bool IsPersonnel(PayableBill bill) =>
            bill.BillType == (int)EnumPayableBillType.Payroll || bill.BillType == (int)EnumPayableBillType.Vacation;

        private static string ResolveReceivableStatus(Order order, Bill? bill)
        {
            if (ReceivableResolver.IsOrderRefunded(order))
                return "Refunded";

            return bill?.Status.ToString() ?? "SemBoleto";
        }

        private static async Task WriteEntryAsync(ZipArchive zip, string name, string content, CancellationToken cancellation)
        {
            await using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            await stream.WriteAsync(Csv.Encoding.GetPreamble(), cancellation);
            await stream.WriteAsync(Csv.Encoding.GetBytes(content), cancellation);
        }
    }
}
