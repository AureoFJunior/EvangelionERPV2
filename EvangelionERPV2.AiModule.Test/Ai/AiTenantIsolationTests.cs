using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.AiModule.Application.Tools;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using Moq;
using System.Linq.Expressions;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    /// <summary>
    /// The assistant answers about one company and only one company.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tenant query filter in <c>AppDbContext</c> is the first line of defence, but it is not the
    /// one being tested here — and deliberately so. It stands down outside a request and for a
    /// self-API machine token, and the agent's tools are exactly the kind of code that runs with an
    /// id handed to it by a language model. So every tool scopes explicitly, and these tests compile
    /// each tool's own predicate against rows from two tenants to prove it.
    /// </para>
    /// <para>
    /// A failure here is not a bug in a screen. It is one company reading another's customers,
    /// prices, debts or stock — LGPD art. 46 in the plainest terms.
    /// </para>
    /// </remarks>
    public class AiTenantIsolationTests
    {
        private static readonly Guid Mine = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid Theirs = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid Caller = Guid.Parse("33333333-3333-3333-3333-333333333333");

        private static AiToolContext MyContext => new(Mine, Caller);

        [Fact]
        public async Task SearchingProducts_NeverReturnsAnotherCompanysStock()
        {
            var rows = new[]
            {
                Product("Coca-Cola 2L", Mine),
                Product("Coca-Cola 2L", Theirs),
            };

            var json = await InvokeProductSearch(rows, new AIFunctionArguments { ["termo"] = "Coca" });

            // Same product name in both companies: the count is what gives a leak away.
            Assert.Contains("\"total\":1", json);
        }

        [Fact]
        public async Task SearchingCustomers_NeverReturnsAnotherCompanysPeople()
        {
            // Customers are personal data. This one is the LGPD case rather than a commercial one.
            var rows = new[]
            {
                Customer("Maria Silva", Mine),
                Customer("João Souza", Theirs),
            };

            var repository = new Mock<IRepository<Customer>>();
            repository
                .Setup(x => x.GetPagedAsync(
                    It.IsAny<Expression<Func<Customer, bool>>>(),
                    It.IsAny<Func<IQueryable<Customer>, IOrderedQueryable<Customer>>>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync((
                    Expression<Func<Customer, bool>>? predicate,
                    Func<IQueryable<Customer>, IOrderedQueryable<Customer>>? orderBy,
                    int? page,
                    int? size) =>
                {
                    var matched = predicate is null ? rows.ToList() : rows.Where(predicate.Compile()).ToList();
                    return ((IReadOnlyList<Customer>)matched, matched.Count);
                });

            var tool = new BuscarClientesTool(repository.Object);
            var json = (await tool.Create(MyContext).InvokeAsync(new AIFunctionArguments()))?.ToString() ?? string.Empty;

            Assert.Contains("Maria Silva", json);
            Assert.DoesNotContain("João Souza", json);
        }

        [Fact]
        public async Task AdjustingStock_WithAnotherCompanysProductId_IsRefused()
        {
            // The id comes from a language model, which can be talked into anything. The tool cannot.
            var theirProduct = Product("Produto deles", Theirs);
            var repository = new Mock<IRepository<Product>>();
            repository.Setup(x => x.GetByIdAsync(theirProduct.Id)).ReturnsAsync(theirProduct);

            var service = new Mock<IProductService<Product>>();
            var tool = new AjustarEstoqueTool(service.Object, repository.Object);

            var json = (await tool.Create(MyContext).InvokeAsync(new AIFunctionArguments
            {
                ["produtoId"] = theirProduct.Id.ToString(),
                ["novoEstoque"] = 10d,
            }))?.ToString() ?? string.Empty;

            Assert.Contains("erro", json);
            service.Verify(
                x => x.ApplyStockMovementAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                    It.IsAny<EnumStockMovementType>(), It.IsAny<Guid?>(), It.IsAny<bool>()),
                Times.Never);
        }

        [Fact]
        public async Task UpdatingAProduct_WithAnotherCompanysProductId_IsRefused()
        {
            var theirProduct = Product("Produto deles", Theirs);
            var repository = new Mock<IRepository<Product>>();
            repository.Setup(x => x.GetByIdAsync(theirProduct.Id)).ReturnsAsync(theirProduct);

            var tool = new AtualizarProdutoTool(new Mock<IProductService<Product>>().Object, repository.Object);

            var json = (await tool.Create(MyContext).InvokeAsync(new AIFunctionArguments
            {
                ["produtoId"] = theirProduct.Id.ToString(),
                ["preco"] = 1d,
            }))?.ToString() ?? string.Empty;

            Assert.Contains("erro", json);
            repository.Verify(x => x.Update(It.IsAny<Product>()), Times.Never);
        }

        [Fact]
        public async Task ResolvingAName_NeverReachesIntoAnotherCompany()
        {
            // The lookup exists because the model truncates GUIDs and offers names instead. It must
            // not become a way to find rows the caller could not otherwise see.
            var rows = new[] { Product("Monster Rosa", Theirs) };
            var repository = new Mock<IRepository<Product>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Product, bool>>>()))
                .ReturnsAsync((Expression<Func<Product, bool>> predicate) => rows.Where(predicate.Compile()).ToList());

            var resolved = await AiEntityLookup.ProductAsync(repository.Object, "Monster Rosa", Mine);

            Assert.Null(resolved.Id);
            Assert.NotNull(resolved.Error);
        }

        private static async Task<string> InvokeProductSearch(IReadOnlyList<Product> rows, AIFunctionArguments arguments)
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
                    var matched = predicate is null ? rows.ToList() : rows.Where(predicate.Compile()).ToList();
                    return ((IReadOnlyList<Product>)matched, matched.Count);
                });

            var tool = new BuscarProdutosTool(repository.Object);

            return (await tool.Create(MyContext).InvokeAsync(arguments))?.ToString() ?? string.Empty;
        }

        private static Product Product(string name, Guid enterpriseId) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            IsActive = true,
            Name = name,
            DefaultValue = 10,
            StorageQuantity = 5,
        };

        private static Customer Customer(string name, Guid enterpriseId) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = enterpriseId,
            IsActive = true,
            Name = name,
        };
    }
}
