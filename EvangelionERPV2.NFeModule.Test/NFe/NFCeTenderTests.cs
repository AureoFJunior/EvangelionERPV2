using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using Moq;
using Xunit;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    /// <summary>
    /// The counter's tenders reaching the note. `NFeXmlBuilderTests` covers what the XML then says;
    /// this covers whether the service bothers to look them up at all.
    /// </summary>
    /// <remarks>
    /// It did not, until this landed: `PdvSalePayment` has said since M29a that it is "the source
    /// NFC-e's pag group is built from", and every note the till issued declared tPag 99 "Outros" —
    /// on a consumer document, where pag is what the customer and the fisco both read.
    /// </remarks>
    public class NFCeTenderTests
    {
        [Fact]
        public async Task AnNFCeForATillSale_CarriesTheTendersThatPaidIt()
        {
            var context = new NFeIssuanceWriteAheadTests.IssuanceContext();
            context.SetupAuthorizingProvider();
            context.SeedTillSale(
                new PdvSalePayment { Method = (int)EnumPaymentMethod.Cash, Amount = 40m, ChangeGiven = 10m },
                new PdvSalePayment { Method = (int)EnumPaymentMethod.Pix, Amount = 60m });

            NFeIssueContext? seen = null;
            context.Provider
                .Setup(x => x.IssueAsync(It.IsAny<NFeIssueContext>(), It.IsAny<CancellationToken>()))
                .Callback<NFeIssueContext, CancellationToken>((issued, _) => seen = issued)
                .ReturnsAsync(new NFeProviderResult { Status = NFeStatus.Authorized, Cstat = "100" });

            await context.Service.IssueAsync(context.OrderId, context.EnterpriseId, NFeDocumentType.NFCe);

            Assert.NotNull(seen!.Payments);
            Assert.Equal(2, seen.Payments!.Count);
        }

        [Fact]
        public async Task AModelo55_NeverAsksHowTheTillWasPaid()
        {
            // A back-office note is issued against an order, not a sale. Reading a till's tenders
            // for it would be two queries for a document that must still declare "Outros".
            var context = new NFeIssuanceWriteAheadTests.IssuanceContext();
            context.SetupAuthorizingProvider();
            context.SeedTillSale(
                new PdvSalePayment { Method = (int)EnumPaymentMethod.Cash, Amount = 100m });

            NFeIssueContext? seen = null;
            context.Provider
                .Setup(x => x.IssueAsync(It.IsAny<NFeIssueContext>(), It.IsAny<CancellationToken>()))
                .Callback<NFeIssueContext, CancellationToken>((issued, _) => seen = issued)
                .ReturnsAsync(new NFeProviderResult { Status = NFeStatus.Authorized, Cstat = "100" });

            await context.Service.IssueAsync(context.OrderId, context.EnterpriseId);

            Assert.Null(seen!.Payments);
            context.PdvSaleRepository.Verify(
                x => x.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PdvSale, bool>>>()),
                Times.Never);
        }

        [Fact]
        public async Task AnNFCeForAnOrderNoTillEverRang_FallsBackToOutros()
        {
            // The back office can issue an NFC-e for an ordinary order. There are no tenders to
            // read, and the builder's "Outros" branch is the honest answer.
            var context = new NFeIssuanceWriteAheadTests.IssuanceContext();
            context.SetupAuthorizingProvider();

            NFeIssueContext? seen = null;
            context.Provider
                .Setup(x => x.IssueAsync(It.IsAny<NFeIssueContext>(), It.IsAny<CancellationToken>()))
                .Callback<NFeIssueContext, CancellationToken>((issued, _) => seen = issued)
                .ReturnsAsync(new NFeProviderResult { Status = NFeStatus.Authorized, Cstat = "100" });

            await context.Service.IssueAsync(context.OrderId, context.EnterpriseId, NFeDocumentType.NFCe);

            Assert.Null(seen!.Payments);
        }
    }
}
