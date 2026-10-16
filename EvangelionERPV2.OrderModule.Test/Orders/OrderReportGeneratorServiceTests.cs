using EvangelionERPV2.OrderModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.OrderModule.Test.Orders
{
    public class OrderReportGeneratorServiceTests
    {
        [Fact]
        public async Task TopProductsRevenue_UsesEagerLoadedVariantDisplayName()
        {
            var product = new Product("Shirt", "", 20, 1, false, false, null, Guid.NewGuid())
            {
                Id = Guid.NewGuid(),
                VariantLabel = "Blue / Large"
            };
            var line = new OrderedProduct
            {
                ProductId = product.Id,
                Product = product,
                Quantity = 2,
                Value = 20,
                UnitOfMeasure = "unit"
            };
            var order = new Order { OrderedProduct = [line] };
            var service = new OrderReportGeneratorService(new Mock<IRepository<Product>>().Object);

            var html = await service.GenerateTopProductsByRevenueReportAsync(
                new Enterprise("Enterprise", "11999999999", "enterprise@test.com", "Street", true),
                [order]);

            Assert.Contains("Shirt — Blue / Large", html);
        }
    }
}
