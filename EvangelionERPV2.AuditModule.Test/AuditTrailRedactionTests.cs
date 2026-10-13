using System.Text.Json;

using EvangelionERPV2.AuditModule.Application.Services;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;

using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.AuditModule.Test
{
    /// <summary>
    /// The audit trail records every change to an audited entity, which makes it the one place
    /// personal data can be reconstructed from after it was deleted everywhere else. These tests
    /// pin the redaction, and — just as important — pin that ordinary business columns are still
    /// readable, because a trail that redacts everything records nothing.
    /// </summary>
    public class AuditTrailRedactionTests
    {
        [Fact]
        public async Task Insert_OfACustomer_RedactsEveryIdentifyingColumn()
        {
            var customerId = Guid.NewGuid();
            var databaseName = $"audit-redaction-{Guid.NewGuid()}";

            await using (var context = CreateContext(databaseName))
            {
                context.Customer.Add(new Customer
                {
                    Id = customerId,
                    EnterpriseId = Guid.NewGuid(),
                    Name = "Rei Ayanami",
                    Email = "rei@nerv.jp",
                    PhoneNumber = "11999998888",
                    Adress = "Rua das Flores, 100",
                    Document = "39053344705",
                    IsActive = true
                });

                await context.SaveChangesAsync();
            }

            var changes = await ChangesForAsync(databaseName, customerId);

            Assert.Equal("[redacted]", ValueOf(changes, "Name"));
            Assert.Equal("[redacted]", ValueOf(changes, "Email"));
            Assert.Equal("[redacted]", ValueOf(changes, "PhoneNumber"));
            Assert.Equal("[redacted]", ValueOf(changes, "Adress"));
            Assert.Equal("[redacted]", ValueOf(changes, "Document"));
        }

        [Fact]
        public async Task Edit_OfAPayableBill_RedactsTheRefundReasonAndKeepsTheMoney()
        {
            var billId = Guid.NewGuid();
            var databaseName = $"audit-redaction-{Guid.NewGuid()}";

            await using (var context = CreateContext(databaseName))
            {
                context.PayableBill.Add(new PayableBill
                {
                    Id = billId,
                    EnterpriseId = Guid.NewGuid(),
                    Description = "Fornecedor",
                    Amount = 100m,
                    DueDate = new DateTime(2026, 8, 1),
                    IsActive = true
                });

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(databaseName))
            {
                var bill = await context.PayableBill.FirstAsync(x => x.Id == billId);
                bill.Amount = 250m;
                bill.RefundReason = "Cliente Ikari pediu estorno";

                await context.SaveChangesAsync();
            }

            var changes = await ChangesForAsync(databaseName, billId, action: "EDIT");

            Assert.Equal("[redacted]", ValueOf(changes, "RefundReason"));

            Assert.Equal(250m, ((JsonElement)ValueOf(changes, "Amount")!).GetDecimal());
        }

        [Fact]
        public async Task SoftDelete_IsRecordedAsADeleteNotAnEdit()
        {
            var customerId = Guid.NewGuid();
            var databaseName = $"audit-redaction-{Guid.NewGuid()}";

            await using (var context = CreateContext(databaseName))
            {
                context.Customer.Add(new Customer
                {
                    Id = customerId,
                    EnterpriseId = Guid.NewGuid(),
                    Name = "Shinji",
                    IsActive = true
                });

                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(databaseName))
            {
                var customer = await context.Customer.FirstAsync(x => x.Id == customerId);
                customer.IsActive = false;

                await context.SaveChangesAsync();
            }

            await using var assertContext = CreateContext(databaseName);
            var actions = await assertContext.AuditTrails.AsNoTracking()
                .Where(x => x.EntityId == customerId)
                .Select(x => x.Action)
                .ToListAsync();

            Assert.Contains("ADD", actions);
            Assert.Contains("DELETE", actions);
            Assert.DoesNotContain("EDIT", actions);
        }

        private static async Task<Dictionary<string, Dictionary<string, JsonElement>>> ChangesForAsync(
            string databaseName, Guid entityId, string action = "ADD")
        {
            await using var context = CreateContext(databaseName);

            var trail = await context.AuditTrails.AsNoTracking()
                .FirstAsync(x => x.EntityId == entityId && x.Action == action);

            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(trail.ChangesJson!)!;
        }

        /// <summary>The recorded value of one property, whichever side of the change it sits on.</summary>
        private static object? ValueOf(Dictionary<string, Dictionary<string, JsonElement>> changes, string propertyName)
        {
            var change = changes[propertyName];
            var element = change.TryGetValue("NewValue", out var newValue) && newValue.ValueKind != JsonValueKind.Null
                ? newValue
                : change["OldValue"];

            return element.ValueKind == JsonValueKind.String ? element.GetString() : element;
        }

        private static AppDbContext CreateContext(string databaseName)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;

            return new AppDbContext(options, httpContextAccessor: null, new AuditTrailEntryFactory());
        }
    }
}
