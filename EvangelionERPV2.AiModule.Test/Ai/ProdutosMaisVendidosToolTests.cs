using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Tools;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using Moq;
using System.Linq.Expressions;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    public class ProdutosMaisVendidosToolTests
    {
        private static readonly Guid Tenant = Guid.NewGuid();
        private static readonly Guid Coca = Guid.NewGuid();
        private static readonly Guid Monster = Guid.NewGuid();

        [Fact]
        public void Tool_DeclaresItsNameAndPermission()
        {
            var tool = new ProdutosMaisVendidosTool(new Mock<IRepository<Order>>().Object);

            Assert.Equal("produtos_mais_vendidos", tool.Name);
            Assert.Equal("products.read", tool.RequiredPermission);
            Assert.False(tool.RequiresConfirmation);
        }

        [Fact]
        public async Task Invoking_RanksByQuantitySoldAcrossOrders()
        {
            var repository = OrdersReturning(new[]
            {
                Order(EnumOrderStatus.Finished, Line(Coca, "Coca-Cola 2L", 2, 10m), Line(Monster, "Monster Rosa", 1, 12m)),
                Order(EnumOrderStatus.Paid, Line(Coca, "Coca-Cola 2L", 3, 10m)),
            });

            var json = await Invoke(repository);
            using var document = JsonDocument.Parse(json);
            var items = document.RootElement.GetProperty("itens");

            Assert.Equal("Coca-Cola 2L", items[0].GetProperty("nome").GetString());
            Assert.Equal(5, items[0].GetProperty("quantidade").GetDecimal());
            Assert.Equal(50m, items[0].GetProperty("faturamento").GetDecimal());
            Assert.Equal(2, items[0].GetProperty("pedidos").GetInt32());
            Assert.Equal("Monster Rosa", items[1].GetProperty("nome").GetString());
        }

        [Fact]
        public async Task Invoking_IgnoresRefundedOrders()
        {
            var refunded = Order(EnumOrderStatus.Finished, Line(Monster, "Monster Rosa", 99, 12m));
            refunded.RefundedAt = DateTime.UtcNow;

            var repository = OrdersReturning(new[]
            {
                Order(EnumOrderStatus.Finished, Line(Coca, "Coca-Cola 2L", 1, 10m)),
                refunded,
            });

            var json = await Invoke(repository);

            Assert.Contains("Coca-Cola 2L", json);
            Assert.DoesNotContain("Monster Rosa", json);
        }

        [Fact]
        public async Task Invoking_HonoursTheLimit()
        {
            var repository = OrdersReturning(new[]
            {
                Order(EnumOrderStatus.Finished, Line(Coca, "Coca-Cola 2L", 5, 10m), Line(Monster, "Monster Rosa", 1, 12m)),
            });

            var json = await Invoke(repository, new AIFunctionArguments { ["limite"] = 1 });

            Assert.Contains("Coca-Cola 2L", json);
            Assert.DoesNotContain("Monster Rosa", json);
        }

        [Fact]
        public async Task Invoking_WithNoSales_SaysZeroRatherThanFailing()
        {
            var json = await Invoke(OrdersReturning(Array.Empty<Order>()));

            Assert.Contains("\"total\":0", json);
        }

        [Fact]
        public async Task Invoking_WhenRepositoryThrowsNotFound_ReportsZeroRatherThanFailing()
        {
            var repository = new Mock<IRepository<Order>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>>()))
                .ThrowsAsync(new NotFoundDatabaseException());

            Assert.Contains("\"total\":0", await Invoke(repository));
        }

        private static async Task<string> Invoke(Mock<IRepository<Order>> repository, AIFunctionArguments? arguments = null)
        {
            var tool = new ProdutosMaisVendidosTool(repository.Object);
            var function = tool.Create(new AiToolContext(Tenant, Guid.NewGuid()));
            var result = await function.InvokeAsync(arguments ?? new AIFunctionArguments());

            return result?.ToString() ?? string.Empty;
        }

        private static Mock<IRepository<Order>> OrdersReturning(IReadOnlyList<Order> orders)
        {
            var repository = new Mock<IRepository<Order>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>>()))
                .ReturnsAsync((Expression<Func<Order, bool>> predicate) => orders.Where(predicate.Compile()));

            return repository;
        }

        private static Order Order(EnumOrderStatus status, params OrderedProduct[] lines) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = Tenant,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            Status = (int)status,
            OrderedProduct = lines,
        };

        private static OrderedProduct Line(Guid productId, string name, decimal quantity, decimal value) => new()
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            ProductId = productId,
            Quantity = quantity,
            Value = value,
            Product = new Product { Id = productId, Name = name },
        };
    }
}
