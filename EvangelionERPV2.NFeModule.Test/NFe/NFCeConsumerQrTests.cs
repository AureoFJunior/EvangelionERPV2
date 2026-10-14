using EvangelionERPV2.NFeModule.Infra.Providers;
using Moq;
using EvangelionERPV2.Shared.Entities;
using Xunit;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    /// <summary>
    /// The consumer QR on an NFCe, adopted through the reconciler — the same `ApplyOutcome` the
    /// issue path uses. Until this landed, <c>NFeDocument.QrCode</c> and <c>UrlChave</c> were
    /// columns that only ever got cleared: an NFCe would authorise and the customer would have
    /// nothing to scan.
    /// </summary>
    public class NFCeConsumerQrTests
    {
        private const string Qr = "https://www.sefaz.rs.gov.br/nfce/qrcode?p=4325...|2|1|1|abc";
        private const string UrlChave = "https://www.sefaz.rs.gov.br/nfce/consulta";

        [Fact]
        public async Task AnAuthorisedNFCe_KeepsItsQrAndItsUrl()
        {
            var context = new NFeReconciliationServiceTests.ReconciliationContext();
            var document = context.Stale(NFeReconciliationServiceTests.TransmittedDocument());

            context.Provider
                .Setup(x => x.ConsultAsync(document.AccessKey, It.IsAny<EnterpriseFiscalSettings>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NFeProviderResult
                {
                    Status = NFeStatus.Authorized,
                    Cstat = "100",
                    Protocol = "135260000000001",
                    QrCode = Qr,
                    UrlChave = UrlChave
                });

            await context.Service.ReconcileAsync();

            Assert.Equal(Qr, document.QrCode);
            Assert.Equal(UrlChave, document.UrlChave);
        }

        [Fact]
        public async Task AnOutcomeWithNoQr_DoesNotEraseTheOneWeHave()
        {
            var context = new NFeReconciliationServiceTests.ReconciliationContext();
            var document = context.Stale(NFeReconciliationServiceTests.TransmittedDocument());
            document.QrCode = Qr;
            document.UrlChave = UrlChave;

            context.Provider
                .Setup(x => x.ConsultAsync(document.AccessKey, It.IsAny<EnterpriseFiscalSettings>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NFeProviderResult { Status = NFeStatus.Authorized, Cstat = "100" });

            await context.Service.ReconcileAsync();

            Assert.Equal(Qr, document.QrCode);
            Assert.Equal(UrlChave, document.UrlChave);
        }

        /// <summary>
        /// The QR has to survive the path that issues the note, not only the one that reconciles
        /// it. An authorised document is terminal, so the reconciler never looks at it again: a QR
        /// dropped here is a QR the customer never gets.
        /// </summary>
        [Fact]
        public async Task AnNFCeAuthorisedAtTheCounter_KeepsItsQr()
        {
            var context = new NFeIssuanceWriteAheadTests.IssuanceContext();
            context.Provider
                .Setup(x => x.IssueAsync(It.IsAny<NFeIssueContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NFeProviderResult
                {
                    Status = NFeStatus.Authorized,
                    Cstat = "100",
                    Protocol = "PROT-1",
                    XmlContent = "<xml />",
                    IssuedAt = DateTime.UtcNow,
                    QrCode = Qr,
                    UrlChave = UrlChave
                });

            var result = await context.Service.IssueAsync(
                context.OrderId, context.EnterpriseId, NFeDocumentType.NFCe);

            Assert.Equal(Qr, result!.QrCode);
            Assert.Equal(UrlChave, result.UrlChave);
        }

        /// <summary>
        /// A failure that provably never reached the wire must retract the write-ahead marker, or
        /// the next attempt burns the allocated number and the till cannot tell that falling back
        /// to contingency is safe.
        /// </summary>
        [Fact]
        public async Task AnIssueThatNeverLeftTheProcess_RetractsTheWriteAheadMarker()
        {
            var context = new NFeIssuanceWriteAheadTests.IssuanceContext();
            context.Provider
                .Setup(x => x.IssueAsync(It.IsAny<NFeIssueContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NFeProviderResult
                {
                    Status = NFeStatus.Error,
                    XMotivo = "No CSC is configured for this enterprise.",
                    NeverTransmitted = true
                });

            var result = await context.Service.IssueAsync(
                context.OrderId, context.EnterpriseId, NFeDocumentType.NFCe);

            Assert.Equal(NFeStatus.Error, result!.Status);
            Assert.Null(result.TransmittedAtUtc);
            Assert.False(result.IsOutcomeUnknown);
        }

        [Fact]
        public async Task AModelo55Outcome_CarriesNoQrAtAll()
        {
            var context = new NFeReconciliationServiceTests.ReconciliationContext();
            var document = context.Stale(NFeReconciliationServiceTests.TransmittedDocument());

            context.Provider
                .Setup(x => x.ConsultAsync(document.AccessKey, It.IsAny<EnterpriseFiscalSettings>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NFeProviderResult { Status = NFeStatus.Authorized, Cstat = "100" });

            await context.Service.ReconcileAsync();

            Assert.Equal(string.Empty, document.QrCode);
            Assert.Equal(string.Empty, document.UrlChave);
        }
    }
}
