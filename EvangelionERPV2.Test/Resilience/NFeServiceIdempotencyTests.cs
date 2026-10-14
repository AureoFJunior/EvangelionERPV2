using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Models;
using EvangelionERPV2.NFeModule.Application.Providers;
using EvangelionERPV2.NFeModule.Application.Services;
using EvangelionERPV2.NFeModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using System.Threading;

namespace EvangelionERPV2.Test.Resilience
{
    public class NFeServiceIdempotencyTests
    {
        [Fact]
        public async Task IssueAsync_DuplicateCommit_ReturnsExistingDocument()
        {
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var existingDocument = new NFeDocument
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Type = NFeDocumentType.NFe,
                Status = NFeStatus.Authorized,
                AccessKey = "existing-key"
            };

            var nfeRepositoryMock = new Mock<IRepository<NFeDocument>>();
            var nfeRepositoryCustomMock = new Mock<INFeRepository<NFeDocument>>();
            var orderRepositoryMock = new Mock<IRepository<Order>>();
            var customerRepositoryMock = new Mock<IRepository<Customer>>();
            var enterpriseRepositoryMock = new Mock<IRepository<Enterprise>>();
            var providerMock = new Mock<INFeProvider>();

            orderRepositoryMock
                .Setup(x => x.GetByIdAsync(orderId))
                .ReturnsAsync(new Order
                {
                    Id = orderId,
                    EnterpriseId = enterpriseId,
                    CustomerId = customerId,
                    TotalValue = 100
                });

            customerRepositoryMock
                .Setup(x => x.GetByIdAsync(customerId))
                .ReturnsAsync(new Customer { Id = customerId, Name = "Customer" });

            enterpriseRepositoryMock
                .Setup(x => x.GetByIdAsync(enterpriseId))
                .ReturnsAsync(new Enterprise { Id = enterpriseId, Name = "Enterprise" });

            nfeRepositoryCustomMock
                .SetupSequence(x => x.GetByOrderIdAsync(orderId, NFeDocumentType.NFe))
                .ReturnsAsync((NFeDocument?)null)
                .ReturnsAsync(existingDocument);

            nfeRepositoryMock
                .Setup(x => x.CreateAsync(It.IsAny<NFeDocument>()))
                .ReturnsAsync((NFeDocument document) => document);

            nfeRepositoryMock
                .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateException("duplicate key", new Exception("unique index")));

            providerMock
                .Setup(x => x.IssueAsync(
                    It.IsAny<Order>(),
                    It.IsAny<Enterprise>(),
                    It.IsAny<Customer>(),
                    NFeDocumentType.NFe,
                    It.IsAny<NFeSettings>()))
                .ReturnsAsync(new NFeProviderResult
                {
                    AccessKey = "new-key",
                    Protocol = "PROT-1",
                    XmlContent = "<xml></xml>",
                    Number = "1",
                    Series = "1",
                    Environment = "Homologation",
                    IssuedAt = DateTime.UtcNow,
                    TotalValue = 100,
                    Status = NFeStatus.Authorized
                });

            var service = new NFeService(
                nfeRepositoryMock.Object,
                nfeRepositoryCustomMock.Object,
                orderRepositoryMock.Object,
                customerRepositoryMock.Object,
                enterpriseRepositoryMock.Object,
                providerMock.Object,
                Options.Create(new NFeSettings { Enabled = true }));

            var result = await service.IssueAsync(orderId, enterpriseId, NFeDocumentType.NFe);

            Assert.NotNull(result);
            Assert.Equal(existingDocument.Id, result!.Id);
            Assert.Equal(existingDocument.AccessKey, result.AccessKey);
            providerMock.Verify(
                x => x.IssueAsync(
                    It.IsAny<Order>(),
                    It.IsAny<Enterprise>(),
                    It.IsAny<Customer>(),
                    NFeDocumentType.NFe,
                    It.IsAny<NFeSettings>()),
                Times.Never);
        }

        [Fact]
        public async Task IssueAsync_ConcurrentSameOrder_IssuesOnlyOnce()
        {
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var customerId = Guid.NewGuid();

            var order = new Order
            {
                Id = orderId,
                EnterpriseId = enterpriseId,
                CustomerId = customerId,
                TotalValue = 100
            };
            var enterprise = new Enterprise { Id = enterpriseId, Name = "Enterprise" };
            var customer = new Customer { Id = customerId, Name = "Customer" };

            var nfeRepositoryMock = new Mock<IRepository<NFeDocument>>();
            var nfeRepositoryCustomMock = new Mock<INFeRepository<NFeDocument>>();
            var orderRepositoryMock = new Mock<IRepository<Order>>();
            var customerRepositoryMock = new Mock<IRepository<Customer>>();
            var enterpriseRepositoryMock = new Mock<IRepository<Enterprise>>();
            var providerMock = new Mock<INFeProvider>();

            var persistedDocument = (NFeDocument?)null;
            var persistedGate = new object();
            var providerCalls = 0;
            var commitCalls = 0;

            orderRepositoryMock
                .Setup(x => x.GetByIdAsync(orderId))
                .ReturnsAsync(order);

            customerRepositoryMock
                .Setup(x => x.GetByIdAsync(customerId))
                .ReturnsAsync(customer);

            enterpriseRepositoryMock
                .Setup(x => x.GetByIdAsync(enterpriseId))
                .ReturnsAsync(enterprise);

            nfeRepositoryCustomMock
                .Setup(x => x.GetByOrderIdAsync(orderId, NFeDocumentType.NFe))
                .ReturnsAsync(() =>
                {
                    lock (persistedGate)
                    {
                        return persistedDocument;
                    }
                });

            nfeRepositoryMock
                .Setup(x => x.CreateAsync(It.IsAny<NFeDocument>()))
                .Callback<NFeDocument>(document =>
                {
                    lock (persistedGate)
                    {
                        persistedDocument = document;
                    }
                })
                .ReturnsAsync((NFeDocument document) => document);

            nfeRepositoryMock
                .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Increment(ref commitCalls))
                .Returns(Task.CompletedTask);

            providerMock
                .Setup(x => x.IssueAsync(
                    It.IsAny<Order>(),
                    It.IsAny<Enterprise>(),
                    It.IsAny<Customer>(),
                    NFeDocumentType.NFe,
                    It.IsAny<NFeSettings>()))
                .Callback(() => Interlocked.Increment(ref providerCalls))
                .Returns(async () =>
                {
                    await Task.Delay(50);
                    return new NFeProviderResult
                    {
                        AccessKey = "12345678901234567890123456789012345678901234",
                        Protocol = "PROT-1",
                        XmlContent = "<xml></xml>",
                        Number = "1",
                        Series = "1",
                        Environment = "Homologation",
                        IssuedAt = DateTime.UtcNow,
                        TotalValue = 100,
                        Status = NFeStatus.Authorized
                    };
                });

            var service = new NFeService(
                nfeRepositoryMock.Object,
                nfeRepositoryCustomMock.Object,
                orderRepositoryMock.Object,
                customerRepositoryMock.Object,
                enterpriseRepositoryMock.Object,
                providerMock.Object,
                Options.Create(new NFeSettings { Enabled = true }));

            var issuanceTask1 = service.IssueAsync(orderId, enterpriseId, NFeDocumentType.NFe);
            var issuanceTask2 = service.IssueAsync(orderId, enterpriseId, NFeDocumentType.NFe);
            await Task.WhenAll(issuanceTask1, issuanceTask2);

            var issuedDocument1 = await issuanceTask1;
            var issuedDocument2 = await issuanceTask2;

            Assert.NotNull(issuedDocument1);
            Assert.NotNull(issuedDocument2);
            Assert.Equal(issuedDocument1!.Id, issuedDocument2!.Id);
            Assert.Equal(1, providerCalls);
            Assert.Equal(2, commitCalls);
        }

        [Fact]
        public async Task IssueAsync_ExistingErroredDocument_RetriesIssuance()
        {
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var erroredDocument = new NFeDocument
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Type = NFeDocumentType.NFe,
                Status = NFeStatus.Error,
                AccessKey = "failed-key",
                XmlContent = "<failed />",
                IsActive = true
            };

            var nfeRepositoryMock = new Mock<IRepository<NFeDocument>>();
            var nfeRepositoryCustomMock = new Mock<INFeRepository<NFeDocument>>();
            var orderRepositoryMock = new Mock<IRepository<Order>>();
            var customerRepositoryMock = new Mock<IRepository<Customer>>();
            var enterpriseRepositoryMock = new Mock<IRepository<Enterprise>>();
            var providerMock = new Mock<INFeProvider>();

            orderRepositoryMock
                .Setup(x => x.GetByIdAsync(orderId))
                .ReturnsAsync(new Order
                {
                    Id = orderId,
                    EnterpriseId = enterpriseId,
                    CustomerId = customerId,
                    TotalValue = 100
                });

            customerRepositoryMock
                .Setup(x => x.GetByIdAsync(customerId))
                .ReturnsAsync(new Customer { Id = customerId, Name = "Customer" });

            enterpriseRepositoryMock
                .Setup(x => x.GetByIdAsync(enterpriseId))
                .ReturnsAsync(new Enterprise { Id = enterpriseId, Name = "Enterprise" });

            nfeRepositoryCustomMock
                .Setup(x => x.GetByOrderIdAsync(orderId, NFeDocumentType.NFe))
                .ReturnsAsync(erroredDocument);

            nfeRepositoryMock
                .Setup(x => x.Update(It.IsAny<NFeDocument>()))
                .Returns((NFeDocument document) => document);

            nfeRepositoryMock
                .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            providerMock
                .Setup(x => x.IssueAsync(
                    It.IsAny<Order>(),
                    It.IsAny<Enterprise>(),
                    It.IsAny<Customer>(),
                    NFeDocumentType.NFe,
                    It.IsAny<NFeSettings>()))
                .ReturnsAsync(new NFeProviderResult
                {
                    AccessKey = "authorized-key",
                    Protocol = "PROT-RETRY",
                    XmlContent = "<authorized />",
                    Number = "2",
                    Series = "1",
                    Environment = "Homologation",
                    IssuedAt = DateTime.UtcNow,
                    TotalValue = 100,
                    Status = NFeStatus.Authorized
                });

            var service = new NFeService(
                nfeRepositoryMock.Object,
                nfeRepositoryCustomMock.Object,
                orderRepositoryMock.Object,
                customerRepositoryMock.Object,
                enterpriseRepositoryMock.Object,
                providerMock.Object,
                Options.Create(new NFeSettings { Enabled = true }));

            var result = await service.IssueAsync(orderId, enterpriseId, NFeDocumentType.NFe);

            Assert.NotNull(result);
            Assert.Equal(erroredDocument.Id, result!.Id);
            Assert.Equal(NFeStatus.Authorized, result.Status);
            Assert.Equal("authorized-key", result.AccessKey);
            providerMock.Verify(
                x => x.IssueAsync(
                    It.IsAny<Order>(),
                    It.IsAny<Enterprise>(),
                    It.IsAny<Customer>(),
                    NFeDocumentType.NFe,
                    It.IsAny<NFeSettings>()),
                Times.Once);
            nfeRepositoryMock.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }
}
