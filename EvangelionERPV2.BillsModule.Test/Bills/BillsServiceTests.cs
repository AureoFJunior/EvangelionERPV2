using EvangelionERPV2.BillsModule.Application.Configs;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.BillsModule.Domain.Interface;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using BoletoNetCore;
using System.Reflection;
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
            var enterpriseId = Guid.NewGuid();

            var result = await service.GetByOrderIdAsync(Guid.Empty, enterpriseId);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetByOrderIdAsync_WhenBillExists_ReturnsBill()
        {
            var settings = CreateValidSettings();
            var (service, _, billRepoCustom, orderRepo, _) = CreateServiceWithMocks(settings);
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var expected = new BillEntity { Id = Guid.NewGuid(), OrderId = orderId, BankCode = 33 };

            orderRepo
                .Setup(r => r.GetByIdAsync(orderId))
                .ReturnsAsync(new Order { Id = orderId, EnterpriseId = enterpriseId });
            billRepoCustom.Setup(r => r.GetByOrderIdAsync(orderId)).ReturnsAsync(expected);

            var result = await service.GetByOrderIdAsync(orderId, enterpriseId);

            Assert.Same(expected, result);
        }

        [Fact]
        public async Task GetByOrderIdAsync_WhenEnterpriseDoesNotMatch_ReturnsNull()
        {
            var settings = CreateValidSettings();
            var (service, _, billRepoCustom, orderRepo, _) = CreateServiceWithMocks(settings);
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();

            orderRepo
                .Setup(r => r.GetByIdAsync(orderId))
                .ReturnsAsync(new Order { Id = orderId, EnterpriseId = Guid.NewGuid() });

            var result = await service.GetByOrderIdAsync(orderId, enterpriseId);

            Assert.Null(result);
            billRepoCustom.Verify(r => r.GetByOrderIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task GetByOrderIdAsync_WhenEnterpriseIdIsEmpty_ReturnsNullWithoutLookup()
        {
            var settings = CreateValidSettings();
            var (service, _, billRepoCustom, orderRepo, _) = CreateServiceWithMocks(settings);
            var orderId = Guid.NewGuid();

            var result = await service.GetByOrderIdAsync(orderId, Guid.Empty);

            Assert.Null(result);
            orderRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
            billRepoCustom.Verify(r => r.GetByOrderIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task GenerateAsync_WhenDisabled_ReturnsNull()
        {
            var settings = CreateValidSettings();
            settings.Enabled = false;
            var (service, billRepo, _, orderRepo, _) = CreateServiceWithMocks(settings);
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            orderRepo
                .Setup(r => r.GetByIdAsync(orderId))
                .ReturnsAsync(new Order { Id = orderId, EnterpriseId = enterpriseId });

            var result = await service.GenerateAsync(orderId, enterpriseId);

            Assert.Null(result);
            billRepo.Verify(r => r.CreateAsync(It.IsAny<BillEntity>()), Times.Never);
            orderRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task GenerateAsync_WhenOrderIdIsEmpty_Throws()
        {
            var settings = CreateValidSettings();
            var (service, _, _, _, _) = CreateServiceWithMocks(settings);

            await Assert.ThrowsAsync<InsertDatabaseException>(() => service.GenerateAsync(Guid.Empty, Guid.NewGuid()));
        }

        [Fact]
        public async Task GenerateAsync_WhenEnterpriseIdIsEmpty_ThrowsNotFoundWithoutLookup()
        {
            var settings = CreateValidSettings();
            var (service, _, billRepoCustom, orderRepo, customerRepo) = CreateServiceWithMocks(settings);
            var orderId = Guid.NewGuid();

            await Assert.ThrowsAsync<NotFoundDatabaseException>(() => service.GenerateAsync(orderId, Guid.Empty));

            orderRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
            customerRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
            billRepoCustom.Verify(r => r.GetByOrderIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task GenerateAsync_WhenExistingBill_ReturnsExisting()
        {
            var settings = CreateValidSettings();
            var (service, billRepo, billRepoCustom, orderRepo, _) = CreateServiceWithMocks(settings);
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var existing = new BillEntity { Id = Guid.NewGuid(), OrderId = orderId, BankCode = 33 };

            orderRepo
                .Setup(r => r.GetByIdAsync(orderId))
                .ReturnsAsync(new Order { Id = orderId, EnterpriseId = enterpriseId });
            billRepoCustom.Setup(r => r.GetByOrderIdAsync(orderId)).ReturnsAsync(existing);

            var result = await service.GenerateAsync(orderId, enterpriseId);

            Assert.Same(existing, result);
            billRepo.Verify(r => r.CreateAsync(It.IsAny<BillEntity>()), Times.Never);
            orderRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>()), Times.Once);
        }

        [Fact]
        public async Task GenerateAsync_WhenOrderMissing_Throws()
        {
            var settings = CreateValidSettings();
            var (service, _, billRepoCustom, orderRepo, _) = CreateServiceWithMocks(settings);
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();

            billRepoCustom.Setup(r => r.GetByOrderIdAsync(orderId)).ReturnsAsync((BillEntity?)null);
            // IRepository.GetByIdAsync is declared non-nullable, so the nullable ReturnsAsync
            // overload does not match it. Task.FromResult says the same thing without asking Moq
            // to reconcile a nullability the interface never claimed.
            orderRepo.Setup(r => r.GetByIdAsync(orderId)).Returns(Task.FromResult<Order>(null!));

            await Assert.ThrowsAsync<NotFoundDatabaseException>(() => service.GenerateAsync(orderId, enterpriseId));
        }

        [Fact]
        public async Task GenerateAsync_WhenEnterpriseDoesNotMatch_ThrowsNotFound()
        {
            var settings = CreateValidSettings();
            var (service, _, billRepoCustom, orderRepo, _) = CreateServiceWithMocks(settings);
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();

            orderRepo
                .Setup(r => r.GetByIdAsync(orderId))
                .ReturnsAsync(new Order { Id = orderId, EnterpriseId = Guid.NewGuid() });
            billRepoCustom.Setup(r => r.GetByOrderIdAsync(orderId)).ReturnsAsync((BillEntity?)null);

            await Assert.ThrowsAsync<NotFoundDatabaseException>(() => service.GenerateAsync(orderId, enterpriseId));
            billRepoCustom.Verify(r => r.GetByOrderIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task GenerateAsync_WhenPayerDocumentInvalid_Throws()
        {
            var settings = CreateValidSettings();
            settings.DefaultPayerDocument = "123";
            var (service, _, billRepoCustom, orderRepo, customerRepo) = CreateServiceWithMocks(settings);
            var customerId = Guid.NewGuid();
            var order = CreateOrder(customerId);
            var enterpriseId = order.EnterpriseId ?? Guid.NewGuid();
            var customer = new Customer("Customer", "11999999999", "customer@test.com", "Rua Teste", "")
            {
                Id = customerId
            };

            billRepoCustom.Setup(r => r.GetByOrderIdAsync(order.Id)).ReturnsAsync((BillEntity?)null);
            orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);
            customerRepo.Setup(r => r.GetByIdAsync(customerId)).ReturnsAsync(customer);

            await Assert.ThrowsAsync<InsertDatabaseException>(() => service.GenerateAsync(order.Id, enterpriseId));
        }

        [Fact]
        public async Task GenerateAsync_WithValidData_CreatesBill()
        {
            var settings = CreateValidSettings();
            var (service, billRepo, billRepoCustom, orderRepo, customerRepo) = CreateServiceWithMocks(settings);
            var customerId = Guid.NewGuid();
            var order = CreateOrder(customerId);
            var enterpriseId = order.EnterpriseId ?? Guid.NewGuid();
            var customer = new Customer("Customer", "11999999999", "customer@test.com", "Rua Teste", "11144477735")
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

            var result = await service.GenerateAsync(order.Id, enterpriseId);

            Assert.NotNull(result);
            Assert.NotNull(created);
            Assert.Equal(order.Id, created?.OrderId);
            Assert.Equal(settings.BankCode, created?.BankCode);
            Assert.Equal(order.TotalValue, created?.Amount ?? 0, 2);
            Assert.False(string.IsNullOrWhiteSpace(created?.DigitableLine));
            Assert.False(string.IsNullOrWhiteSpace(created?.BarCode));
            Assert.False(string.IsNullOrWhiteSpace(created?.HtmlContent));
        }

        [Fact]
        public async Task GenerateAsync_WhenEmailStackIsUnavailable_StillReturnsBill()
        {
            var settings = CreateValidSettings();
            var billRepo = new Mock<IRepository<BillEntity>>();
            var billRepoCustom = new Mock<IBillsRepository<BillEntity>>();
            var orderRepo = new Mock<IRepository<Order>>();
            var customerRepo = new Mock<IRepository<Customer>>();
            var enterpriseRepo = new Mock<IRepository<Enterprise>>();

            var customerId = Guid.NewGuid();
            var order = CreateOrder(customerId);
            var enterpriseId = order.EnterpriseId!.Value;

            billRepoCustom.Setup(r => r.GetByOrderIdAsync(order.Id)).ReturnsAsync((BillEntity?)null);
            billRepoCustom
                .Setup(r => r.AllocateNextNumberAsync(enterpriseId, settings.BankCode, settings.Wallet, It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);
            orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);
            customerRepo.Setup(r => r.GetByIdAsync(customerId)).ReturnsAsync(
                new Customer("Customer", "11999999999", "customer@test.com", "Rua Teste", "11144477735") { Id = customerId });
            enterpriseRepo.Setup(r => r.GetByIdAsync(enterpriseId)).ReturnsAsync(
                new Enterprise("Enterprise", "11999999999", "enterprise@test.com", "Rua Teste", false) { Id = enterpriseId });
            billRepo.Setup(r => r.CreateAsync(It.IsAny<BillEntity>())).ReturnsAsync((BillEntity entity) => entity);
            billRepo.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var service = new BillsService(
                billRepo.Object,
                billRepoCustom.Object,
                orderRepo.Object,
                customerRepo.Object,
                enterpriseRepo.Object,
                new Mock<IRepository<BillNumberSequence>>().Object,
                () => throw new InvalidOperationException("email stack is down"),
                Options.Create(settings));

            var result = await service.GenerateAsync(order.Id, enterpriseId);

            Assert.NotNull(result);
            Assert.False(string.IsNullOrWhiteSpace(result!.DigitableLine));
        }

        [Fact]
        public async Task GenerateAsync_WithValidData_SendsBillToCustomerEmail()
        {
            var settings = CreateValidSettings();
            var billRepo = new Mock<IRepository<BillEntity>>();
            var billRepoCustom = new Mock<IBillsRepository<BillEntity>>();
            var orderRepo = new Mock<IRepository<Order>>();
            var customerRepo = new Mock<IRepository<Customer>>();
            var enterpriseRepo = new Mock<IRepository<Enterprise>>();
            var emailService = new Mock<IEmailService<EmailStructure>>();

            var customerId = Guid.NewGuid();
            var order = CreateOrder(customerId);
            var enterpriseId = order.EnterpriseId!.Value;
            var enterprise = new Enterprise("Enterprise", "11999999999", "enterprise@test.com", "Rua Teste", false)
            {
                Id = enterpriseId
            };
            var customer = new Customer("Customer", "11999999999", "customer@test.com", "Rua Teste", "11144477735")
            {
                Id = customerId
            };

            billRepoCustom.Setup(r => r.GetByOrderIdAsync(order.Id)).ReturnsAsync((BillEntity?)null);
            billRepoCustom
                .Setup(r => r.AllocateNextNumberAsync(enterpriseId, settings.BankCode, settings.Wallet, It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);
            orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);
            customerRepo.Setup(r => r.GetByIdAsync(customerId)).ReturnsAsync(customer);
            enterpriseRepo.Setup(r => r.GetByIdAsync(enterpriseId)).ReturnsAsync(enterprise);
            billRepo.Setup(r => r.CreateAsync(It.IsAny<BillEntity>())).ReturnsAsync((BillEntity entity) => entity);
            billRepo.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            EmailStructure? sentEmail = null;
            emailService
                .Setup(x => x.SendManualEmail(It.IsAny<EmailStructure>(), enterprise))
                .Callback<EmailStructure, Enterprise>((email, _) => sentEmail = email)
                .Returns(Task.CompletedTask);

            var service = new BillsService(
                billRepo.Object,
                billRepoCustom.Object,
                orderRepo.Object,
                customerRepo.Object,
                enterpriseRepo.Object,
                new Mock<IRepository<BillNumberSequence>>().Object,
                () => emailService.Object,
                Options.Create(settings));

            var result = await service.GenerateAsync(order.Id, enterpriseId);

            Assert.NotNull(result);
            Assert.NotNull(sentEmail);
            Assert.Equal(new[] { customer.Email }, sentEmail!.RecipientEmails);
            Assert.Contains(result!.DocumentNumber, sentEmail.Subject);
            // Payable details up front, then the full bill so the barcode can be scanned from the
            // email. EmailService turns those embedded images into cid: resources when sending.
            Assert.Contains(result.DigitableLine, sentEmail.Body);
            Assert.Contains(result.DocumentNumber, sentEmail.Body);
            Assert.Contains(result.HtmlContent, sentEmail.Body);
        }

        [Fact]
        public void BuildPagador_WithHtmlPayload_EncodesCustomerFields()
        {
            var settings = CreateValidSettings();
            var (service, _, _, _, _) = CreateServiceWithMocks(settings);
            var customer = new Customer(
                "<script>alert('x')</script>",
                "<img src=x onerror=alert(1)>",
                "attacker@example.com\"><script>alert(2)</script>",
                "<b>Rua Teste</b>",
                "11144477735");

            var method = typeof(BillsService).GetMethod("BuildPagador", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var result = method!.Invoke(service, [customer]);
            var pagador = Assert.IsType<Pagador>(result);

            Assert.DoesNotContain("<script>", pagador.Nome, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("&lt;script&gt;", pagador.Nome, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<b>", pagador.Endereco.LogradouroEndereco, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("&lt;b&gt;", pagador.Endereco.LogradouroEndereco, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<img", pagador.Telefone, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("&lt;img", pagador.Telefone, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<script>", pagador.Observacoes, StringComparison.OrdinalIgnoreCase);
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

            // Every generate path allocates, so the counter answers by default. Tests that care
            // about the allocated value override this setup.
            billRepoCustom
                .Setup(r => r.AllocateNextNumberAsync(
                    It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);

            var service = new BillsService(
                billRepo.Object,
                billRepoCustom.Object,
                orderRepo.Object,
                customerRepo.Object,
                new Mock<IRepository<Enterprise>>().Object,
                new Mock<IRepository<BillNumberSequence>>().Object,
                () => new Mock<IEmailService<EmailStructure>>().Object,
                Options.Create(settings));

            return (service, billRepo, billRepoCustom, orderRepo, customerRepo);
        }

        private static BillsSettings CreateValidSettings()
        {
            return new BillsSettings
            {
                Enabled = true,
                // Keeps the unit suite off the headless browser.
                AttachPdfToBillEmail = false,
                BankCode = 33,
                BeneficiaryDocument = "12345678000195",
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
                DefaultPayerDocument = "11144477735",
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
