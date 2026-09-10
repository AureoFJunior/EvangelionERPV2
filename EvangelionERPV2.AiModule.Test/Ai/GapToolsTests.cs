using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Tools;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using Moq;
using System.Linq.Expressions;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    /// <summary>
    /// The tools added after a live session found questions the agent could not answer at all:
    /// who owes me, what do I owe this week, which customer buys most, and what is my ticket médio.
    /// </summary>
    public class GapToolsTests
    {
        private static readonly Guid Tenant = Guid.NewGuid();
        private static readonly AiToolContext Context = new(Tenant, Guid.NewGuid());

        [Fact]
        public async Task ClientesInadimplentes_ListsWhoIsOverdueAndByHowMuch()
        {
            var service = new Mock<IReceivablesReportService>();
            service
                .Setup(x => x.GetDelinquentAsync(Tenant, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[]
                {
                    new DelinquentCustomerDTO
                    {
                        CustomerId = Guid.NewGuid(),
                        CustomerName = "Padaria do Zé",
                        OverdueCount = 2,
                        OverdueAmount = 350.75m,
                        DaysOverdue = 12,
                        OldestDueDate = new DateTime(2026, 8, 29, 0, 0, 0, DateTimeKind.Utc),
                    },
                });

            var json = await Invoke(new ClientesInadimplentesTool(service.Object));

            Assert.Contains("Padaria do Zé", json);
            Assert.Contains("350.75", json);
            Assert.Contains("\"diasEmAtraso\":12", json);
        }

        [Fact]
        public async Task ClientesInadimplentes_PassesTheRequestedThreshold()
        {
            var service = new Mock<IReceivablesReportService>();
            service
                .Setup(x => x.GetDelinquentAsync(Tenant, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<DelinquentCustomerDTO>());

            await Invoke(new ClientesInadimplentesTool(service.Object), new AIFunctionArguments { ["diasDeAtraso"] = 30 });

            service.Verify(x => x.GetDelinquentAsync(Tenant, 30, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ContasAPagar_IncludesWhatIsAlreadyOverdueAndFlagsIt()
        {
            var repository = new Mock<IRepository<PayableBill>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>>()))
                .ReturnsAsync(new[]
                {
                    Bill("Aluguel", 3974.60m, DateTime.UtcNow.Date.AddDays(-5)),
                    Bill("Energia", 512.30m, DateTime.UtcNow.Date.AddDays(3)),
                });

            var json = await Invoke(new ContasAPagarTool(repository.Object));
            using var document = JsonDocument.Parse(json);
            var items = document.RootElement.GetProperty("itens");

            Assert.Equal("Aluguel", items[0].GetProperty("descricao").GetString());
            Assert.True(items[0].GetProperty("vencida").GetBoolean());
            Assert.Equal(5, items[0].GetProperty("diasEmAtraso").GetInt32());
            Assert.False(items[1].GetProperty("vencida").GetBoolean());
        }

        [Fact]
        public async Task ContasAPagar_WhenNothingIsDue_SaysZeroRatherThanFailing()
        {
            var repository = new Mock<IRepository<PayableBill>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>>()))
                .ThrowsAsync(new NotFoundDatabaseException());

            Assert.Contains("\"total\":0", await Invoke(new ContasAPagarTool(repository.Object)));
        }

        [Fact]
        public async Task MelhoresClientes_RanksByWhatEachCustomerSpent()
        {
            var padaria = Guid.NewGuid();
            var mercado = Guid.NewGuid();
            var repository = OrdersReturning(new[]
            {
                Order(padaria, "Padaria do Zé", 100m),
                Order(padaria, "Padaria do Zé", 250m),
                Order(mercado, "Mercado Central", 300m),
            });

            var json = await Invoke(new MelhoresClientesTool(repository.Object));
            using var document = JsonDocument.Parse(json);
            var items = document.RootElement.GetProperty("itens");

            Assert.Equal("Padaria do Zé", items[0].GetProperty("nome").GetString());
            Assert.Equal(350m, items[0].GetProperty("totalComprado").GetDecimal());
            Assert.Equal(2, items[0].GetProperty("pedidos").GetInt32());
            Assert.Equal("Mercado Central", items[1].GetProperty("nome").GetString());
        }

        [Fact]
        public async Task MelhoresClientes_IgnoresRefundedOrders()
        {
            var padaria = Guid.NewGuid();
            var refunded = Order(padaria, "Padaria do Zé", 9999m);
            refunded.RefundedAt = DateTime.UtcNow;

            var repository = OrdersReturning(new[] { Order(padaria, "Padaria do Zé", 10m), refunded });

            var json = await Invoke(new MelhoresClientesTool(repository.Object));
            using var document = JsonDocument.Parse(json);

            Assert.Equal(10m, document.RootElement.GetProperty("itens")[0].GetProperty("totalComprado").GetDecimal());
        }

        [Fact]
        public async Task ConsultarPedidos_ReportsTheWindowsTotalAndTicketMedio()
        {
            var customer = Guid.NewGuid();
            var repository = OrdersReturning(new[]
            {
                Order(customer, "Padaria do Zé", 100m),
                Order(customer, "Padaria do Zé", 300m),
            });

            var json = await Invoke(new ConsultarPedidosTool(repository.Object));
            using var document = JsonDocument.Parse(json);
            var summary = document.RootElement.GetProperty("resumo");

            Assert.Equal(2, summary.GetProperty("pedidos").GetInt32());
            Assert.Equal(400m, summary.GetProperty("faturamento").GetDecimal());
            Assert.Equal(200m, summary.GetProperty("ticketMedio").GetDecimal());
        }

        [Fact]
        public async Task ConsultarPedidos_LeavesRefundedOrdersOutOfTheTotals()
        {
            var customer = Guid.NewGuid();
            var refunded = Order(customer, "Padaria do Zé", 500m);
            refunded.Status = (int)EnumOrderStatus.Refund;

            var repository = OrdersReturning(new[] { Order(customer, "Padaria do Zé", 100m), refunded });

            var json = await Invoke(new ConsultarPedidosTool(repository.Object));
            using var document = JsonDocument.Parse(json);
            var summary = document.RootElement.GetProperty("resumo");

            // Both rows are still listed; only the money excludes the estorno.
            Assert.Equal(2, document.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(100m, summary.GetProperty("faturamento").GetDecimal());
            Assert.Equal(100m, summary.GetProperty("ticketMedio").GetDecimal());
        }

        private static async Task<string> Invoke(IAiTool tool, AIFunctionArguments? arguments = null)
        {
            var result = await tool.Create(Context).InvokeAsync(arguments ?? new AIFunctionArguments());

            return result?.ToString() ?? string.Empty;
        }

        private static Mock<IRepository<Order>> OrdersReturning(IReadOnlyList<Order> orders)
        {
            var repository = new Mock<IRepository<Order>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>>()))
                .ReturnsAsync((Expression<Func<Order, bool>> predicate) => orders.Where(predicate.Compile()));

            return repository;
        }

        private static Order Order(Guid customerId, string customerName, decimal total) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = Tenant,
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            Status = (int)EnumOrderStatus.Finished,
            CustomerId = customerId,
            Customer = new Customer { Id = customerId, Name = customerName },
            TotalValue = total,
        };

        private static PayableBill Bill(string description, decimal amount, DateTime dueDate) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = Tenant,
            IsActive = true,
            Description = description,
            Amount = amount,
            DueDate = dueDate,
            IsPaid = false,
        };
    }
}
