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
    public class BuscarClientesToolTests
    {
        private static readonly Guid Tenant = Guid.NewGuid();

        private static Customer Row(string name, string? document = null, bool active = true) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = Tenant,
            IsActive = active,
            Name = name,
            Document = document,
        };

        [Fact]
        public void Tool_DeclaresItsNameAndPermission()
        {
            var tool = new BuscarClientesTool(new Mock<IRepository<Customer>>().Object);

            Assert.Equal("buscar_clientes", tool.Name);
            Assert.Equal("customers.read", tool.RequiredPermission);
            Assert.False(tool.RequiresConfirmation);
        }

        [Fact]
        public async Task Invoking_DoesNotReturnTheCustomersDocument()
        {
            var repository = PagedRepository(new[]
            {
                Row("Padaria do Zé", "12345678900"),
            });

            var json = await Invoke(repository, new AIFunctionArguments { ["termo"] = "Zé" });

            Assert.Contains("Padaria do Zé", json);
            Assert.DoesNotContain("12345678900", json);
        }

        [Fact]
        public async Task Invoking_WhenRepositoryThrowsNotFound_ReportsZeroRatherThanFailing()
        {
            var repository = new Mock<IRepository<Customer>>();
            repository
                .Setup(x => x.GetPagedAsync(
                    It.IsAny<Expression<Func<Customer, bool>>>(),
                    It.IsAny<Func<IQueryable<Customer>, IOrderedQueryable<Customer>>>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ThrowsAsync(new NotFoundDatabaseException());

            var json = await Invoke(repository, new AIFunctionArguments { ["termo"] = "nao existe" });

            Assert.Contains("\"total\":0", json);
        }

        [Fact]
        public async Task Invoking_WithNoTerm_ListsTheCustomersInsteadOfRefusing()
        {
            var repository = PagedRepository(new[]
            {
                Row("Padaria do Zé"),
                Row("Mercado Central"),
            });

            var json = await Invoke(repository, new AIFunctionArguments());

            Assert.Contains("Padaria do Zé", json);
            Assert.Contains("Mercado Central", json);
            Assert.DoesNotContain("\"erro\"", json);
        }

        [Fact]
        public async Task Invoking_DoesNotReturnSoftDeletedCustomers()
        {
            var repository = PagedRepository(new[]
            {
                Row("Padaria do Zé"),
                Row("Padaria Velha", active: false),
            });

            var json = await Invoke(repository, new AIFunctionArguments { ["termo"] = "Padaria" });

            Assert.Contains("Padaria do Zé", json);
            Assert.DoesNotContain("Padaria Velha", json);
        }

        [Fact]
        public async Task Invoking_WithMoreThanOnePage_SaysWhichPageToAskForNext()
        {
            var repository = PagedRepository(
                Enumerable.Range(1, 21).Select(i => Row($"Cliente {i:00}")).ToList());

            var json = await Invoke(repository, new AIFunctionArguments());

            Assert.Contains("\"total\":21", json);
            Assert.Contains("\"truncado\":true", json);
            Assert.Contains("\"proximaPagina\":2", json);
        }

        private static async Task<string> Invoke(
            Mock<IRepository<Customer>> repository, AIFunctionArguments arguments)
        {
            var tool = new BuscarClientesTool(repository.Object);
            var function = tool.Create(new AiToolContext(Tenant, Guid.NewGuid()));
            var result = await function.InvokeAsync(arguments);

            return result?.ToString() ?? string.Empty;
        }

        private static Mock<IRepository<Customer>> PagedRepository(IReadOnlyList<Customer> rows)
        {
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
                    var matched = predicate is null ? rows : rows.Where(predicate.Compile()).ToList();
                    var ordered = orderBy is null ? matched.AsQueryable() : orderBy(matched.AsQueryable());
                    var items = ordered
                        .Skip(((page ?? 1) - 1) * (size ?? matched.Count))
                        .Take(size ?? matched.Count)
                        .ToList();

                    return ((IReadOnlyList<Customer>)items, matched.Count);
                });

            return repository;
        }
    }
}
