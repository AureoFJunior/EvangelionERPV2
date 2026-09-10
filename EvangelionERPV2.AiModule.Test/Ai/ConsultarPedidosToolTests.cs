using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Tools;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using Moq;
using System.Linq.Expressions;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    public class ConsultarPedidosToolTests
    {
        [Fact]
        public void Tool_DeclaresItsNameAndPermission()
        {
            var tool = new ConsultarPedidosTool(new Mock<IRepository<Order>>().Object);

            Assert.Equal("consultar_pedidos", tool.Name);
            Assert.Equal("orders.read", tool.RequiredPermission);
            Assert.False(tool.RequiresConfirmation);
        }

        [Fact]
        public async Task Invoking_ReturnsOrdersWithACastStatusRatherThanARawInteger()
        {
            var repository = new Mock<IRepository<Order>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>>()))
                .ReturnsAsync(new[]
                {
                    new Order { Id = Guid.NewGuid(), TotalValue = 42m, Status = (int)EnumOrderStatus.Paid },
                });

            var tool = new ConsultarPedidosTool(repository.Object);
            var function = tool.Create(new AiToolContext(Guid.NewGuid(), Guid.NewGuid()));

            var result = await function.InvokeAsync(new AIFunctionArguments());

            var json = result?.ToString() ?? string.Empty;
            Assert.Contains("\"total\":1", json);
            Assert.Contains("\"status\":\"Paid\"", json);
            Assert.DoesNotContain("\"status\":1", json);
        }

        /// <summary>
        /// The real OrderRepository throws NotFoundDatabaseException on zero matches instead of
        /// returning an empty list. A period with no orders is a normal answer, not a failure.
        /// </summary>
        [Fact]
        public async Task Invoking_WhenRepositoryThrowsNotFound_ReportsZeroRatherThanFailing()
        {
            var repository = new Mock<IRepository<Order>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>>()))
                .ThrowsAsync(new NotFoundDatabaseException());

            var tool = new ConsultarPedidosTool(repository.Object);
            var function = tool.Create(new AiToolContext(Guid.NewGuid(), Guid.NewGuid()));

            var result = await function.InvokeAsync(new AIFunctionArguments());

            Assert.Contains("\"total\":0", result?.ToString() ?? string.Empty);
        }
    }
}
