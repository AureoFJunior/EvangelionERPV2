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
            // A consult answers about status, not about supplementary information. The customer may
            // still be holding the receipt years later, so what authorised once must survive.
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

        [Fact]
        public async Task AModelo55Outcome_CarriesNoQrAtAll()
        {
            // Modelo 55 has no infNFeSupl. Nothing may invent one.
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
