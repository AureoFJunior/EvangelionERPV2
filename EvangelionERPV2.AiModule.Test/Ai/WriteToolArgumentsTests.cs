using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Tools;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using Moq;
using System.Linq.Expressions;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    /// <summary>
    /// A confirmed action replays only the arguments the model actually sent, so every parameter the
    /// model may omit needs a C# default — without one <c>AIFunctionFactory</c> calls it required and
    /// the invoke throws <see cref="ArgumentException"/> at confirm time, long after the proposal
    /// looked fine. That shipped once; these are the calls that catch it coming back.
    /// </summary>
    public class WriteToolArgumentsTests
    {
        private static readonly AiToolContext Context = new(Guid.NewGuid(), Guid.NewGuid());

        [Fact]
        public async Task CriarCliente_RunsWithOnlyTheNameSupplied()
        {
            var service = new Mock<ICustomerService<Customer>>();
            service.Setup(x => x.CreateAsync(It.IsAny<Customer>()))
                .ReturnsAsync((Customer customer) => customer);

            var tool = new CriarClienteTool(service.Object);

            var result = await Invoke(tool, new() { ["nome"] = "Ana" });

            Assert.Contains("Ana", result);
            service.Verify(x => x.CreateAsync(It.IsAny<Customer>()), Times.Once);
        }

        [Fact]
        public async Task AtualizarCliente_RunsWithOnlyTheIdSupplied()
        {
            var tool = new AtualizarClienteTool(
                Mock.Of<ICustomerService<Customer>>(), Mock.Of<IRepository<Customer>>());

            var result = await Invoke(tool, new() { ["clienteId"] = Guid.NewGuid().ToString() });

            Assert.Contains("erro", result);
        }

        [Fact]
        public async Task AtualizarProduto_RunsWithTheIdAndAPrice()
        {
            var tool = new AtualizarProdutoTool(
                Mock.Of<IProductService<Product>>(), Mock.Of<IRepository<Product>>());

            var result = await Invoke(tool, new() { ["produtoId"] = Guid.NewGuid().ToString(), ["preco"] = 15m });

            // The product does not exist in this mock, which is as far as the argument binding needs to get.
            Assert.Contains("produto não encontrado", result);
        }

        [Fact]
        public async Task CriarPedido_RefusesAnEmptyItemList()
        {
            var tool = new CriarPedidoTool(
                Mock.Of<IOrderService<Order>>(),
                Mock.Of<IRepository<Product>>(),
                Mock.Of<IRepository<Customer>>());

            var result = await Invoke(tool, new()
            {
                ["clienteId"] = Guid.NewGuid().ToString(),
                ["itens"] = Array.Empty<ItemDoPedido>(),
            });

            Assert.Contains("pelo menos um item", result);
        }

        [Fact]
        public void EveryWriteToolSummarisesItselfForTheCard()
        {
            var tool = new CriarClienteTool(Mock.Of<ICustomerService<Customer>>());

            var summary = tool.Summarize(new Dictionary<string, object?>
            {
                ["nome"] = "Ana",
                ["telefone"] = "11999990000",
            });

            Assert.Equal("Cadastrar o cliente Ana, telefone 11999990000", summary);
            Assert.True(tool.RequiresConfirmation);
        }

        [Fact]
        public async Task CriarPedido_ResolvesTheCustomerAndEveryLineByName()
        {
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var tool = new CriarPedidoTool(
                Mock.Of<IOrderService<Order>>(),
                ProductsNamed((productId, "Monster Rosa")),
                CustomersNamed((customerId, "Padaria do Zé")));

            var prepared = await tool.PrepareAsync(new Dictionary<string, object?>
            {
                ["clienteId"] = "Padaria do Zé",
                ["itens"] = JsonSerializer.SerializeToElement(new[] { new { produtoId = "Monster Rosa", quantidade = 2 } }),
            }, Context);

            Assert.Null(prepared.Error);
            Assert.Equal(customerId.ToString(), prepared.Arguments["clienteId"]);
            Assert.Contains(productId.ToString(), prepared.Arguments["itens"]!.ToString());
        }

        [Fact]
        public async Task CriarPedido_RefusesALineWithNoQuantity()
        {
            var tool = new CriarPedidoTool(
                Mock.Of<IOrderService<Order>>(),
                ProductsNamed((Guid.NewGuid(), "Monster Rosa")),
                CustomersNamed((Guid.NewGuid(), "Padaria do Zé")));

            var prepared = await tool.PrepareAsync(new Dictionary<string, object?>
            {
                ["clienteId"] = "Padaria do Zé",
                ["itens"] = JsonSerializer.SerializeToElement(new[] { new { produtoId = "Monster Rosa", quantidade = 0 } }),
            }, Context);

            Assert.Equal("quantidade deve ser maior que zero.", prepared.Error);
        }

        [Fact]
        public async Task AtualizarProduto_DoesNotTouchStockAtAll()
        {
            // Stock only moves through ProductService.ApplyStockMovementAsync, which writes the
            // ledger row. This tool assigning StorageQuantity is what made the scalar and the
            // StockMovement history disagree.
            var id = Guid.NewGuid();
            var stored = new Product { Id = id, EnterpriseId = Context.EnterpriseId, Name = "teste", DefaultValue = 12m, StorageQuantity = 7m };
            var repository = new Mock<IRepository<Product>>();
            repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(stored);

            var service = new Mock<IProductService<Product>>();
            service.Setup(x => x.UpdateAsync(It.IsAny<Product>())).ReturnsAsync((Product p) => p);

            var tool = new AtualizarProdutoTool(service.Object, repository.Object);

            var result = await Invoke(tool, new() { ["produtoId"] = id.ToString(), ["preco"] = 15m, ["estoque"] = 999m });

            Assert.Equal(7m, stored.StorageQuantity);
            Assert.DoesNotContain("estoque", result);
            service.Verify(x => x.ApplyStockMovementAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                It.IsAny<EnumStockMovementType>(), It.IsAny<Guid?>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task AtualizarProduto_WithValuesTheProductAlreadyHas_DoesNotReportAChange()
        {
            var id = Guid.NewGuid();
            var repository = new Mock<IRepository<Product>>();
            repository.Setup(x => x.GetByIdAsync(id))
                .ReturnsAsync(new Product { Id = id, EnterpriseId = Context.EnterpriseId, Name = "teste", DefaultValue = 12m, StorageQuantity = 7m });

            var service = new Mock<IProductService<Product>>();
            var tool = new AtualizarProdutoTool(service.Object, repository.Object);

            // The model echoes the price it just read.
            var result = await Invoke(tool, new() { ["produtoId"] = id.ToString(), ["preco"] = 12m });

            Assert.Contains("nada para alterar", result);
            service.Verify(x => x.UpdateAsync(It.IsAny<Product>()), Times.Never);
        }

        [Fact]
        public async Task AtualizarProduto_AppliesTheFieldThatActuallyChanged()
        {
            var id = Guid.NewGuid();
            var stored = new Product { Id = id, EnterpriseId = Context.EnterpriseId, Name = "teste", DefaultValue = 12m, StorageQuantity = 7m };
            var repository = new Mock<IRepository<Product>>();
            repository.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(stored);

            var service = new Mock<IProductService<Product>>();
            service.Setup(x => x.UpdateAsync(It.IsAny<Product>())).ReturnsAsync((Product p) => p);

            var tool = new AtualizarProdutoTool(service.Object, repository.Object);

            var result = await Invoke(tool, new() { ["produtoId"] = id.ToString(), ["preco"] = 15m });

            Assert.DoesNotContain("erro", result);
            Assert.Equal(15m, stored.DefaultValue);
            Assert.Equal(7m, stored.StorageQuantity);
            service.Verify(x => x.UpdateAsync(It.IsAny<Product>()), Times.Once);
        }


        [Fact]
        public async Task AtualizarProduto_ResolvesAnIdFragmentToTheProductItStartsWith()
        {
            // The exact failure a live session hit: "5ecc3fd5" for 5ecc3fd5-496a-...
            var id = new Guid("5ecc3fd5-496a-4e79-83c6-da8633b96645");
            var tool = new AtualizarProdutoTool(
                Mock.Of<IProductService<Product>>(), ProductsNamed((id, "Monster Rosa")));

            var prepared = await tool.PrepareAsync(
                new Dictionary<string, object?> { ["produtoId"] = "5ecc3fd5", ["preco"] = 33m }, Context);

            Assert.Null(prepared.Error);
            Assert.Equal(id.ToString(), prepared.Arguments["produtoId"]);
        }

        [Fact]
        public async Task AtualizarProduto_AsksWhichOneWhenAFragmentMatchesTwoProducts()
        {
            var tool = new AtualizarProdutoTool(
                Mock.Of<IProductService<Product>>(),
                ProductsNamed(
                    (new Guid("aaaaaaaa-0000-0000-0000-000000000001"), "Primeiro"),
                    (new Guid("aaaaaaaa-0000-0000-0000-000000000002"), "Segundo")));

            var prepared = await tool.PrepareAsync(
                new Dictionary<string, object?> { ["produtoId"] = "aaaaaaaa", ["preco"] = 33m }, Context);

            Assert.Contains("mais de um produto começa com", prepared.Error);
        }

        private static IRepository<Product> ProductsNamed(params (Guid Id, string Name)[] rows)
        {
            var repository = new Mock<IRepository<Product>>();
            repository
                .Setup(x => x.GetPagedAsync(
                    It.IsAny<Expression<Func<Product, bool>>>(),
                    It.IsAny<Func<IQueryable<Product>, IOrderedQueryable<Product>>>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync(((IReadOnlyList<Product>)rows
                    .Select(row => new Product { Id = row.Id, Name = row.Name, EnterpriseId = Context.EnterpriseId, IsActive = true })
                    .ToList(), rows.Length));
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Product, bool>>>()))
                .ReturnsAsync((Expression<Func<Product, bool>> predicate) => rows
                    .Select(row => new Product
                    {
                        Id = row.Id,
                        Name = row.Name,
                        EnterpriseId = Context.EnterpriseId,
                        IsActive = true,
                    })
                    .Where(predicate.Compile()));

            return repository.Object;
        }

        private static IRepository<Customer> CustomersNamed(params (Guid Id, string Name)[] rows)
        {
            var repository = new Mock<IRepository<Customer>>();
            repository
                .Setup(x => x.GetPagedAsync(
                    It.IsAny<Expression<Func<Customer, bool>>>(),
                    It.IsAny<Func<IQueryable<Customer>, IOrderedQueryable<Customer>>>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync(((IReadOnlyList<Customer>)rows
                    .Select(row => new Customer { Id = row.Id, Name = row.Name, EnterpriseId = Context.EnterpriseId, IsActive = true })
                    .ToList(), rows.Length));
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Customer, bool>>>()))
                .ReturnsAsync((Expression<Func<Customer, bool>> predicate) => rows
                    .Select(row => new Customer
                    {
                        Id = row.Id,
                        Name = row.Name,
                        EnterpriseId = Context.EnterpriseId,
                        IsActive = true,
                    })
                    .Where(predicate.Compile()));

            return repository.Object;
        }

        private static async Task<string> Invoke(IAiTool tool, Dictionary<string, object?> arguments)
        {
            var value = await tool.Create(Context).InvokeAsync(new AIFunctionArguments(arguments));
            return value?.ToString() ?? string.Empty;
        }
    }
}
