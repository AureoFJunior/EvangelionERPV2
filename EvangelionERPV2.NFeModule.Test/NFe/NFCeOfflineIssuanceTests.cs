using EvangelionERPV2.NFeModule.Infra.Xml;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using Moq;
using Xunit;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    /// <summary>
    /// Issuing an NFC-e off-line: the shop keeps trading with SEFAZ unreachable.
    /// </summary>
    public class NFCeOfflineIssuanceTests
    {
        private static readonly DateTime EnteredAt = new(2026, 8, 31, 14, 30, 0, DateTimeKind.Utc);
        private const string Justification = "SEFAZ inacessivel no momento da venda";

        private static NFeContingency Contingency() =>
            new() { EnteredAt = EnteredAt, Justification = Justification };

        [Fact]
        public async Task AnOfflineNote_TakesTheContingencySeries_NotTheOnlineOne()
        {
            var context = new NFeIssuanceWriteAheadTests.IssuanceContext();
            Authorize(context);

            await context.Service.IssueAsync(
                context.OrderId, context.EnterpriseId, NFeDocumentType.NFCe, Contingency());

            context.NFeRepositoryCustom.Verify(
                x => x.AllocateNextNumberAsync(context.EnterpriseId, 65, "9", 2, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task AnOnlineNFCe_StillTakesTheOnlineSeries()
        {
            var context = new NFeIssuanceWriteAheadTests.IssuanceContext();
            Authorize(context);

            await context.Service.IssueAsync(context.OrderId, context.EnterpriseId, NFeDocumentType.NFCe);

            context.NFeRepositoryCustom.Verify(
                x => x.AllocateNextNumberAsync(context.EnterpriseId, 65, "1", 2, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task TheTransportIsToldItIsContingency_AndWhenAndWhy()
        {
            var context = new NFeIssuanceWriteAheadTests.IssuanceContext();
            NFeIssueContext? seen = null;

            context.Provider
                .Setup(x => x.IssueAsync(It.IsAny<NFeIssueContext>(), It.IsAny<CancellationToken>()))
                .Callback<NFeIssueContext, CancellationToken>((issued, _) => seen = issued)
                .ReturnsAsync(Authorized());

            await context.Service.IssueAsync(
                context.OrderId, context.EnterpriseId, NFeDocumentType.NFCe, Contingency());

            Assert.NotNull(seen);
            Assert.Equal(9, seen!.TipoEmissao);
            Assert.True(seen.IsOfflineContingency);
            Assert.Equal(EnteredAt, seen.ContingencyEnteredAt);
            Assert.Equal(Justification, seen.ContingencyJustification);
        }

        [Fact]
        public async Task TheContingencyIsCommittedBeforeTransmission()
        {
            var context = new NFeIssuanceWriteAheadTests.IssuanceContext();
            NFeDocument? atTransmission = null;

            context.Provider
                .Setup(x => x.IssueAsync(It.IsAny<NFeIssueContext>(), It.IsAny<CancellationToken>()))
                .Callback(() => atTransmission = new NFeDocument
                {
                    TipoEmissao = context.Persisted!.TipoEmissao,
                    ContingencyEnteredAt = context.Persisted.ContingencyEnteredAt,
                    ContingencyJustification = context.Persisted.ContingencyJustification
                })
                .ReturnsAsync(Authorized());

            await context.Service.IssueAsync(
                context.OrderId, context.EnterpriseId, NFeDocumentType.NFCe, Contingency());

            Assert.NotNull(atTransmission);
            Assert.Equal(9, atTransmission!.TipoEmissao);
            Assert.Equal(EnteredAt, atTransmission.ContingencyEnteredAt);
            Assert.Equal(Justification, atTransmission.ContingencyJustification);
        }

        [Fact]
        public async Task AnOrdinaryNote_RecordsNoContingencyAtAll()
        {
            var context = new NFeIssuanceWriteAheadTests.IssuanceContext();
            Authorize(context);

            await context.Service.IssueAsync(context.OrderId, context.EnterpriseId, NFeDocumentType.NFCe);

            Assert.Equal(1, context.Persisted!.TipoEmissao);
            Assert.Null(context.Persisted.ContingencyEnteredAt);
            Assert.Null(context.Persisted.ContingencyJustification);
        }

        /// <summary>
        /// tpEmis 9 is the NFC-e offline mode. Modelo 55 has its own contingency modes and none of
        /// them is this one.
        /// </summary>
        [Fact]
        public async Task Modelo55_CannotBeIssuedOffline()
        {
            var context = new NFeIssuanceWriteAheadTests.IssuanceContext();
            Authorize(context);

            await Assert.ThrowsAsync<InsertDatabaseException>(() => context.Service.IssueAsync(
                context.OrderId, context.EnterpriseId, NFeDocumentType.NFe, Contingency()));
        }

        /// <summary>
        /// The path the till actually takes: an online attempt that provably never left the
        /// process, then the same sale re-issued off-line.
        /// </summary>
        [Fact]
        public async Task AContingencyRetryOfAFailedOnlineNote_TakesAFreshOfflineAllocation()
        {
            var context = new NFeIssuanceWriteAheadTests.IssuanceContext();
            Authorize(context);
            context.Seed(FailedOnlineNFCe(context));

            var result = await context.Service.IssueAsync(
                context.OrderId, context.EnterpriseId, NFeDocumentType.NFCe, Contingency());

            context.NFeRepositoryCustom.Verify(
                x => x.AllocateNextNumberAsync(context.EnterpriseId, 65, "9", 2, It.IsAny<CancellationToken>()),
                Times.Once);

            Assert.Equal(9, result!.TipoEmissao);
            Assert.Equal("9", result.AccessKey.Substring(34, 1));
            Assert.Equal("9", result.Series);
        }

        /// <summary>An online retry of an online failure still reuses its number — nothing changed.</summary>
        [Fact]
        public async Task AnOnlineRetryOfAFailedOnlineNote_KeepsItsNumber()
        {
            var context = new NFeIssuanceWriteAheadTests.IssuanceContext();
            Authorize(context);
            var failed = FailedOnlineNFCe(context);
            context.Seed(failed);

            var result = await context.Service.IssueAsync(
                context.OrderId, context.EnterpriseId, NFeDocumentType.NFCe);

            context.VerifyAllocations(Times.Never());
            Assert.Equal(failed.AccessKey, result!.AccessKey);
        }

        /// <summary>An NFC-e that was claimed and numbered online, and never transmitted.</summary>
        private static NFeDocument FailedOnlineNFCe(NFeIssuanceWriteAheadTests.IssuanceContext context) => new()
        {
            Id = Guid.NewGuid(),
            OrderId = context.OrderId,
            EnterpriseId = context.EnterpriseId,
            Type = NFeDocumentType.NFCe,
            Model = 65,
            Status = NFeStatus.Error,
            AccessKey = NFeAccessKeyBuilder.Build(
                43, EmitterNow(), "11222333000181", 65, 1, 7, 1,
                NFeAccessKeyBuilder.GenerateCodigoNumerico(7)),
            Number = "7",
            Series = "1",
            Environment = "Homologation",
            TipoEmissao = 1,
            TransmittedAtUtc = null,
            IsActive = true
        };

        /// <summary>Mirrors NFeService.EmitterNow — the chave's AAMM is in the emitter's timezone.</summary>
        private static DateTime EmitterNow()
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return DateTime.UtcNow.AddHours(-3);
            }
        }

        private static void Authorize(NFeIssuanceWriteAheadTests.IssuanceContext context) =>
            context.Provider
                .Setup(x => x.IssueAsync(It.IsAny<NFeIssueContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Authorized());

        private static NFeProviderResult Authorized() => new()
        {
            Protocol = "PROT-1",
            XmlContent = "<xml />",
            IssuedAt = DateTime.UtcNow,
            TotalValue = 100,
            Status = NFeStatus.Authorized
        };
    }
}
