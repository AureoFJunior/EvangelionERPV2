using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EvangelionERPV2.PdvModule.Test
{
    /// <summary>
    /// The row that says a PIX charge exists, and what it cost us.
    /// </summary>
    /// <remarks>
    /// The unique index is the whole point: a terminal that resends StartPixCharge after a lost
    /// response must find the intent it already created, not open a second one against the same
    /// basket — a customer who pays twice is the failure this table exists to prevent.
    /// </remarks>
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
            // FeeAmount is null until a webhook says what Stripe actually took. Defaulting it to zero
            // would put a made-up number into the only measurement this design keeps.
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
