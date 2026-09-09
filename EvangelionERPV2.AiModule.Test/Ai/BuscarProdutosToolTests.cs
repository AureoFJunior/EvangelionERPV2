using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Tools;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
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

        /// <summary>
        /// The real ProductRepository throws NotFoundDatabaseException on zero matches instead of
        /// returning an empty list. A search matching nothing is a normal answer, not a failure.
        /// </summary>
        [Fact]
        public async Task Invoking_WhenRepositoryThrowsNotFound_ReportsZeroRatherThanFailing()
        {
            var repository = new Mock<IRepository<Product>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Product, bool>>>()))
                .ThrowsAsync(new NotFoundDatabaseException());

            var tool = new BuscarProdutosTool(repository.Object);
            var function = tool.Create(new AiToolContext(Guid.NewGuid(), Guid.NewGuid()));

            var result = await function.InvokeAsync(new AIFunctionArguments { ["termo"] = "nao existe" });

            Assert.Contains("\"total\":0", result?.ToString() ?? string.Empty);
        }

        [Fact]
        public async Task Invoking_WithEmptyTerm_ReturnsErrorRatherThanEverything()
        {
            var repository = new Mock<IRepository<Product>>();

            var tool = new BuscarProdutosTool(repository.Object);
            var function = tool.Create(new AiToolContext(Guid.NewGuid(), Guid.NewGuid()));

            var result = await function.InvokeAsync(new AIFunctionArguments { ["termo"] = "  " });

            Assert.Contains("\"erro\"", result?.ToString() ?? string.Empty);
            repository.Verify(
                x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Product, bool>>>()),
                Times.Never);
        }

        /// <summary>
        /// The repository is read one row past the display cap specifically so ForModel can tell
        /// "exactly 20 matched" from "more than 20 matched". Without that extra row, total and
        /// mostrando are always equal and truncado can never be true.
        /// </summary>
        [Fact]
        public async Task Invoking_WithMoreThanTheDisplayCap_ReportsTruncated()
        {
            var repository = new Mock<IRepository<Product>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Product, bool>>>()))
                .ReturnsAsync(Enumerable.Range(1, 21).Select(i => new Product { Id = Guid.NewGuid(), Name = $"Produto {i}" }));

            var tool = new BuscarProdutosTool(repository.Object);
            var function = tool.Create(new AiToolContext(Guid.NewGuid(), Guid.NewGuid()));

            var result = await function.InvokeAsync(new AIFunctionArguments { ["termo"] = "produto" });

            var json = result?.ToString() ?? string.Empty;
            Assert.Contains("\"mostrando\":20", json);
            Assert.Contains("\"truncado\":true", json);
        }
    }
}
