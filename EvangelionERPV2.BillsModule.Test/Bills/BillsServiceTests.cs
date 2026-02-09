using EvangelionERPV2.BillsModule.Application.Configs;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.BillsModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using BillEntity = EvangelionERPV2.Shared.Entities.Bill;

namespace EvangelionERPV2.BillsModule.Test
{
    public class BillsServiceTests
    {
        [Fact]
        public async Task GetByOrderIdAsync_WhenIdIsEmpty_ReturnsNull()
        {
            var settings = CreateValidSettings();
            var (service, _, _, _, _) = CreateServiceWithMocks(settings);

            var result = await service.GetByOrderIdAsync(Guid.Empty);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetByOrderIdAsync_WhenBillExists_ReturnsBill()
        {
            var settings = CreateValidSettings();
            var (service, _, billRepoCustom, _, _) = CreateServiceWithMocks(settings);
            var orderId = Guid.NewGuid();
            var expected = new BillEntity { Id = Guid.NewGuid(), OrderId = orderId, BankCode = 33 };

            billRepoCustom.Setup(r => r.GetByOrderIdAsync(orderId)).ReturnsAsync(expected);

            var result = await service.GetByOrderIdAsync(orderId);

            Assert.Same(expected, result);
        }

        [Fact]
        public async Task GenerateAsync_WhenDisabled_ReturnsNull()
        {
            var settings = CreateValidSettings();
            settings.Enabled = false;
            var (service, billRepo, _, orderRepo, _) = CreateServiceWithMocks(settings);

            var result = await service.GenerateAsync(Guid.NewGuid());

            Assert.Null(result);
            billRepo.Verify(r => r.CreateAsync(It.IsAny<BillEntity>()), Times.Never);
            orderRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task GenerateAsync_WhenOrderIdIsEmpty_Throws()
        {
            var settings = CreateValidSettings();
            var (service, _, _, _, _) = CreateServiceWithMocks(settings);

            await Assert.ThrowsAsync<InsertDatabaseException>(() => service.GenerateAsync(Guid.Empty));
        }

        [Fact]
        public async Task GenerateAsync_WhenExistingBill_ReturnsExisting()
        {
            var settings = CreateValidSettings();
            var (service, billRepo, billRepoCustom, orderRepo, _) = CreateServiceWithMocks(settings);
            var orderId = Guid.NewGuid();
            var existing = new BillEntity { Id = Guid.NewGuid(), OrderId = orderId, BankCode = 33 };

            billRepoCustom.Setup(r => r.GetByOrderIdAsync(orderId)).ReturnsAsync(existing);

            var result = await service.GenerateAsync(orderId);

            Assert.Same(existing, result);
            billRepo.Verify(r => r.CreateAsync(It.IsAny<BillEntity>()), Times.Never);
            orderRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task GenerateAsync_WhenOrderMissing_Throws()
        {
            var settings = CreateValidSettings();
            var (service, _, billRepoCustom, orderRepo, _) = CreateServiceWithMocks(settings);
            var orderId = Guid.NewGuid();

            billRepoCustom.Setup(r => r.GetByOrderIdAsync(orderId)).ReturnsAsync((BillEntity?)null);
            orderRepo.Setup(r => r.GetByIdAsync(orderId)).ReturnsAsync((Order?)null);

            await Assert.ThrowsAsync<NotFoundDatabaseException>(() => service.GenerateAsync(orderId));
        }

        [Fact]
        public async Task GenerateAsync_WhenPayerDocumentInvalid_Throws()
        {
            var settings = CreateValidSettings();
            settings.DefaultPayerDocument = "123";
            var (service, _, billRepoCustom, orderRepo, customerRepo) = CreateServiceWithMocks(settings);
            var customerId = Guid.NewGuid();
            var order = CreateOrder(customerId);
            var customer = new Customer("Customer", "11999999999", "customer@test.com", "Rua Teste", "")
            {
                Id = customerId
            };

            billRepoCustom.Setup(r => r.GetByOrderIdAsync(order.Id)).ReturnsAsync((BillEntity?)null);
            orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);
            customerRepo.Setup(r => r.GetByIdAsync(customerId)).ReturnsAsync(customer);

            await Assert.ThrowsAsync<InsertDatabaseException>(() => service.GenerateAsync(order.Id));
        }

        [Fact]
        public async Task GenerateAsync_WithValidData_CreatesBill()
        {
            var settings = CreateValidSettings();
            var (service, billRepo, billRepoCustom, orderRepo, customerRepo) = CreateServiceWithMocks(settings);
            var customerId = Guid.NewGuid();
            var order = CreateOrder(customerId);
            var customer = new Customer("Customer", "11999999999", "customer@test.com", "Rua Teste", "44331610128")
            {
                Id = customerId
            };
            BillEntity? created = null;

            billRepoCustom.Setup(r => r.GetByOrderIdAsync(order.Id)).ReturnsAsync((BillEntity?)null);
            orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);
            customerRepo.Setup(r => r.GetByIdAsync(customerId)).ReturnsAsync(customer);
            billRepo.Setup(r => r.CreateAsync(It.IsAny<BillEntity>()))
                .Callback<BillEntity>(entity => created = entity)
                .ReturnsAsync((BillEntity entity) => entity);
            billRepo.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var result = await service.GenerateAsync(order.Id);

            Assert.NotNull(result);
            Assert.NotNull(created);
            Assert.Equal(order.Id, created?.OrderId);
            Assert.Equal(settings.BankCode, created?.BankCode);
            Assert.Equal(order.TotalValue, created?.Amount ?? 0, 2);
            Assert.False(string.IsNullOrWhiteSpace(created?.DigitableLine));
            Assert.False(string.IsNullOrWhiteSpace(created?.BarCode));
            Assert.False(string.IsNullOrWhiteSpace(created?.HtmlContent));
        }

        private static (BillsService service,
            Mock<IRepository<BillEntity>> billRepo,
            Mock<IBillsRepository<BillEntity>> billRepoCustom,
            Mock<IRepository<Order>> orderRepo,
            Mock<IRepository<Customer>> customerRepo) CreateServiceWithMocks(BillsSettings settings)
        {
            var billRepo = new Mock<IRepository<BillEntity>>();
            var billRepoCustom = new Mock<IBillsRepository<BillEntity>>();
            var orderRepo = new Mock<IRepository<Order>>();
            var customerRepo = new Mock<IRepository<Customer>>();
            var service = new BillsService(
                billRepo.Object,
                billRepoCustom.Object,
                orderRepo.Object,
                customerRepo.Object,
                Options.Create(settings));

            return (service, billRepo, billRepoCustom, orderRepo, customerRepo);
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

        private static Order CreateOrder(Guid? customerId)
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

            order.Id = Guid.NewGuid();
            order.CreatedAt = DateTime.UtcNow;

            return order;
        }
    }
}



