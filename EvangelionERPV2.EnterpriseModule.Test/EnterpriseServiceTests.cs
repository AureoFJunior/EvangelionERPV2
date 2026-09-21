using EvangelionERPV2.EnterpriseModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;

using Moq;

namespace EvangelionERPV2.EnterpriseModule.Test
{
    /// <summary>
    /// The enterprise row is the tenant every other table hangs off, and it carries
    /// <c>CurrentBalance</c>. These tests pin the three things this service actually decides:
    /// the currency it stores, the id it assigns, and what happens when the row is not there.
    /// </summary>
    public class EnterpriseServiceTests
    {
        #region Currency

        [Theory]
        [InlineData(null, "BRL")]
        [InlineData("", "BRL")]
        [InlineData("   ", "BRL")]
        [InlineData("usd", "USD")]
        [InlineData("  eur  ", "EUR")]
        public async Task CreateAsync_NormalizesTheCurrency(string? provided, string expected)
        {
            var context = new ServiceContext();

            var created = await context.Service.CreateAsync(new Enterprise { Name = "NERV", Currency = provided! });

            Assert.Equal(expected, created.Currency);
        }

        [Fact]
        public async Task CreateAsync_AssignsItsOwnId()
        {
            var context = new ServiceContext();
            var attemptedId = Guid.NewGuid();

            var created = await context.Service.CreateAsync(new Enterprise { Id = attemptedId, Name = "NERV" });

            Assert.NotEqual(attemptedId, created.Id);
            Assert.NotEqual(Guid.Empty, created.Id);
        }

        [Fact]
        public void Update_WithABlankCurrency_KeepsTheStoredOne()
        {
            var stored = new Enterprise { Id = Guid.NewGuid(), Name = "NERV", Currency = "USD", IsActive = true };
            var context = new ServiceContext(stored);

            var updated = context.Service.Update(new Enterprise { Id = stored.Id, Name = "NERV", Currency = "  " });

            Assert.Equal("USD", updated.Currency);
        }

        [Fact]
        public void Update_WithANewCurrency_NormalizesIt()
        {
            var stored = new Enterprise { Id = Guid.NewGuid(), Name = "NERV", Currency = "BRL", IsActive = true };
            var context = new ServiceContext(stored);

            var updated = context.Service.Update(new Enterprise { Id = stored.Id, Name = "NERV", Currency = "jpy" });

            Assert.Equal("JPY", updated.Currency);
        }

        #endregion

        #region Missing rows

        [Fact]
        public void Update_OfAnEnterpriseThatIsNotThere_ThrowsNotFound()
        {
            var context = new ServiceContext();

            Assert.Throws<NotFoundDatabaseException>(
                () => context.Service.Update(new Enterprise { Id = Guid.NewGuid(), Name = "NERV" }));
        }

        [Fact]
        public void Delete_OfAnEnterpriseThatIsNotThere_ThrowsNotFound()
        {
            var context = new ServiceContext();

            Assert.Throws<NotFoundDatabaseException>(() => context.Service.Delete(Guid.NewGuid()));
        }

        #endregion

        #region Delete

        [Fact]
        public void Delete_DeactivatesTheRowRatherThanRemovingIt()
        {
            var stored = new Enterprise { Id = Guid.NewGuid(), Name = "NERV", IsActive = true };
            var context = new ServiceContext(stored);

            var deleted = context.Service.Delete(stored.Id);

            Assert.False(deleted.IsActive);
            Assert.NotNull(deleted.UpdatedAt);
            Assert.Equal(0, context.HardDeletes);
        }

        #endregion

        private sealed class ServiceContext
        {
            private readonly List<Enterprise> _enterprises = [];

            public EnterpriseService Service { get; }
            public int HardDeletes { get; private set; }

            public ServiceContext(params Enterprise[] stored)
            {
                _enterprises.AddRange(stored);

                var repository = new Mock<IRepository<Enterprise>>();

                repository.Setup(x => x.CreateAsync(It.IsAny<Enterprise>()))
                    .ReturnsAsync((Enterprise enterprise) =>
                    {
                        _enterprises.Add(enterprise);
                        return enterprise;
                    });

                repository.Setup(x => x.GetById(It.IsAny<Guid>()))
                    .Returns((Guid id) => _enterprises.FirstOrDefault(x => x.Id == id)!);

                repository.Setup(x => x.Update(It.IsAny<Enterprise>()))
                    .Returns((Enterprise enterprise) => enterprise);

                repository.Setup(x => x.Delete(It.IsAny<Enterprise>()))
                    .Returns((Enterprise enterprise) =>
                    {
                        HardDeletes++;
                        return enterprise;
                    });

                repository.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

                Service = new EnterpriseService(repository.Object);
            }
        }
    }
}
