using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.PdvModule.Application.Services;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EvangelionERPV2.PdvModule.Test
{
    /// <summary>
    /// The reads a backoffice screen needs: which tills exist, which are open, and who overrode what.
    /// </summary>
    /// <remarks>
    /// Every one of them is tenant-scoped, and the terminal list is the one that would leak a
    /// credential verifier if anybody ever returned the entity instead of the projection.
    /// </remarks>
    public class PdvBackofficeTests : IDisposable
    {
        private readonly PdvTestContext _database = new();
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Guid _otherEnterpriseId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();

        public void Dispose() => _database.Dispose();

        [Fact]
        public async Task TheTerminalListNeverCarriesTheCredential()
        {
            // PdvTerminal stores a PBKDF2 verifier. The DTO has nowhere to put it, and this test
            // fails the day somebody returns the entity instead.
            using var context = _database.CreateContext();
            context.PdvTerminal.Add(PdvTerminal.Enroll(_enterpriseId, "CAIXA-01", "super-secret-credential"));
            await context.SaveChangesAsync();

            var terminals = await CreateService(context).GetTerminalsAsync(_enterpriseId);

            var terminal = Assert.Single(terminals);
            Assert.Equal("CAIXA-01", terminal.TerminalId);
            Assert.DoesNotContain("super-secret-credential",
                System.Text.Json.JsonSerializer.Serialize(terminal));
        }

        [Fact]
        public async Task TheTerminalListStopsAtTheTenantBoundary()
        {
            using var context = _database.CreateContext();
            context.PdvTerminal.Add(PdvTerminal.Enroll(_enterpriseId, "CAIXA-01", "mine"));
            context.PdvTerminal.Add(PdvTerminal.Enroll(_otherEnterpriseId, "CAIXA-99", "theirs"));
            await context.SaveChangesAsync();

            var terminals = await CreateService(context).GetTerminalsAsync(_enterpriseId);

            Assert.Equal("CAIXA-01", Assert.Single(terminals).TerminalId);
        }

        [Fact]
        public async Task RevokingATerminalStopsItsCredentialWorking()
        {
            using var context = _database.CreateContext();
            var terminal = PdvTerminal.Enroll(_enterpriseId, "CAIXA-01", "the-credential");
            context.PdvTerminal.Add(terminal);
            await context.SaveChangesAsync();

            Assert.True(terminal.Verify("the-credential"));
            Assert.True(await CreateService(context).RevokeTerminalAsync(terminal.Id, _enterpriseId));

            var stored = await context.PdvTerminal.AsNoTracking().SingleAsync();
            Assert.True(stored.IsRevoked);
            Assert.False(stored.Verify("the-credential"));
        }

        [Fact]
        public async Task RevokingTwice_AndRevokingSomebodyElsesTill_BothAnswerFalse()
        {
            // Idempotent, and another tenant's terminal must be indistinguishable from one that
            // never existed — an error that only fires for real ids is an enumeration oracle.
            using var context = _database.CreateContext();
            var mine = PdvTerminal.Enroll(_enterpriseId, "CAIXA-01", "mine");
            var theirs = PdvTerminal.Enroll(_otherEnterpriseId, "CAIXA-99", "theirs");
            context.PdvTerminal.AddRange(mine, theirs);
            await context.SaveChangesAsync();

            var service = CreateService(context);

            Assert.True(await service.RevokeTerminalAsync(mine.Id, _enterpriseId));
            Assert.False(await service.RevokeTerminalAsync(mine.Id, _enterpriseId));
            Assert.False(await service.RevokeTerminalAsync(theirs.Id, _enterpriseId));
            Assert.False(await service.RevokeTerminalAsync(Guid.NewGuid(), _enterpriseId));

            Assert.False((await context.PdvTerminal.AsNoTracking()
                .SingleAsync(row => row.Id == theirs.Id)).IsRevoked);
        }

        [Fact]
        public async Task OnlyOpenSessionsAreListed()
        {
            using var context = _database.CreateContext();
            var service = CreateService(context);

            var openId = Guid.NewGuid();
            await OpenSessionAsync(service, openId, "CAIXA-01");

            var closedId = Guid.NewGuid();
            await OpenSessionAsync(service, closedId, "CAIXA-02");
            await service.CloseSessionAsync(new Shared.DTOs.CloseCashSessionRequestDTO
            {
                ClientSessionId = closedId,
                CountedAmount = 200m,
                ClosedAt = DateTime.UtcNow
            }, _enterpriseId, _userId);

            var sessions = await service.GetOpenSessionsAsync(_enterpriseId);

            var session = Assert.Single(sessions);
            Assert.Equal("CAIXA-01", session.TerminalId);

            // The terminal's own id for the session, which is what every other endpoint takes.
            Assert.Equal(openId, session.Id);

            // And under its own name, so a caller does not have to know that `Id` means something
            // different here than it does on GetOpenSession.
            Assert.Equal(openId, session.ClientSessionId);
        }

        [Fact]
        public async Task TheOverrideAuditAnswersNewestFirstAndCanShowOnlyTheRefusals()
        {
            using var context = _database.CreateContext();
            var supervisor = Guid.NewGuid();

            context.PdvOverrideAudit.AddRange(
                Audit(_enterpriseId, supervisor, "pdv.item.void", succeeded: true, minutesAgo: 30),
                Audit(_enterpriseId, supervisor, "pdv.discount.override", succeeded: false, minutesAgo: 20),
                Audit(_enterpriseId, supervisor, "pdv.discount.override", succeeded: false, minutesAgo: 10),
                Audit(_otherEnterpriseId, supervisor, "pdv.item.void", succeeded: false, minutesAgo: 5));
            await context.SaveChangesAsync();

            var service = CreateService(context);

            var all = await service.GetOverrideAuditAsync(_enterpriseId, since: null, onlyRefused: null, take: 50);
            Assert.Equal(3, all.Count);
            Assert.True(all[0].OccurredAt > all[1].OccurredAt);

            // The refusals are the reason the table exists: a run of them is somebody guessing a PIN.
            var refused = await service.GetOverrideAuditAsync(_enterpriseId, since: null, onlyRefused: true, take: 50);
            Assert.Equal(2, refused.Count);
            Assert.All(refused, audit => Assert.False(audit.Succeeded));
        }

        [Fact]
        public async Task TheOverrideAuditWindowAndItsPageAreBothBounded()
        {
            using var context = _database.CreateContext();
            var supervisor = Guid.NewGuid();

            context.PdvOverrideAudit.AddRange(
                Audit(_enterpriseId, supervisor, "pdv.item.void", succeeded: false, minutesAgo: 5),
                Audit(_enterpriseId, supervisor, "pdv.item.void", succeeded: false, minutesAgo: 60 * 48));
            await context.SaveChangesAsync();

            var service = CreateService(context);

            var recent = await service.GetOverrideAuditAsync(
                _enterpriseId, since: DateTime.UtcNow.AddHours(-1), onlyRefused: null, take: 50);
            Assert.Single(recent);

            // A nonsense page size is clamped rather than refused; this is a screen, not an export.
            Assert.Single(await service.GetOverrideAuditAsync(
                _enterpriseId, since: null, onlyRefused: null, take: 1));
            Assert.Equal(2, (await service.GetOverrideAuditAsync(
                _enterpriseId, since: null, onlyRefused: null, take: 100_000)).Count);
        }

        private static PdvOverrideAudit Audit(Guid enterpriseId, Guid supervisor, string permission,
            bool succeeded, int minutesAgo) =>
            PdvOverrideAudit.Record(enterpriseId, Guid.NewGuid(), supervisor, Guid.NewGuid(),
                "CAIXA-01", Guid.NewGuid(), permission, succeeded, DateTime.UtcNow.AddMinutes(-minutesAgo));

        private async Task OpenSessionAsync(PdvService service, Guid clientSessionId, string terminalId) =>
            await service.OpenSessionAsync(new Shared.DTOs.OpenCashSessionRequestDTO
            {
                ClientSessionId = clientSessionId,
                TerminalId = terminalId,
                OpeningFloat = 200m,
                OpenedAt = DateTime.UtcNow
            }, _enterpriseId, _userId);

        private PdvService CreateService(AppDbContext context) =>
            new(context,
                new Repository<CashSession>(context),
                new Repository<CashMovement>(context),
                new Repository<PdvSale>(context),
                new Repository<PdvSalePayment>(context),
                new Mock<IOrderService<Order>>().Object,
                new Mock<IProductService<Product>>().Object,
                new Repository<PdvCharge>(context),
                PixProviderStubs.Object());
    }
}
