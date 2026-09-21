using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Application.Providers;
using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.NFeModule.Application.Services;
using EvangelionERPV2.NFeModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EvangelionERPV2.Test.Security
{
    public class NFeServiceTenantScopeTests
    {
        [Fact]
        public async Task GetByOrderIdAsync_WhenEnterpriseIdIsEmpty_ReturnsNullWithoutLookup()
        {
            var (service, nfeRepositoryCustom, orderRepository, _, _, _) = CreateService();
            var orderId = Guid.NewGuid();

            var result = await service.GetByOrderIdAsync(orderId, Guid.Empty);

            Assert.Null(result);
            orderRepository.Verify(x => x.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
            nfeRepositoryCustom.Verify(x => x.GetByOrderIdAsync(It.IsAny<Guid>(), It.IsAny<NFeDocumentType?>()), Times.Never);
        }

        [Fact]
        public async Task IssueAsync_WhenEnterpriseIdIsEmpty_ThrowsNotFoundWithoutLookup()
        {
            var (service, nfeRepositoryCustom, orderRepository, customerRepository, enterpriseRepository, provider) = CreateService();
            var orderId = Guid.NewGuid();

            await Assert.ThrowsAsync<NotFoundDatabaseException>(() => service.IssueAsync(orderId, Guid.Empty));

            orderRepository.Verify(x => x.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
            customerRepository.Verify(x => x.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
            enterpriseRepository.Verify(x => x.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
            nfeRepositoryCustom.Verify(x => x.GetByOrderIdAsync(It.IsAny<Guid>(), It.IsAny<NFeDocumentType?>()), Times.Never);
            provider.Verify(x => x.IssueAsync(It.IsAny<NFeIssueContext>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ConsultAsync_ReadsByAccessKeyTenantScopedInSql(bool documentExists)
        {
            var (service, nfeRepositoryCustom, orderRepository, _, _, _) = CreateService();
            var enterpriseId = Guid.NewGuid();
            const string accessKey = "35260712345678000195550010000000011323801728";

            nfeRepositoryCustom
                .Setup(x => x.GetByAccessKeyAsync(accessKey, enterpriseId))
                .ReturnsAsync(documentExists
                    ? new NFeDocument { Id = Guid.NewGuid(), EnterpriseId = enterpriseId }
                    : null);

            var result = await service.ConsultAsync(accessKey, enterpriseId);

            Assert.Equal(documentExists, result != null);

            nfeRepositoryCustom.Verify(x => x.GetByAccessKeyAsync(accessKey, enterpriseId), Times.Once);

            nfeRepositoryCustom.Verify(
                x => x.GetByAccessKeyAsync(It.IsAny<string>()),
                documentExists ? Times.Never() : Times.Once());
        }

        [Fact]
        public async Task ConsultAsync_LegacyRowBelongingToAnotherTenant_ReturnsNull()
        {
            var (service, nfeRepositoryCustom, orderRepository, _, _, _) = CreateService();
            var enterpriseId = Guid.NewGuid();
            var otherEnterpriseId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            const string accessKey = "35260712345678000195550010000000011323801728";

            nfeRepositoryCustom
                .Setup(x => x.GetByAccessKeyAsync(accessKey, enterpriseId))
                .ReturnsAsync((NFeDocument?)null);

            nfeRepositoryCustom
                .Setup(x => x.GetByAccessKeyAsync(accessKey))
                .ReturnsAsync(new NFeDocument
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = Guid.Empty,
                    OrderId = orderId,
                });

            orderRepository
                .Setup(x => x.GetByIdAsync(orderId))
                .ReturnsAsync(new Order { Id = orderId, EnterpriseId = otherEnterpriseId });

            Assert.Null(await service.ConsultAsync(accessKey, enterpriseId));
        }

        [Fact]
        public async Task ConsultAsync_WhenEnterpriseIdIsEmpty_ReturnsNullWithoutAnyLookup()
        {
            var (service, nfeRepositoryCustom, orderRepository, _, _, _) = CreateService();

            Assert.Null(await service.ConsultAsync("35260712345678000195550010000000011323801728", Guid.Empty));

            nfeRepositoryCustom.Verify(x => x.GetByAccessKeyAsync(It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
            nfeRepositoryCustom.Verify(x => x.GetByAccessKeyAsync(It.IsAny<string>()), Times.Never);
            orderRepository.Verify(x => x.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task CancelAsync_DocumentOfAnotherTenant_ReturnsNullAndDoesNotWrite()
        {
            var (service, nfeRepositoryCustom, _, _, _, _) = CreateService();
            var enterpriseId = Guid.NewGuid();
            const string accessKey = "35260712345678000195550010000000011323801728";

            nfeRepositoryCustom
                .Setup(x => x.GetByAccessKeyAsync(accessKey, enterpriseId))
                .ReturnsAsync((NFeDocument?)null);
            nfeRepositoryCustom
                .Setup(x => x.GetByAccessKeyAsync(accessKey))
                .ReturnsAsync(new NFeDocument
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = Guid.NewGuid(),
                    Status = NFeStatus.Authorized,
                });

            Assert.Null(await service.CancelAsync(accessKey, enterpriseId, "cancelling someone else's note"));
        }

        private static (NFeService service,
            Mock<INFeRepository<NFeDocument>> nfeRepositoryCustom,
            Mock<IRepository<Order>> orderRepository,
            Mock<IRepository<Customer>> customerRepository,
            Mock<IRepository<Enterprise>> enterpriseRepository,
            Mock<INFeProvider> provider) CreateService()
        {
            var nfeRepository = new Mock<IRepository<NFeDocument>>();
            var nfeRepositoryCustom = new Mock<INFeRepository<NFeDocument>>();
            var orderRepository = new Mock<IRepository<Order>>();
            var customerRepository = new Mock<IRepository<Customer>>();
            var enterpriseRepository = new Mock<IRepository<Enterprise>>();
            var provider = new Mock<INFeProvider>();

            var fiscalSettingsService = new Mock<INFeFiscalSettingsService>();

            var service = new NFeService(
                nfeRepository.Object,
                nfeRepositoryCustom.Object,
                orderRepository.Object,
                customerRepository.Object,
                enterpriseRepository.Object,
                provider.Object,
                fiscalSettingsService.Object,
                Options.Create(new NFeSettings { Enabled = true }),
                new Mock<IRepository<PdvSale>>().Object,
                new Mock<IRepository<PdvSalePayment>>().Object);

            return (service, nfeRepositoryCustom, orderRepository, customerRepository, enterpriseRepository, provider);
        }
    }
}
