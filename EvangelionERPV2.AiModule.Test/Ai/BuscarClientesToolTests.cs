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
            var repository = new Mock<IRepository<Customer>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Customer, bool>>>()))
                .ReturnsAsync(new[]
                {
                    new Customer { Id = Guid.NewGuid(), Name = "Padaria do Zé", Document = "12345678900" },
                });

            var tool = new BuscarClientesTool(repository.Object);
            var function = tool.Create(new AiToolContext(Guid.NewGuid(), Guid.NewGuid()));

            var result = await function.InvokeAsync(new AIFunctionArguments { ["termo"] = "zé" });

            var json = result?.ToString() ?? string.Empty;
            Assert.Contains("Padaria do Zé", json);
            Assert.DoesNotContain("12345678900", json);
        }

        /// <summary>
        /// The real CustomerRepository throws NotFoundDatabaseException on zero matches instead of
        /// returning an empty list. A search matching nothing is a normal answer, not a failure.
        /// </summary>
        [Fact]
        public async Task Invoking_WhenRepositoryThrowsNotFound_ReportsZeroRatherThanFailing()
        {
            var repository = new Mock<IRepository<Customer>>();
            repository
                .Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Customer, bool>>>()))
                .ThrowsAsync(new NotFoundDatabaseException());

            var tool = new BuscarClientesTool(repository.Object);
            var function = tool.Create(new AiToolContext(Guid.NewGuid(), Guid.NewGuid()));

            var result = await function.InvokeAsync(new AIFunctionArguments { ["termo"] = "nao existe" });

            Assert.Contains("\"total\":0", result?.ToString() ?? string.Empty);
        }

        [Fact]
        public async Task Invoking_WithEmptyTerm_ReturnsErrorRatherThanEverything()
        {
            var repository = new Mock<IRepository<Customer>>();

            var tool = new BuscarClientesTool(repository.Object);
            var function = tool.Create(new AiToolContext(Guid.NewGuid(), Guid.NewGuid()));

            var result = await function.InvokeAsync(new AIFunctionArguments { ["termo"] = "" });

            Assert.Contains("\"erro\"", result?.ToString() ?? string.Empty);
            repository.Verify(
                x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Customer, bool>>>()),
                Times.Never);
        }
    }
}
