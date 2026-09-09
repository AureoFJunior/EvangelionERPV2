using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Tools;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using Moq;
using System.Linq.Expressions;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    public class BuscarProdutosToolTests
    {
        [Fact]
        public void Tool_DeclaresItsNameAndPermission()
        {
            var tool = new BuscarProdutosTool(new Mock<IRepository<Product>>().Object);

            Assert.Equal("buscar_produtos", tool.Name);
            Assert.Equal("products.read", tool.RequiredPermission);
            Assert.False(tool.RequiresConfirmation);
        }

        [Fact]
        public async Task Invoking_ReturnsTheMatchingProductsAsJson()
        {
            var repository = new Mock<IRepository<Product>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Product, bool>>>()))
                .ReturnsAsync(new[]
                {
                    new Product { Id = Guid.NewGuid(), Name = "Coca-Cola 2L", DefaultValue = 9.5m, StorageQuantity = 12 },
                });

            var tool = new BuscarProdutosTool(repository.Object);
            var function = tool.Create(new AiToolContext(Guid.NewGuid(), Guid.NewGuid()));

            var result = await function.InvokeAsync(new AIFunctionArguments { ["termo"] = "coca" });

            var json = result?.ToString() ?? string.Empty;
            Assert.Contains("Coca-Cola 2L", json);
            Assert.Contains("\"total\":1", json);
        }

        [Fact]
        public async Task Invoking_WithNoMatches_SaysZeroRatherThanFailing()
        {
            var repository = new Mock<IRepository<Product>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Product, bool>>>()))
                .ReturnsAsync(Array.Empty<Product>());

            var tool = new BuscarProdutosTool(repository.Object);
            var function = tool.Create(new AiToolContext(Guid.NewGuid(), Guid.NewGuid()));

            var result = await function.InvokeAsync(new AIFunctionArguments { ["termo"] = "nao existe" });

            Assert.Contains("\"total\":0", result?.ToString() ?? string.Empty);
        }
    }
}
