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
        private static readonly Guid Tenant = Guid.NewGuid();

        /// <summary>A row the tool's tenant predicate will actually match.</summary>
        private static Product Row(string name, decimal price = 0, decimal stock = 0, bool active = true) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = Tenant,
            IsActive = active,
            Name = name,
            DefaultValue = price,
            StorageQuantity = stock,
        };

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
            var repository = PagedRepository(new[]
            {
                Row("Coca-Cola 2L", 9.5m, 12),
            });

            var json = await Invoke(repository, new AIFunctionArguments { ["termo"] = "Coca" });

            Assert.Contains("Coca-Cola 2L", json);
            Assert.Contains("\"total\":1", json);
        }

        [Fact]
        public async Task Invoking_WithNoMatches_SaysZeroRatherThanFailing()
        {
            var json = await Invoke(PagedRepository(Array.Empty<Product>()), new AIFunctionArguments { ["termo"] = "nao existe" });

            Assert.Contains("\"total\":0", json);
        }

        [Fact]
        public async Task Invoking_WhenRepositoryThrowsNotFound_ReportsZeroRatherThanFailing()
        {
            var repository = new Mock<IRepository<Product>>();
            repository
                .Setup(x => x.GetPagedAsync(
                    It.IsAny<Expression<Func<Product, bool>>>(),
                    It.IsAny<Func<IQueryable<Product>, IOrderedQueryable<Product>>>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ThrowsAsync(new NotFoundDatabaseException());

            var json = await Invoke(repository, new AIFunctionArguments { ["termo"] = "nao existe" });

            Assert.Contains("\"total\":0", json);
        }

        [Fact]
        public async Task Invoking_WithNoTerm_ListsTheCatalogueInsteadOfRefusing()
        {
            var repository = PagedRepository(new[]
            {
                Row("Coca-Cola 2L"),
                Row("Monster Rosa"),
            });

            var json = await Invoke(repository, new AIFunctionArguments());

            Assert.Contains("Coca-Cola 2L", json);
            Assert.Contains("Monster Rosa", json);
            Assert.Contains("\"total\":2", json);
            Assert.DoesNotContain("\"erro\"", json);
        }

        [Fact]
        public async Task Invoking_DoesNotReturnSoftDeletedProducts()
        {
            var repository = PagedRepository(new[]
            {
                Row("Coca-Cola 2L"),
                Row("Coca-Cola 600ml", active: false),
            });

            var json = await Invoke(repository, new AIFunctionArguments { ["termo"] = "Coca" });

            Assert.Contains("Coca-Cola 2L", json);
            Assert.DoesNotContain("Coca-Cola 600ml", json);
        }

        [Fact]
        public async Task Invoking_WithMoreThanOnePage_SaysWhichPageToAskForNext()
        {
            var repository = PagedRepository(
                Enumerable.Range(1, 21).Select(i => Row($"Produto {i:00}")).ToList());

            var json = await Invoke(repository, new AIFunctionArguments { ["termo"] = "Produto" });

            Assert.Contains("\"total\":21", json);
            Assert.Contains("\"mostrando\":20", json);
            Assert.Contains("\"truncado\":true", json);
            Assert.Contains("\"proximaPagina\":2", json);
        }

        [Fact]
        public async Task Invoking_TheSecondPage_ReturnsTheRestAndStopsAskingForMore()
        {
            var repository = PagedRepository(
                Enumerable.Range(1, 21).Select(i => Row($"Produto {i:00}")).ToList());

            var json = await Invoke(repository, new AIFunctionArguments { ["pagina"] = 2 });

            Assert.Contains("\"pagina\":2", json);
            Assert.Contains("\"mostrando\":1", json);
            Assert.Contains("\"truncado\":false", json);
            Assert.Contains("\"proximaPagina\":null", json);
        }

        private static async Task<string> Invoke(
            Mock<IRepository<Product>> repository, AIFunctionArguments arguments)
        {
            var tool = new BuscarProdutosTool(repository.Object);
            var function = tool.Create(new AiToolContext(Tenant, Guid.NewGuid()));
            var result = await function.InvokeAsync(arguments);

            return result?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Applies the predicate and the paging the way the real repository does. One difference to
        /// know about: <c>string.Contains</c> compiled in memory is case-sensitive, while the same
        /// predicate on SQL Server matches under the database collation, which is not. Search terms
        /// here therefore match the stored case; case-insensitivity is the database's to keep.
        /// </summary>
        private static Mock<IRepository<Product>> PagedRepository(IReadOnlyList<Product> rows)
        {
            var repository = new Mock<IRepository<Product>>();
            repository
                .Setup(x => x.GetPagedAsync(
                    It.IsAny<Expression<Func<Product, bool>>>(),
                    It.IsAny<Func<IQueryable<Product>, IOrderedQueryable<Product>>>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync((
                    Expression<Func<Product, bool>>? predicate,
                    Func<IQueryable<Product>, IOrderedQueryable<Product>>? orderBy,
                    int? page,
                    int? size) =>
                {
                    var matched = predicate is null ? rows : rows.Where(predicate.Compile()).ToList();
                    var ordered = orderBy is null ? matched.AsQueryable() : orderBy(matched.AsQueryable());
                    var items = ordered
                        .Skip(((page ?? 1) - 1) * (size ?? matched.Count))
                        .Take(size ?? matched.Count)
                        .ToList();

                    return ((IReadOnlyList<Product>)items, matched.Count);
                });

            return repository;
        }
    }
}
