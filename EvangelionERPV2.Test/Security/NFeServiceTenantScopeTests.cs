using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Providers;
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
            provider.Verify(x => x.IssueAsync(It.IsAny<Order>(), It.IsAny<Enterprise>(), It.IsAny<Customer>(), It.IsAny<NFeDocumentType>(), It.IsAny<NFeSettings>()), Times.Never);
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

            var service = new NFeService(
                nfeRepository.Object,
                nfeRepositoryCustom.Object,
                orderRepository.Object,
                customerRepository.Object,
                enterpriseRepository.Object,
                provider.Object,
                Options.Create(new NFeSettings { Enabled = true }));

            return (service, nfeRepositoryCustom, orderRepository, customerRepository, enterpriseRepository, provider);
        }
    }
}
