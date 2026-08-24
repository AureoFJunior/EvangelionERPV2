using System.IO.Compression;
using System.Linq.Expressions;
using System.Text;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Moq;

namespace EvangelionERPV2.BillsModule.Test
{
    public class AccountingExportServiceTests
    {
        private static readonly Guid EnterpriseId = Guid.NewGuid();
        private const int Year = 2026;
        private const int Month = 3;
        private static readonly DateTime InsidePeriod = new(Year, Month, 15, 10, 0, 0, DateTimeKind.Utc);

        [Fact]
        public async Task BuildMonthly_ShouldProduceExactlyTheFourTablesTheAccountantGets()
        {
            var package = await BuildSut().BuildMonthlyAsync(EnterpriseId, Year, Month);

            using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);

            Assert.Equal(
                ["receivables.csv", "payables.csv", "nfe.csv", "stock-movements.csv"],
                zip.Entries.Select(x => x.Name).ToArray());
        }

        [Fact]
        public async Task BuildMonthly_ShouldQuoteAFieldCarryingTheSeparatorAQuoteAndANewline()
        {
            // Excel in pt-BR splits on ';', so an unquoted customer name containing one silently
            // shifts every column after it into the wrong header.
            var customer = BuildCustomer("Nerv; \"Tokyo-3\"\r\nHQ");
            var order = BuildOrder(100, customer, InsidePeriod);

            var csv = await ReadEntryAsync(
                await BuildSut(orders: [order], customers: [customer]).BuildMonthlyAsync(EnterpriseId, Year, Month),
                "receivables.csv");

            Assert.Contains("\"Nerv; \"\"Tokyo-3\"\"\r\nHQ\"", csv);
            Assert.Equal(customer.Name, ParseField(csv, row: 1, column: 2));
        }

        [Theory]
        [InlineData(2026, 2, 28, false)]
        [InlineData(2026, 3, 1, true)]
        [InlineData(2026, 3, 31, true)]
        [InlineData(2026, 4, 1, false)]
        public async Task BuildMonthly_ShouldTakeTheMonthAsAHalfOpenInterval(int year, int month, int day, bool expected)
        {
            var order = BuildOrder(500, null, new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc));

            var csv = await ReadEntryAsync(
                await BuildSut(orders: [order]).BuildMonthlyAsync(EnterpriseId, Year, Month),
                "receivables.csv");

            Assert.Equal(expected, DataRows(csv).Count == 1);
        }

        [Fact]
        public async Task BuildMonthly_ShouldReportTheBoletoAsTheTruthAboutAReceivable()
        {
            var order = BuildOrder(1000, null, InsidePeriod);
            var bill = new Bill
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Order = order,
                IsActive = true,
                DocumentNumber = "000123",
                Amount = 1000,
                DueDate = InsidePeriod.Date.AddDays(20),
                Status = BillStatus.Paid,
                PaidAt = InsidePeriod.Date.AddDays(3),
                PaidAmount = 1012.50m
            };

            var csv = await ReadEntryAsync(
                await BuildSut(orders: [order], bills: [bill]).BuildMonthlyAsync(EnterpriseId, Year, Month),
                "receivables.csv");

            var row = Assert.Single(DataRows(csv));
            Assert.Equal("000123", ParseField(csv, 1, 3));
            Assert.Equal("1012.50", ParseField(csv, 1, 5));
            Assert.Equal("Paid", ParseField(csv, 1, 6));
            Assert.Contains("2026-03-18", row);
        }

        [Fact]
        public async Task BuildMonthly_ShouldCollapsePayrollToOneRow_AndNeverNameAnEmployee()
        {
            // One PayableBill per employee is how payroll is stored; the accountant books the folha
            // as a single lançamento, and the package is an email attachment.
            var payables = new List<PayableBill>
            {
                BuildPayable("Folha de pagamento 03/2026 - Shinji Ikari", 3000, EnumPayableBillType.Payroll),
                BuildPayable("Folha de pagamento 03/2026 - Rei Ayanami", 2000, EnumPayableBillType.Payroll),
                BuildPayable("Aluguel", 4500, EnumPayableBillType.OperatingCost)
            };

            var csv = await ReadEntryAsync(
                await BuildSut(payables: payables).BuildMonthlyAsync(EnterpriseId, Year, Month),
                "payables.csv");

            Assert.Equal(2, DataRows(csv).Count);
            Assert.DoesNotContain("Shinji", csv);
            Assert.DoesNotContain("Rei Ayanami", csv);
            Assert.Contains("Folha de pagamento (2 funcionários)", csv);
            Assert.Contains("5000.00", csv);
        }

        [Fact]
        public async Task BuildMonthly_ShouldReportTheGrossOfAFeriasBill_NotTheNettedForecast()
        {
            // PayableBill.Amount on a férias bill is net of the provision payroll already accrued.
            // What leaves the bank is GrossAmount — the same rule the bank matcher uses.
            var ferias = BuildPayable("Férias", 400, EnumPayableBillType.Vacation);
            ferias.GrossAmount = 1800;

            var csv = await ReadEntryAsync(
                await BuildSut(payables: [ferias]).BuildMonthlyAsync(EnterpriseId, Year, Month),
                "payables.csv");

            Assert.Contains("1800.00", csv);
            Assert.DoesNotContain("400.00", csv);
        }

        [Fact]
        public async Task BuildMonthly_ShouldExportTheNotaIdentity_AndNoXmlBlob()
        {
            var document = new NFeDocument
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                IsActive = true,
                AccessKey = new string('3', 44),
                Number = "42",
                Series = "1",
                IssuedAt = InsidePeriod,
                TotalValue = 250.40m,
                Status = NFeStatus.Authorized,
                Cstat = "100",
                XmlContent = "<nfeProc>should never reach the accountant</nfeProc>"
            };

            var csv = await ReadEntryAsync(
                await BuildSut(nfeDocuments: [document]).BuildMonthlyAsync(EnterpriseId, Year, Month),
                "nfe.csv");

            Assert.Contains(new string('3', 44), csv);
            Assert.Contains("250.40", csv);
            Assert.Contains("100", csv);
            Assert.DoesNotContain("nfeProc", csv);
        }

        [Fact]
        public async Task BuildMonthly_ShouldNameTheProductBehindEachStockMovement()
        {
            var product = new Product { Id = Guid.NewGuid(), EnterpriseId = EnterpriseId, IsActive = true, Name = "Plugsuit" };
            var movement = new StockMovement
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                IsActive = true,
                ProductId = product.Id,
                Quantity = -3,
                BalanceAfter = 17,
                Type = (int)EnumStockMovementType.Sale,
                CreatedAt = InsidePeriod
            };

            var csv = await ReadEntryAsync(
                await BuildSut(products: [product], stockMovements: [movement]).BuildMonthlyAsync(EnterpriseId, Year, Month),
                "stock-movements.csv");

            Assert.Equal("Plugsuit", ParseField(csv, 1, 1));
            Assert.Equal("Sale", ParseField(csv, 1, 2));
            Assert.Equal("-3.00", ParseField(csv, 1, 3));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(13)]
        public async Task BuildMonthly_ShouldRefuseAMonthThatDoesNotExist(int month)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => BuildSut().BuildMonthlyAsync(EnterpriseId, Year, month));
        }

        [Fact]
        public async Task BuildMonthly_ShouldRefuseAnAbsentTenant()
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => BuildSut().BuildMonthlyAsync(Guid.Empty, Year, Month));
        }

        private static AccountingExportService BuildSut(
            List<Order>? orders = null,
            List<Bill>? bills = null,
            List<Customer>? customers = null,
            List<PayableBill>? payables = null,
            List<Supplier>? suppliers = null,
            List<NFeDocument>? nfeDocuments = null,
            List<StockMovement>? stockMovements = null,
            List<Product>? products = null)
        {
            return new AccountingExportService(
                Repo(orders ?? []).Object,
                Repo(bills ?? []).Object,
                // Customers and suppliers behave like their real repositories, whose GetAllAsync
                // override throws instead of returning an empty list — a tenant with neither is
                // the common case, and it used to 204 the whole package.
                Repo(customers ?? [], throwsWhenEmpty: true).Object,
                Repo(payables ?? []).Object,
                Repo(suppliers ?? [], throwsWhenEmpty: true).Object,
                Repo(nfeDocuments ?? []).Object,
                Repo(stockMovements ?? []).Object,
                Repo(products ?? []).Object);
        }

        private static async Task<string> ReadEntryAsync(byte[] package, string entryName)
        {
            using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
            var entry = zip.GetEntry(entryName);
            Assert.NotNull(entry);

            using var buffer = new MemoryStream();
            await using (var stream = entry!.Open())
                await stream.CopyToAsync(buffer);

            // Csv writes a BOM so Excel reads the accents; strip it before asserting on text.
            return new StreamReader(new MemoryStream(buffer.ToArray()), Csv.Encoding, true).ReadToEnd();
        }

        /// <summary>Everything after the header, with the quoted newlines still inside their field.</summary>
        private static List<string> DataRows(string csv) => SplitRows(csv).Skip(1).ToList();

        private static string ParseField(string csv, int row, int column) => SplitFields(SplitRows(csv)[row])[column];

        private static List<string> SplitRows(string csv)
        {
            var rows = new List<string>();
            var current = new StringBuilder();
            var quoted = false;

            for (var i = 0; i < csv.Length; i++)
            {
                var character = csv[i];

                if (character == '"')
                    quoted = !quoted;

                if (!quoted && character == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                {
                    rows.Add(current.ToString());
                    current.Clear();
                    i++;
                    continue;
                }

                current.Append(character);
            }

            if (current.Length > 0)
                rows.Add(current.ToString());

            return rows;
        }

        private static List<string> SplitFields(string row)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            var quoted = false;

            for (var i = 0; i < row.Length; i++)
            {
                var character = row[i];

                if (character == '"')
                {
                    if (quoted && i + 1 < row.Length && row[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                        continue;
                    }

                    quoted = !quoted;
                    continue;
                }

                if (!quoted && character == Csv.Separator)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(character);
            }

            fields.Add(current.ToString());
            return fields;
        }

        private static Customer BuildCustomer(string name) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            IsActive = true,
            Name = name,
            Email = "contact@nerv.jp",
            PhoneNumber = "+55 11 90000-0000"
        };

        private static Order BuildOrder(decimal total, Customer? customer, DateTime createdAt)
        {
            customer ??= BuildCustomer("Rei");

            return new Order
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                IsActive = true,
                TotalValue = total,
                Status = (int)EnumOrderStatus.Pending,
                CustomerId = customer.Id,
                Customer = customer,
                CreatedAt = createdAt,
                PaymentScheduledDate = createdAt.Date.AddDays(30)
            };
        }

        private static PayableBill BuildPayable(string description, decimal amount, EnumPayableBillType type) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            IsActive = true,
            Description = description,
            Amount = amount,
            BillType = (int)type,
            DueDate = InsidePeriod.Date.AddDays(5)
        };

        private static Mock<IRepository<TEntity>> Repo<TEntity>(List<TEntity> records, bool throwsWhenEmpty = false)
            where TEntity : BaseEntity
        {
            var repo = new Mock<IRepository<TEntity>>();

            repo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<TEntity, bool>>?>()))
                .ReturnsAsync((Expression<Func<TEntity, bool>>? predicate) =>
                {
                    var result = predicate == null ? records.ToList() : records.Where(predicate.Compile()).ToList();
                    if (throwsWhenEmpty && result.Count == 0)
                        throw new NotFoundDatabaseException();

                    return result;
                });

            repo.Setup(x => x.GetPagedAsync(
                    It.IsAny<Expression<Func<TEntity, bool>>?>(),
                    It.IsAny<Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync((
                    Expression<Func<TEntity, bool>>? predicate,
                    Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? _,
                    int? __,
                    int? ___) =>
                {
                    var items = predicate == null
                        ? records.ToList()
                        : records.Where(predicate.Compile()).ToList();

                    return ((IReadOnlyList<TEntity>)items, items.Count);
                });

            return repo;
        }
    }
}
