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
        /// This solution soft-deletes (OrderService sets IsActive = false) and has no global query
        /// filter, so every read filters by hand or counts cancelled orders as revenue.
        /// </summary>
        [Fact]
        public async Task Invoking_DoesNotReturnSoftDeletedOrders()
        {
            var tenant = Guid.NewGuid();
            var rows = new[]
            {
                new Order { Id = Guid.NewGuid(), EnterpriseId = tenant, TotalValue = 42m, CreatedAt = DateTime.UtcNow, IsActive = true },
                new Order { Id = Guid.NewGuid(), EnterpriseId = tenant, TotalValue = 999m, CreatedAt = DateTime.UtcNow, IsActive = false },
            };
            var repository = new Mock<IRepository<Order>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>>()))
                .ReturnsAsync((Expression<Func<Order, bool>> predicate) => rows.Where(predicate.Compile()));

            var tool = new ConsultarPedidosTool(repository.Object);
            var function = tool.Create(new AiToolContext(tenant, Guid.NewGuid()));

            var result = await function.InvokeAsync(new AIFunctionArguments());

            var json = result?.ToString() ?? string.Empty;
            Assert.Contains("\"total\":1", json);
            Assert.DoesNotContain("999", json);
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
