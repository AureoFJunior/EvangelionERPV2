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
