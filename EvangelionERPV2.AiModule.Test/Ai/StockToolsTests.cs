using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Tools;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using Moq;
using System.Linq.Expressions;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    /// <summary>
    /// Stock has one door: <c>ProductService.ApplyStockMovementAsync</c>, which moves the quantity
    /// and writes the ledger row together. Bought stock arrives through a received purchase; a
    /// counted correction is an Adjustment. Neither is an assignment to StorageQuantity.
    /// </summary>
    public class StockToolsTests
    {
        private static readonly Guid Tenant = Guid.NewGuid();
        private static readonly AiToolContext Context = new(Tenant, Guid.NewGuid());

        [Fact]
        public async Task AjustarEstoque_AppliesTheDifferenceAsAnAdjustmentMovement()
        {
            var id = Guid.NewGuid();
            var repository = new Mock<IRepository<Product>>();
            repository.Setup(x => x.GetByIdAsync(id))
                .ReturnsAsync(new Product { Id = id, EnterpriseId = Tenant, Name = "teste", StorageQuantity = 7m });

            var service = new Mock<IProductService<Product>>();
            service.Setup(x => x.ApplyStockMovementAsync(
                    id, Tenant, It.IsAny<decimal>(), EnumStockMovementType.Adjustment, null, It.IsAny<bool>()))
                .ReturnsAsync((Guid _, Guid _, decimal delta, EnumStockMovementType _, Guid? _, bool _) =>
                    new StockMovement { ProductId = id, Quantity = delta, BalanceAfter = 7m + delta });

            var tool = new AjustarEstoqueTool(service.Object, repository.Object);

            var result = await Invoke(tool, new() { ["produtoId"] = id.ToString(), ["novoEstoque"] = 25m });

            service.Verify(x => x.ApplyStockMovementAsync(
                id, Tenant, 18m, EnumStockMovementType.Adjustment, null, It.IsAny<bool>()), Times.Once);
            Assert.Contains("Ajuste", result);
        }

        [Fact]
        public async Task AjustarEstoque_WithTheQuantityAlreadyOnHand_WritesNothing()
        {
            var id = Guid.NewGuid();
            var repository = new Mock<IRepository<Product>>();
            repository.Setup(x => x.GetByIdAsync(id))
                .ReturnsAsync(new Product { Id = id, EnterpriseId = Tenant, Name = "teste", StorageQuantity = 7m });

            var service = new Mock<IProductService<Product>>();
            var tool = new AjustarEstoqueTool(service.Object, repository.Object);

            var result = await Invoke(tool, new() { ["produtoId"] = id.ToString(), ["novoEstoque"] = 7m });

            Assert.Contains("nada para alterar", result);
            service.Verify(x => x.ApplyStockMovementAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                It.IsAny<EnumStockMovementType>(), It.IsAny<Guid?>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task AjustarEstoque_RefusesANegativeCount()
        {
            var tool = new AjustarEstoqueTool(Mock.Of<IProductService<Product>>(), Mock.Of<IRepository<Product>>());

            var prepared = await tool.PrepareAsync(
                new Dictionary<string, object?> { ["produtoId"] = Guid.NewGuid().ToString(), ["novoEstoque"] = -1m },
                Context);

            Assert.Equal("o estoque contado não pode ser negativo.", prepared.Error);
        }

        [Fact]
        public async Task ReceberCompra_MarksThePurchaseReceived()
        {
            var billId = Guid.NewGuid();
            var service = new Mock<IPayableBillService>();
            service.Setup(x => x.MarkProductsReceivedAsync(billId, Tenant))
                .ReturnsAsync(new PayableBill
                {
                    Id = billId,
                    Description = "Compra de bebidas",
                    ProductsReceivedAt = DateTime.UtcNow,
                });

            var tool = new ReceberCompraTool(service.Object, Mock.Of<IRepository<PayableBill>>());

            var result = await Invoke(tool, new() { ["compraId"] = billId.ToString() });

            Assert.Contains("entrada lançada no estoque", result);
            service.Verify(x => x.MarkProductsReceivedAsync(billId, Tenant), Times.Once);
        }

        [Fact]
        public async Task ReceberCompra_ExplainsASecondReceipt()
        {
            var billId = Guid.NewGuid();
            var service = new Mock<IPayableBillService>();
            service.Setup(x => x.MarkProductsReceivedAsync(billId, Tenant))
                .ThrowsAsync(new InsertDatabaseException("Products were already received for this payable bill."));

            var tool = new ReceberCompraTool(service.Object, Mock.Of<IRepository<PayableBill>>());

            var result = await Invoke(tool, new() { ["compraId"] = billId.ToString() });

            Assert.Contains("já teve a entrada lançada", result);
        }

        [Fact]
        public async Task ReceberCompra_FindsThePurchaseByItsDescription()
        {
            var billId = Guid.NewGuid();
            var repository = new Mock<IRepository<PayableBill>>();
            repository.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>>()))
                .ReturnsAsync((Expression<Func<PayableBill, bool>> predicate) => new[]
                {
                    new PayableBill
                    {
                        Id = billId,
                        EnterpriseId = Tenant,
                        IsActive = true,
                        BillType = (int)EnumPayableBillType.ProductStock,
                        Description = "Compra de bebidas",
                    },
                }.Where(predicate.Compile()));

            var tool = new ReceberCompraTool(Mock.Of<IPayableBillService>(), repository.Object);

            var prepared = await tool.PrepareAsync(
                new Dictionary<string, object?> { ["compraId"] = "bebidas" }, Context);

            Assert.Null(prepared.Error);
            Assert.Equal(billId.ToString(), prepared.Arguments["compraId"]);
        }

        [Fact]
        public async Task ComprasPendentes_ListsOnlyPurchasesStillWaitingForTheGoods()
        {
            var repository = new Mock<IRepository<PayableBill>>();
            repository.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>>()))
                .ReturnsAsync((Expression<Func<PayableBill, bool>> predicate) => new[]
                {
                    new PayableBill
                    {
                        Id = Guid.NewGuid(), EnterpriseId = Tenant, IsActive = true,
                        BillType = (int)EnumPayableBillType.ProductStock,
                        Description = "Compra de bebidas", Amount = 500m, DueDate = DateTime.UtcNow,
                    },
                    new PayableBill
                    {
                        Id = Guid.NewGuid(), EnterpriseId = Tenant, IsActive = true,
                        BillType = (int)EnumPayableBillType.ProductStock,
                        Description = "Compra já recebida", ProductsReceivedAt = DateTime.UtcNow,
                    },
                    new PayableBill
                    {
                        Id = Guid.NewGuid(), EnterpriseId = Tenant, IsActive = true,
                        BillType = (int)EnumPayableBillType.OperatingCost, Description = "Aluguel",
                    },
                }.Where(predicate.Compile()));

            var result = await Invoke(new ComprasPendentesTool(repository.Object), new());

            Assert.Contains("Compra de bebidas", result);
            Assert.DoesNotContain("Compra já recebida", result);
            Assert.DoesNotContain("Aluguel", result);
        }

        private static async Task<string> Invoke(IAiTool tool, Dictionary<string, object?> arguments)
        {
            var value = await tool.Create(Context).InvokeAsync(new AIFunctionArguments(arguments));

            return value?.ToString() ?? string.Empty;
        }
    }
}
