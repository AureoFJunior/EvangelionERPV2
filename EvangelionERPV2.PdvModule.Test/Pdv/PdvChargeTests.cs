using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EvangelionERPV2.PdvModule.Test
{
    /// <summary>
    /// The row that says a PIX charge exists, and what it cost us.
    /// </summary>
    public class PdvChargeTests : IDisposable
    {
        private readonly PdvTestContext _database = new();

        public void Dispose() => _database.Dispose();

        [Fact]
        public async Task TwoChargesCannotShareOneProviderIntentInOneTenant()
        {
            var enterpriseId = Guid.NewGuid();
            using var context = _database.CreateContext();
            context.PdvCharge.Add(Charge(enterpriseId, "pi_1"));
            await context.SaveChangesAsync();

            context.PdvCharge.Add(Charge(enterpriseId, "pi_1"));

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        [Fact]
        public async Task TwoTenantsMayEachHaveTheirOwnIntentIds()
        {
            using var context = _database.CreateContext();
            context.PdvCharge.Add(Charge(Guid.NewGuid(), "pi_1"));
            context.PdvCharge.Add(Charge(Guid.NewGuid(), "pi_1"));

            await context.SaveChangesAsync();

            Assert.Equal(2, await context.PdvCharge.CountAsync());
        }

        [Fact]
        public async Task ANewChargeIsPendingAndCostsNothingYet()
        {
            using var context = _database.CreateContext();
            var charge = Charge(Guid.NewGuid(), "pi_1");
            context.PdvCharge.Add(charge);
            await context.SaveChangesAsync();

            Assert.Equal((int)EnumPdvChargeStatus.Pending, charge.Status);
            Assert.Null(charge.FeeAmount);
            Assert.Null(charge.PaidAt);
        }

        private static PdvCharge Charge(Guid enterpriseId, string intentId) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            ClientSaleId = Guid.NewGuid(),
            Provider = "stripe",
            ProviderIntentId = intentId,
            Amount = 60m,
            Status = (int)EnumPdvChargeStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };
    }
}
