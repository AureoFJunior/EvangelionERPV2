using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Tools;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using Moq;
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
        public async Task AtualizarProduto_RunsWithOnlyTheIdSupplied()
        {
            var tool = new AtualizarProdutoTool(
                Mock.Of<IProductService<Product>>(), Mock.Of<IRepository<Product>>());

            var result = await Invoke(tool, new() { ["produtoId"] = Guid.NewGuid().ToString() });

            Assert.Contains("informe preco ou estoque", result);
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


        [Theory]
        [InlineData("5ecc3fd5")]                                   // the id the model truncated
        [InlineData("Monster Rosa")]                               // the name instead of the id
        [InlineData("")]
        public void AtualizarProduto_RefusesAnIdThatIsNotAWholeGuid(string produtoId)
        {
            var tool = new AtualizarProdutoTool(
                Mock.Of<IProductService<Product>>(), Mock.Of<IRepository<Product>>());

            var invalid = tool.Validate(new Dictionary<string, object?>
            {
                ["produtoId"] = produtoId,
                ["preco"] = 15m,
            });

            Assert.NotNull(invalid);
            Assert.Contains("produtoId", invalid);
        }

        [Fact]
        public void AtualizarProduto_AcceptsAWholeGuidWithSomethingToChange()
        {
            var tool = new AtualizarProdutoTool(
                Mock.Of<IProductService<Product>>(), Mock.Of<IRepository<Product>>());

            var invalid = tool.Validate(new Dictionary<string, object?>
            {
                ["produtoId"] = Guid.NewGuid().ToString(),
                ["estoque"] = 30m,
            });

            Assert.Null(invalid);
        }

        [Fact]
        public void AtualizarProduto_RefusesACallThatChangesNothing()
        {
            var tool = new AtualizarProdutoTool(
                Mock.Of<IProductService<Product>>(), Mock.Of<IRepository<Product>>());

            var invalid = tool.Validate(new Dictionary<string, object?> { ["produtoId"] = Guid.NewGuid().ToString() });

            Assert.Equal("informe preco ou estoque.", invalid);
        }

        [Fact]
        public void AtualizarCliente_RefusesATruncatedId()
        {
            var tool = new AtualizarClienteTool(
                Mock.Of<ICustomerService<Customer>>(), Mock.Of<IRepository<Customer>>());

            Assert.NotNull(tool.Validate(new Dictionary<string, object?> { ["clienteId"] = "c704eb28" }));
        }

        [Fact]
        public void CriarCliente_RefusesAnEmptyName()
        {
            var tool = new CriarClienteTool(Mock.Of<ICustomerService<Customer>>());

            Assert.Equal("informe o nome do cliente.", tool.Validate(new Dictionary<string, object?> { ["nome"] = "  " }));
        }

        [Fact]
        public void CriarPedido_RefusesLinesWithATruncatedProductIdOrNoQuantity()
        {
            var tool = new CriarPedidoTool(
                Mock.Of<IOrderService<Order>>(),
                Mock.Of<IRepository<Product>>(),
                Mock.Of<IRepository<Customer>>());

            var truncated = tool.Validate(new Dictionary<string, object?>
            {
                ["clienteId"] = Guid.NewGuid().ToString(),
                ["itens"] = JsonSerializer.SerializeToElement(new[] { new { produtoId = "5ecc3fd5", quantidade = 2 } }),
            });

            var zeroed = tool.Validate(new Dictionary<string, object?>
            {
                ["clienteId"] = Guid.NewGuid().ToString(),
                ["itens"] = JsonSerializer.SerializeToElement(new[] { new { produtoId = Guid.NewGuid().ToString(), quantidade = 0 } }),
            });

            Assert.Contains("produtoId", truncated);
            Assert.Contains("quantidade", zeroed);
        }

        [Fact]
        public void CriarPedido_AcceptsAWellFormedOrder()
        {
            var tool = new CriarPedidoTool(
                Mock.Of<IOrderService<Order>>(),
                Mock.Of<IRepository<Product>>(),
                Mock.Of<IRepository<Customer>>());

            var invalid = tool.Validate(new Dictionary<string, object?>
            {
                ["clienteId"] = Guid.NewGuid().ToString(),
                ["itens"] = JsonSerializer.SerializeToElement(new[] { new { produtoId = Guid.NewGuid().ToString(), quantidade = 3 } }),
            });

            Assert.Null(invalid);
        }

        private static async Task<string> Invoke(IAiTool tool, Dictionary<string, object?> arguments)
        {
            var value = await tool.Create(Context).InvokeAsync(new AIFunctionArguments(arguments));
            return value?.ToString() ?? string.Empty;
        }
    }
}
