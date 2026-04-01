using EvangelionERPV2.BillsModule.Application.Configs;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.BillsModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using System.Threading;

namespace EvangelionERPV2.Test.Resilience
{
    public class BillsServiceIdempotencyTests
    {
        [Fact]
        public async Task GenerateAsync_DuplicateCommit_ReturnsExistingBill()
        {
            var orderId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var existingBill = new Bill
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                DocumentNumber = "existing"
            };

            var billRepositoryMock = new Mock<IRepository<Bill>>();
            var billRepositoryCustomMock = new Mock<IBillsRepository<Bill>>();
            var orderRepositoryMock = new Mock<IRepository<Order>>();
            var customerRepositoryMock = new Mock<IRepository<Customer>>();

            orderRepositoryMock
                .Setup(x => x.GetByIdAsync(orderId))
                .ReturnsAsync(CreateOrder(orderId, customerId));

            customerRepositoryMock
                .Setup(x => x.GetByIdAsync(customerId))
                .ReturnsAsync(new Customer
                {
                    Id = customerId,
                    Name = "Customer",
                    Document = "44331610128"
                });

            billRepositoryCustomMock
                .SetupSequence(x => x.GetByOrderIdAsync(orderId))
                .ReturnsAsync((Bill?)null)
                .ReturnsAsync(existingBill);

            billRepositoryMock
                .Setup(x => x.CreateAsync(It.IsAny<Bill>()))
                .ReturnsAsync((Bill bill) => bill);

            billRepositoryMock
                .Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateException("duplicate key", new Exception("unique index")));

            var settings = CreateValidSettings();

            var service = new BillsService(
                billRepositoryMock.Object,
                billRepositoryCustomMock.Object,
                orderRepositoryMock.Object,
                customerRepositoryMock.Object,
                Options.Create(settings));

            var result = await service.GenerateAsync(orderId);

            Assert.NotNull(result);
            Assert.Equal(existingBill.Id, result!.Id);
            Assert.Equal(existingBill.OrderId, result.OrderId);
        }

        private static BillsSettings CreateValidSettings()
        {
            return new BillsSettings
            {
                Enabled = true,
                BankCode = 33,
                BeneficiaryDocument = "86875666000109",
                BeneficiaryName = "Beneficiario Teste",
                BeneficiaryAddress = "Rua Beneficiario",
                BeneficiaryAddressNumber = "123",
                BeneficiaryNeighborhood = "Centro",
                BeneficiaryCity = "Cidade",
                BeneficiaryState = "SP",
                BeneficiaryZipCode = "12345678",
                BeneficiaryNotes = "Observacoes",
                BeneficiaryCode = "1234567",
                BeneficiaryCodeDigit = "",
                TransmissionCode = "123400001234567",
                Agency = "1234",
                AgencyDigit = "5",
                Account = "12345678",
                AccountDigit = "9",
                Wallet = "101",
                WalletVariation = "",
                WalletType = 1,
                RegistrationType = 1,
                PrintType = 2,
                DocumentType = 1,
                NossoNumeroLength = 8,
                Instructions = "Instrucoes",
                DefaultPayerDocument = "44331610128",
                DefaultPayerName = "Pagador Teste",
                DefaultPayerAddress = "Rua Pagador"
            };
        }

        private static Order CreateOrder(Guid orderId, Guid customerId)
        {
            var order = new Order(
                DateTime.UtcNow.AddDays(5),
                DateTime.UtcNow.AddDays(5),
                100,
                Guid.NewGuid(),
                customerId,
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        IsActive = true,
                        Quantity = 1,
                        Value = 100,
                        ProductId = Guid.NewGuid()
                    }
                },
                Guid.NewGuid());

            order.Id = orderId;
            order.CreatedAt = DateTime.UtcNow;

            return order;
        }
    }
}
