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
    /// <remarks>
    /// The identity decisions are the whole subject. A contingency note is numbered while SEFAZ
    /// cannot be asked anything, so it takes its own series; its tpEmis is baked into the chave and
    /// must reach the transport unchanged; and dhCont/xJust are committed before transmission,
    /// because a note handed to a customer still owes SEFAZ a transmission that may be days away.
    /// </remarks>
    public class NFCeOfflineIssuanceTests
    {
        private static readonly DateTime EnteredAt = new(2026, 8, 31, 14, 30, 0, DateTimeKind.Utc);
        private const string Justification = "SEFAZ inacessivel no momento da venda";

        private static NFeContingency Contingency() =>
            new() { EnteredAt = EnteredAt, Justification = Justification };

        [Fact]
        public async Task AnOfflineNote_TakesTheContingencySeries_NotTheOnlineOne()
        {
            // Sharing the online series would let the counter and a later online note claim the
            // same nNF, and the loser is a rejected duplicate for a sale that already happened.
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
            // The chave's 35th digit is tpEmis. A transport that built the XML as normal would ship
            // a document whose key describes a different one.
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
            // A note printed and handed over still owes SEFAZ a transmission. If dhCont were only
            // written after a successful round trip, the ones that never made it — precisely the
            // ones that matter — would carry nothing to find them by.
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
