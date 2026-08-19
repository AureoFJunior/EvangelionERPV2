using System.Text.Json;
using EvangelionERPV2.BillsModule.Application.Configs;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.BillsModule.Domain.Interface;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Options;
using Moq;
using BillEntity = EvangelionERPV2.Shared.Entities.Bill;

namespace EvangelionERPV2.BillsModule.Test
{
    /// <summary>
    /// Guards the shipped BillsSettings against values BoletoNetCore rejects
    /// (unimplemented bank code, invalid wallet/document), which only surface at runtime.
    /// </summary>
    public class BillsSettingsConfigTests
    {
        [Theory]
        [InlineData("appsettings.Development.json")]
        [InlineData("appsettings.Production.json")]
        public async Task ShippedBillsSettings_CanGenerateABill(string appSettingsFile)
        {
            var path = Path.Combine("../../../../EvangelionERPV2.Web", appSettingsFile);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var settings = JsonSerializer.Deserialize<BillsSettings>(
                document.RootElement.GetProperty("BillsSettings").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            // Production ships disabled; the settings themselves must still be usable.
            settings.Enabled = true;
            // Keeps this test off the headless browser.
            settings.AttachPdfToBillEmail = false;

            var billRepo = new Mock<IRepository<BillEntity>>();
            var billRepoCustom = new Mock<IBillsRepository<BillEntity>>();
            var orderRepo = new Mock<IRepository<Order>>();
            var customerRepo = new Mock<IRepository<Customer>>();

            var customerId = Guid.NewGuid();
            var order = new Order(
                DateTime.UtcNow.AddDays(5),
                DateTime.UtcNow.AddDays(5),
                100,
                Guid.NewGuid(),
                customerId,
                new List<OrderedProduct>(),
                Guid.NewGuid())
            {
                Id = Guid.NewGuid()
            };
            var enterpriseId = order.EnterpriseId!.Value;

            orderRepo.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);
            customerRepo
                .Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(new Customer("Customer", "11999999999", "customer@test.com", "Rua Teste", "11144477735")
                {
                    Id = customerId
                });
            billRepoCustom.Setup(r => r.GetByOrderIdAsync(order.Id)).ReturnsAsync((BillEntity?)null);
            billRepoCustom
                .Setup(r => r.AllocateNextNumberAsync(enterpriseId, settings.BankCode, settings.Wallet, It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);
            billRepo.Setup(r => r.CreateAsync(It.IsAny<BillEntity>())).ReturnsAsync((BillEntity entity) => entity);
            billRepo.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var service = new BillsService(
                billRepo.Object,
                billRepoCustom.Object,
                orderRepo.Object,
                customerRepo.Object,
                new Mock<IRepository<Enterprise>>().Object,
                new Mock<IRepository<BillNumberSequence>>().Object,
                () => new Mock<IEmailService<EmailStructure>>().Object,
                Options.Create(settings));

            var bill = await service.GenerateAsync(order.Id, enterpriseId);

            Assert.NotNull(bill);
            Assert.False(string.IsNullOrWhiteSpace(bill!.DigitableLine));
            Assert.False(string.IsNullOrWhiteSpace(bill.BarCode));
        }
    }
}
