using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Tools;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using Moq;

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

        private static async Task<string> Invoke(IAiTool tool, Dictionary<string, object?> arguments)
        {
            var value = await tool.Create(Context).InvokeAsync(new AIFunctionArguments(arguments));
            return value?.ToString() ?? string.Empty;
        }
    }
}
